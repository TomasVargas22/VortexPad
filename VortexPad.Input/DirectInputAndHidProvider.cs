using System.Diagnostics;
using HidSharp;
using Vortice.DirectInput;
using VortexPad.Core.Models;

namespace VortexPad.Input;

public sealed record DetectedPhysicalDevice(
    string UniqueId,
    string DisplayName,
    int VendorId,
    int ProductId,
    string DeviceInstancePath,
    bool IsDirectInput,
    bool IsHid,
    Guid DirectInputGuid);

public sealed class DirectInputAndHidProvider : IDisposable
{
    private IDirectInput8? _directInput;
    private IDirectInputDevice8? _activeDInputDevice;
    private Guid _activeDeviceGuid;
    private readonly object _sync = new();

    // Optional manual/simulated override for live interactive testing in UI when no controller is plugged in
    public bool EnableInteractiveDemoSignal { get; set; }
    private readonly Stopwatch _demoClock = Stopwatch.StartNew();

    public DirectInputAndHidProvider()
    {
        // Lazy-initialize DirectInput on background/poll thread so UI startup is instantaneous
    }

    private void EnsureDirectInputInitialized()
    {
        if (_directInput != null) return;
        try
        {
            _directInput = DInput.DirectInput8Create();
        }
        catch
        {
            _directInput = null;
        }
    }

    public IReadOnlyList<DetectedPhysicalDevice> DiscoverDevices()
    {
        var results = new List<DetectedPhysicalDevice>();
        EnsureDirectInputInitialized();

        // 1. Enumerate via Vortice.DirectInput
        if (_directInput != null)
        {
            try
            {
                var devices = _directInput.GetDevices(DeviceClass.GameControl, DeviceEnumerationFlags.AttachedOnly);
                foreach (var dev in devices)
                {
                    int vid = dev.ProductGuid.ToByteArray()[0] | (dev.ProductGuid.ToByteArray()[1] << 8);
                    int pid = dev.ProductGuid.ToByteArray()[2] | (dev.ProductGuid.ToByteArray()[3] << 8);

                    results.Add(new DetectedPhysicalDevice(
                        UniqueId: dev.InstanceGuid.ToString(),
                        DisplayName: string.IsNullOrWhiteSpace(dev.ProductName) ? "DirectInput Controller" : dev.ProductName.Trim(),
                        VendorId: vid,
                        ProductId: pid,
                        DeviceInstancePath: $"HID\\VID_{vid:X4}&PID_{pid:X4}",
                        IsDirectInput: true,
                        IsHid: true,
                        DirectInputGuid: dev.InstanceGuid));
                }
            }
            catch
            {
                // Ignore enumeration error on headless/restricted systems
            }
        }

        // 2. Enumerate HID gamepads via HidSharp (UsagePage 0x01 Generic Desktop, Usage 0x04 Joystick or 0x05 Gamepad)
        try
        {
            foreach (var hid in DeviceList.Local.GetHidDevices())
            {
                if (hid.VendorID == 0x054C || // Sony DualShock / DualSense
                    hid.VendorID == 0x057E)   // Nintendo Switch Pro
                {
                    bool alreadyAdded = results.Any(r => r.VendorId == hid.VendorID && r.ProductId == hid.ProductID);
                    if (!alreadyAdded)
                    {
                        string name = SafeGetProductName(hid);
                        results.Add(new DetectedPhysicalDevice(
                            UniqueId: $"hid-{hid.VendorID:X4}-{hid.ProductID:X4}",
                            DisplayName: name,
                            VendorId: hid.VendorID,
                            ProductId: hid.ProductID,
                            DeviceInstancePath: hid.DevicePath,
                            IsDirectInput: false,
                            IsHid: true,
                            DirectInputGuid: Guid.Empty));
                    }
                }
            }
        }
        catch
        {
            // Ignore HID enumeration issues
        }

        // Always provide a Virtual Test / Calibration Device entry so the user can test curves/macros without hardware plugged in
        results.Add(new DetectedPhysicalDevice(
            UniqueId: "vortexpad-virtual-test-pad",
            DisplayName: "VortexPad Interactive Test Controller (Simulador en Vivo)",
            VendorId: 0x054C,
            ProductId: 0x0CE6,
            DeviceInstancePath: @"HID\VID_054C&PID_0CE6\VIRTUAL_TEST",
            IsDirectInput: false,
            IsHid: true,
            DirectInputGuid: Guid.Empty));

        return results;
    }

    public bool SelectDevice(DetectedPhysicalDevice? device)
    {
        lock (_sync)
        {
            ReleaseActiveDInputDevice();

            if (device == null || !device.IsDirectInput || device.DirectInputGuid == Guid.Empty || _directInput == null)
            {
                _activeDeviceGuid = Guid.Empty;
                return false;
            }

            try
            {
                _activeDInputDevice = _directInput.CreateDevice(device.DirectInputGuid);
                _activeDInputDevice.SetDataFormat<RawJoystickState>();
                _activeDInputDevice.Acquire();
                _activeDeviceGuid = device.DirectInputGuid;
                return true;
            }
            catch
            {
                ReleaseActiveDInputDevice();
                return false;
            }
        }
    }

    public PhysicalInputState PollCurrentState(DetectedPhysicalDevice? selectedDevice)
    {
        var state = new PhysicalInputState
        {
            DeviceId = selectedDevice?.UniqueId ?? "none",
            DeviceName = selectedDevice?.DisplayName ?? "None",
            VendorId = selectedDevice?.VendorId ?? 0,
            ProductId = selectedDevice?.ProductId ?? 0,
            TimestampTicks = Stopwatch.GetTimestamp()
        };

        if (EnableInteractiveDemoSignal || selectedDevice?.UniqueId == "vortexpad-virtual-test-pad")
        {
            PopulateDemoSignal(state);
            return state;
        }

        lock (_sync)
        {
            if (_activeDInputDevice == null)
            {
                return state;
            }

            try
            {
                _activeDInputDevice.Poll();
                var joy = _activeDInputDevice.GetCurrentJoystickState();

                // Normalize [0..65535] to [-1.0..1.0] (Note: DirectInput Y axis is top=0, bottom=65535, so we invert Y for standard cartesian up=+1)
                state.LX = NormalizeAxisSigned(joy.X);
                state.LY = -NormalizeAxisSigned(joy.Y);
                state.RX = NormalizeAxisSigned(joy.RotationX != 0 ? joy.RotationX : joy.Z);
                state.RY = -NormalizeAxisSigned(joy.RotationY != 0 ? joy.RotationY : joy.RotationZ);

                // Triggers
                state.L2 = NormalizeTriggerUnsigned(joy.Sliders[0]);
                state.R2 = NormalizeTriggerUnsigned(joy.Sliders[1]);

                // Buttons
                bool[] buttons = joy.Buttons;
                if (buttons is { Length: >= 12 })
                {
                    if (buttons[0]) state.PressedButtons.Add(ControllerButton.South);
                    if (buttons[1]) state.PressedButtons.Add(ControllerButton.East);
                    if (buttons[2]) state.PressedButtons.Add(ControllerButton.West);
                    if (buttons[3]) state.PressedButtons.Add(ControllerButton.North);
                    if (buttons[4]) state.PressedButtons.Add(ControllerButton.LeftShoulder);
                    if (buttons[5]) state.PressedButtons.Add(ControllerButton.RightShoulder);
                    if (buttons[6]) state.PressedButtons.Add(ControllerButton.Back);
                    if (buttons[7]) state.PressedButtons.Add(ControllerButton.Start);
                    if (buttons[8]) state.PressedButtons.Add(ControllerButton.LeftThumb);
                    if (buttons[9]) state.PressedButtons.Add(ControllerButton.RightThumb);
                    if (buttons[10]) state.PressedButtons.Add(ControllerButton.Guide);
                    if (buttons[11]) state.PressedButtons.Add(ControllerButton.TouchpadClick);
                }

                // POV Hat (D-Pad) in hundredths of a degree (0=Up, 9000=Right, 18000=Down, 27000=Left, -1=Centered)
                int pov = joy.PointOfViewControllers is { Length: > 0 } ? joy.PointOfViewControllers[0] : -1;
                if (pov >= 0 && pov < 36000)
                {
                    if (pov is >= 31500 or <= 4500) state.PressedButtons.Add(ControllerButton.DPadUp);
                    if (pov is >= 4500 and <= 13500) state.PressedButtons.Add(ControllerButton.DPadRight);
                    if (pov is >= 13500 and <= 22500) state.PressedButtons.Add(ControllerButton.DPadDown);
                    if (pov is >= 22500 and <= 31500) state.PressedButtons.Add(ControllerButton.DPadLeft);
                }
            }
            catch
            {
                // Try re-acquiring on next poll
                try { _activeDInputDevice?.Acquire(); } catch { }
            }
        }

        return state;
    }

    private void PopulateDemoSignal(PhysicalInputState state)
    {
        if (!EnableInteractiveDemoSignal)
        {
            // Subtle realistic sensor micro-noise at rest so AI Drift Calibration can measure real resting samples
            double tRest = _demoClock.Elapsed.TotalSeconds;
            state.LX = Math.Sin(tRest * 7.3) * 0.018;
            state.LY = Math.Cos(tRest * 5.9) * 0.015;
            state.RX = Math.Sin(tRest * 6.1) * 0.021;
            state.RY = Math.Cos(tRest * 8.4) * 0.017;
            return;
        }

        double t = _demoClock.Elapsed.TotalSeconds;
        state.LX = Math.Sin(t * 1.8) * 0.85;
        state.LY = Math.Cos(t * 1.8) * 0.85;
        state.RX = Math.Sin(t * 0.9) * 0.65;
        state.RY = Math.Sin(t * 1.8) * 0.65;
        state.L2 = (Math.Sin(t * 1.4) + 1.0) * 0.5;
        state.R2 = (Math.Cos(t * 1.4) + 1.0) * 0.5;
        state.GyroYaw = Math.Sin(t * 2.5) * 40.0;
        state.GyroPitch = Math.Cos(t * 2.0) * 25.0;

        int phase = (int)(t * 2.0) % 4;
        state.PressedButtons.Add(phase switch
        {
            0 => ControllerButton.South,
            1 => ControllerButton.East,
            2 => ControllerButton.West,
            _ => ControllerButton.North
        });
    }

    private static double NormalizeAxisSigned(int raw)
    {
        double v = ((raw - 32767.5) / 32767.5);
        return Math.Clamp(v, -1.0, 1.0);
    }

    private static double NormalizeTriggerUnsigned(int raw)
    {
        if (raw <= 0) return 0.0;
        return Math.Clamp(raw / 65535.0, 0.0, 1.0);
    }

    private static string SafeGetProductName(HidDevice hid)
    {
        try
        {
            string name = hid.GetProductName();
            if (!string.IsNullOrWhiteSpace(name)) return name.Trim();
        }
        catch { }
        return $"HID Controller ({hid.VendorID:X4}:{hid.ProductID:X4})";
    }

    private void ReleaseActiveDInputDevice()
    {
        if (_activeDInputDevice != null)
        {
            try { _activeDInputDevice.Unacquire(); } catch { }
            try { _activeDInputDevice.Dispose(); } catch { }
            _activeDInputDevice = null;
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            ReleaseActiveDInputDevice();
            _directInput?.Dispose();
            _directInput = null;
        }
    }
}
