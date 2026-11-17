using HidSharp;
using VortexPad.Core.Hid;
using VortexPad.Core.Models;

namespace VortexPad.Input;

/// <summary>
/// Reads a DualSense / DualSense Edge over native HID (USB or Bluetooth) on its own thread.
/// The 1000 Hz loop never blocks: it only copies the latest parsed snapshot.
/// If the controller disconnects, it retries opening it every second.
/// </summary>
public sealed class DualSenseHidReader : IDisposable
{
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(1);

    private readonly string _devicePath;
    private readonly object _sync = new();
    private readonly PhysicalInputState _latest = new();
    private Thread? _thread;
    private volatile bool _running;
    private HidStream? _stream;

    public DualSenseConnection Connection { get; private set; }
    public bool IsConnected { get; private set; }
    public bool HasDeviceCalibration { get; private set; }
    public long ReportsReceived => Interlocked.Read(ref _reportsReceived);
    private long _reportsReceived;

    public string StatusText
    {
        get
        {
            if (!IsConnected) return "DualSense HID: desconectado (reintentando…)";
            string link = Connection switch
            {
                DualSenseConnection.Usb => "USB",
                DualSenseConnection.Bluetooth => "Bluetooth",
                _ => "?"
            };
            string cal = HasDeviceCalibration ? "IMU calibrada" : "IMU nominal";
            return $"DualSense HID nativo ({link}, {cal})";
        }
    }

    public DualSenseHidReader(string devicePath)
    {
        _devicePath = devicePath ?? throw new ArgumentNullException(nameof(devicePath));
    }

    public void Start()
    {
        if (_running) return;
        _running = true;
        _thread = new Thread(ReadLoop)
        {
            Name = "VortexPad DualSense HID Reader",
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

            target.LX = _latest.LX;
            target.LY = _latest.LY;
            target.RX = _latest.RX;
            target.RY = _latest.RY;
            target.L2 = _latest.L2;
            target.R2 = _latest.R2;
            target.GyroYaw = _latest.GyroYaw;
            target.GyroPitch = _latest.GyroPitch;
            target.GyroRoll = _latest.GyroRoll;
            target.AccelX = _latest.AccelX;
            target.AccelY = _latest.AccelY;
            target.AccelZ = _latest.AccelZ;
            target.PressedButtons.Clear();
            target.PressedButtons.UnionWith(_latest.PressedButtons);
            return true;
        }
    }

    private void ReadLoop()
    {
        var scratch = new PhysicalInputState();

        while (_running)
        {
            if (!TryOpen(out var stream, out var calibration, out int reportLength))
            {
                IsConnected = false;
                SleepWhileRunning(ReconnectDelay);
                continue;
            }

            var buffer = new byte[reportLength];
            IsConnected = true;

            try
            {
                while (_running)
                {
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

                    if (DualSenseReportParser.TryParseInputReport(
                            buffer.AsSpan(0, read), calibration, scratch, out var connection))
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

    private bool TryOpen(out HidStream stream, out DualSenseCalibration calibration, out int reportLength)
    {
        stream = null!;
        calibration = DualSenseCalibration.Nominal;
        reportLength = 0;

        try
        {
            var device = DeviceList.Local.GetHidDevices()
                .FirstOrDefault(d => string.Equals(d.DevicePath, _devicePath, StringComparison.OrdinalIgnoreCase));
            if (device == null || !device.TryOpen(out var opened))
            {
                return false;
            }

            opened.ReadTimeout = 250;
            reportLength = Math.Max(device.GetMaxInputReportLength(), 64);

            // Reading feature 0x05 provides the IMU calibration and, over Bluetooth,
            // also switches the controller to the full 0x31 report (with gyro).
            calibration = ReadCalibration(device, opened);
            HasDeviceCalibration = calibration.IsFromDevice;

            lock (_sync)
            {
                _stream = opened;
            }
            stream = opened;
            return true;
        }
        catch
        {
            CloseStream();
            return false;
        }
    }

    private static DualSenseCalibration ReadCalibration(HidDevice device, HidStream stream)
    {
        try
        {
            int len = Math.Max(device.GetMaxFeatureReportLength(), DualSenseCalibration.FeatureReportMinLength);
            var feature = new byte[len];
            feature[0] = DualSenseCalibration.FeatureReportId;
            stream.GetFeature(feature);
            return DualSenseCalibration.TryParse(feature, out var cal) ? cal : DualSenseCalibration.Nominal;
        }
        catch
        {
            return DualSenseCalibration.Nominal;
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
    }
}
