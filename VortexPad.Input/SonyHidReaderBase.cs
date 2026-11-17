using HidSharp;
using VortexPad.Core.Hid;
using VortexPad.Core.Models;

namespace VortexPad.Input;

/// <summary>
/// Base for native HID readers of Sony controllers (USB or Bluetooth), each on its own thread.
/// The 1000 Hz loop never blocks: it only copies the latest parsed snapshot.
/// If the controller disconnects, it retries opening it every second; while the IMU calibration
/// could not be read from the device, it retries it every few seconds from the reader thread.
/// </summary>
public abstract class SonyHidReaderBase<TCalibration> : IDisposable
    where TCalibration : ImuCalibration
{
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan CalibrationRetryDelay = TimeSpan.FromSeconds(3);

    private readonly string _devicePath;
    private readonly object _sync = new();
    private readonly PhysicalInputState _latest = new();
    private Thread? _thread;
    private volatile bool _running;
    private HidStream? _stream;
    private long _reportsReceived;

    public HidConnection Connection { get; private set; }
    public bool IsConnected { get; private set; }
    public bool HasDeviceCalibration { get; private set; }
    public long ReportsReceived => Interlocked.Read(ref _reportsReceived);

    /// <summary>Name shown in the status text and the thread name, e.g. "DualSense".</summary>
    protected abstract string DeviceLabel { get; }

    /// <summary>Nominal calibration used until (or if) the device provides its own.</summary>
    protected abstract TCalibration NominalCalibration { get; }

    /// <summary>Reads the IMU calibration feature report. Must not throw; returns the nominal one on failure.</summary>
    protected abstract TCalibration ReadCalibration(HidDevice device, HidStream stream);

    /// <summary>Parses one input report into <paramref name="state"/>.</summary>
    protected abstract bool TryParseReport(
        ReadOnlySpan<byte> report, TCalibration calibration, PhysicalInputState state, out HidConnection connection);

    public string StatusText
    {
        get
        {
            if (!IsConnected) return $"{DeviceLabel} HID: desconectado (reintentando…)";
            string link = Connection switch
            {
                HidConnection.Usb => "USB",
                HidConnection.Bluetooth => "Bluetooth",
                _ => "?"
            };
            string cal = HasDeviceCalibration ? "IMU calibrada" : "IMU nominal";
            return $"{DeviceLabel} HID nativo ({link}, {cal})";
        }
    }

    protected SonyHidReaderBase(string devicePath)
    {
        _devicePath = devicePath ?? throw new ArgumentNullException(nameof(devicePath));
    }

    public void Start()
    {
        if (_running) return;
        _running = true;
        _thread = new Thread(ReadLoop)
        {
            Name = $"VortexPad {DeviceLabel} HID Reader",
            IsBackground = true,
            Priority = ThreadPriority.AboveNormal
        };
        _thread.Start();
    }

    public void Stop()
    {
        _running = false;
        CloseStream(); // unblocks a pending Read
        _thread?.Join(500);
        _thread = null;
        IsConnected = false;
    }

    /// <summary>
    /// Copies the latest sticks, triggers, buttons and IMU into <paramref name="target"/>.
    /// Returns false if no valid report has arrived yet or the controller is disconnected.
    /// </summary>
    public bool CopyLatestInto(PhysicalInputState target)
    {
        lock (_sync)
        {
            if (!IsConnected || ReportsReceived == 0) return false;
            CopyState(_latest, target);
            return true;
        }
    }

    /// <summary>Reads a feature report into a buffer sized for the device. Returns null on failure.</summary>
    protected static byte[]? TryGetFeature(HidDevice device, HidStream stream, byte reportId, int minLength)
    {
        try
        {
            int len = Math.Max(device.GetMaxFeatureReportLength(), minLength);
            var feature = new byte[len];
            feature[0] = reportId;
            stream.GetFeature(feature);
            return feature;
        }
        catch
        {
            return null;
        }
    }

    private void ReadLoop()
    {
        var scratch = new PhysicalInputState();

        while (_running)
        {
            if (!TryOpen(out var device, out var stream, out var calibration, out int reportLength))
            {
                IsConnected = false;
                SleepWhileRunning(ReconnectDelay);
                continue;
            }

            var buffer = new byte[reportLength];
            var nextCalibrationRetry = DateTime.UtcNow + CalibrationRetryDelay;
            IsConnected = true;

            try
            {
                while (_running)
                {
                    if (!calibration.IsFromDevice && DateTime.UtcNow >= nextCalibrationRetry)
                    {
                        // E.g. wireless adapter with no paired controller at open time, or BT link still settling
                        calibration = ReadCalibration(device, stream);
                        HasDeviceCalibration = calibration.IsFromDevice;
                        nextCalibrationRetry = DateTime.UtcNow + CalibrationRetryDelay;
                    }

                    int read;
                    try
                    {
                        read = stream.Read(buffer, 0, buffer.Length);
                    }
                    catch (TimeoutException)
                    {
                        continue;
                    }

                    if (read <= 0) continue;

                    if (TryParseReport(buffer.AsSpan(0, read), calibration, scratch, out var connection))
                    {
                        lock (_sync)
                        {
                            Connection = connection;
                            CopyState(scratch, _latest);
                        }
                        Interlocked.Increment(ref _reportsReceived);
                    }
                }
            }
            catch (Exception) when (_running)
            {
                // IOException / ObjectDisposedException: controller unplugged or BT link lost → reconnect
            }
            finally
            {
                IsConnected = false;
                CloseStream();
            }

            SleepWhileRunning(ReconnectDelay);
        }
    }

    private bool TryOpen(out HidDevice device, out HidStream stream, out TCalibration calibration, out int reportLength)
    {
        device = null!;
        stream = null!;
        calibration = NominalCalibration;
        reportLength = 0;

        try
        {
            var found = DeviceList.Local.GetHidDevices()
                .FirstOrDefault(d => string.Equals(d.DevicePath, _devicePath, StringComparison.OrdinalIgnoreCase));
            if (found == null || !found.TryOpen(out var opened))
            {
                return false;
            }

            opened.ReadTimeout = 250;
            reportLength = Math.Max(found.GetMaxInputReportLength(), 64);

            // Reading the calibration feature report also switches Bluetooth controllers to the full report (with IMU)
            calibration = ReadCalibration(found, opened);
            HasDeviceCalibration = calibration.IsFromDevice;

            lock (_sync)
            {
                _stream = opened;
            }
            device = found;
            stream = opened;
            return true;
        }
        catch
        {
            CloseStream();
            return false;
        }
    }

    private static void CopyState(PhysicalInputState from, PhysicalInputState to)
    {
        to.LX = from.LX;
        to.LY = from.LY;
        to.RX = from.RX;
        to.RY = from.RY;
        to.L2 = from.L2;
        to.R2 = from.R2;
        to.GyroYaw = from.GyroYaw;
        to.GyroPitch = from.GyroPitch;
        to.GyroRoll = from.GyroRoll;
        to.AccelX = from.AccelX;
        to.AccelY = from.AccelY;
        to.AccelZ = from.AccelZ;
        to.PressedButtons.Clear();
        to.PressedButtons.UnionWith(from.PressedButtons);
    }

    private void SleepWhileRunning(TimeSpan duration)
    {
        var until = DateTime.UtcNow + duration;
        while (_running && DateTime.UtcNow < until)
        {
            Thread.Sleep(50);
        }
    }

    private void CloseStream()
    {
        HidStream? s;
        lock (_sync)
        {
            s = _stream;
            _stream = null;
        }
        try { s?.Dispose(); } catch { }
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }
}
