using HidSharp;
using VortexPad.Core.Diagnostics;

namespace VortexPad.Input;

/// <summary>
/// Detects controllers being connected and disconnected (hot-plug) through HidSharp's device list.
/// Windows fires several notifications per connection (one per HID interface), so they are grouped:
/// <see cref="DevicesChanged"/> fires once, <see cref="DebounceDelay"/> after the last change.
/// The event arrives on a ThreadPool thread.
/// </summary>
public sealed class HidDeviceWatcher : IDisposable
{
    private readonly Timer _debounce;
    private bool _started;

    public HidDeviceWatcher()
    {
        _debounce = new Timer(_ => Raise(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public TimeSpan DebounceDelay { get; init; } = TimeSpan.FromMilliseconds(750);

    public event Action? DevicesChanged;

    public void Start()
    {
        if (_started) return;
        _started = true;
        DeviceList.Local.Changed += OnDeviceListChanged;
    }

    private void OnDeviceListChanged(object? sender, DeviceListChangedEventArgs e)
    {
        // Restarts the countdown with every notification of the same burst
        _debounce.Change(DebounceDelay, Timeout.InfiniteTimeSpan);
    }

    private void Raise()
    {
        VortexLog.Info("HotPlug", "Cambió la lista de dispositivos HID; actualizando mandos.");
        try
        {
            DevicesChanged?.Invoke();
        }
        catch (Exception ex)
        {
            VortexLog.Error("HotPlug", "Error al procesar el cambio de dispositivos.", ex);
        }
    }

    public void Dispose()
    {
        if (_started) DeviceList.Local.Changed -= OnDeviceListChanged;
        _debounce.Dispose();
    }
}
