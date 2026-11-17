namespace VortexPad.Core.Models;

public enum VirtualControllerType
{
    Xbox360,
    DualShock4,
    KeyboardMouse,
    HybridXboxKeyboard
}

public enum ResponseCurveType
{
    Linear,
    Exponential,
    SCurve,
    CustomBezier
}

public enum DeadzoneMode
{
    Radial,
    Axial,
    Hybrid
}

public enum GyroOutputMode
{
    Disabled,
    RightStick,
    MouseDelta
}

public enum GyroActivationMode
{
    AlwaysOn,
    WhileL2Held,
    WhileButtonHeld,
    ToggleButton
}

public enum ControllerButton
{
    None = 0,
    South = 1,       // Xbox A / PS Cross
    East = 2,        // Xbox B / PS Circle
    West = 3,        // Xbox X / PS Square
    North = 4,       // Xbox Y / PS Triangle
    LeftShoulder = 5, // LB / L1
    RightShoulder = 6,// RB / R1
    Back = 7,        // View / Share
    Start = 8,       // Menu / Options
    Guide = 9,       // Xbox / PS Button
    LeftThumb = 10,  // L3
    RightThumb = 11, // R3
    TouchpadClick = 12,
    DPadUp = 13,
    DPadDown = 14,
    DPadLeft = 15,
    DPadRight = 16,
    Paddle1 = 17,
    Paddle2 = 18
}

public sealed class PhysicalInputState
{
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = "Generic DirectInput / HID Pad";
    public int VendorId { get; set; }
    public int ProductId { get; set; }

    // Normalized stick axes in [-1.0, 1.0]
    public double LX { get; set; }
    public double LY { get; set; }
    public double RX { get; set; }
    public double RY { get; set; }

    // Normalized triggers in [0.0, 1.0]
    public double L2 { get; set; }
    public double R2 { get; set; }

    // Gyroscope angular velocity in deg/s
    public double GyroYaw { get; set; }
    public double GyroPitch { get; set; }
    public double GyroRoll { get; set; }

    // Accelerometer in g
    public double AccelX { get; set; }
    public double AccelY { get; set; }
    public double AccelZ { get; set; }

    // Buttons pressed
    public HashSet<ControllerButton> PressedButtons { get; set; } = new();

    public long TimestampTicks { get; set; }

    public bool IsPressed(ControllerButton button) => PressedButtons.Contains(button);

    public PhysicalInputState Clone()
    {
        return new PhysicalInputState
        {
            DeviceId = DeviceId,
            DeviceName = DeviceName,
            VendorId = VendorId,
            ProductId = ProductId,
            LX = LX,
            LY = LY,
            RX = RX,
            RY = RY,
            L2 = L2,
            R2 = R2,
            GyroYaw = GyroYaw,
            GyroPitch = GyroPitch,
            GyroRoll = GyroRoll,
            AccelX = AccelX,
            AccelY = AccelY,
            AccelZ = AccelZ,
            PressedButtons = new HashSet<ControllerButton>(PressedButtons),
            TimestampTicks = TimestampTicks
        };
    }
}

public sealed class VirtualOutputState
{
    public VirtualControllerType TargetType { get; set; } = VirtualControllerType.Xbox360;

    // Processed stick axes in [-1.0, 1.0]
    public double LX { get; set; }
    public double LY { get; set; }
    public double RX { get; set; }
    public double RY { get; set; }

    // Processed triggers in [0.0, 1.0]
    public double L2 { get; set; }
    public double R2 { get; set; }

    // Active virtual buttons
    public HashSet<ControllerButton> ActiveButtons { get; set; } = new();

    // Mouse output deltas (pixels)
    public double MouseDeltaX { get; set; }
    public double MouseDeltaY { get; set; }

    // Active Windows Virtual Key codes (VK_*)
    public HashSet<ushort> ActiveVirtualKeys { get; set; } = new();

    // DS4 Lightbar RGB
    public byte LightbarR { get; set; } = 0;
    public byte LightbarG { get; set; } = 120;
    public byte LightbarB { get; set; } = 255;

    public bool IsButtonActive(ControllerButton button) => ActiveButtons.Contains(button);
}

public sealed class ForceFeedbackState
{
    public double LargeMotor { get; set; }
    public double SmallMotor { get; set; }
    public byte LedR { get; set; }
    public byte LedG { get; set; }
    public byte LedB { get; set; }

    /// <summary>true if the game set the lightbar color (only the virtual DS4 sends it; Xbox 360 does not).</summary>
    public bool HasLightbar { get; set; }
}
