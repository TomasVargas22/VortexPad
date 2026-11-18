using VortexPad.Core.Models;

namespace VortexPad.Core.Profiles;

public sealed class StickConfiguration
{
    public DeadzoneMode DeadzoneMode { get; set; } = DeadzoneMode.Radial;
    public double InnerDeadzone { get; set; } = 0.08;
    public double OuterDeadzone { get; set; } = 0.98;
    public double AntiDeadzone { get; set; } = 0.0;
    public double AxialDeadzone { get; set; } = 0.02;
    public double Sensitivity { get; set; } = 1.0;
    public bool InvertX { get; set; }
    public bool InvertY { get; set; }
    public ResponseCurveType CurveType { get; set; } = ResponseCurveType.Linear;
    public double CurveExponent { get; set; } = 1.8;

    // Cubic Bezier control points Y-coordinates for X in {0.25, 0.50, 0.75} (with (0,0) and (1,1) fixed)
    public double BezierP1 { get; set; } = 0.25;
    public double BezierP2 { get; set; } = 0.50;
}

public sealed class TriggerConfiguration
{
    public double InnerDeadzone { get; set; } = 0.03;
    public double OuterDeadzone { get; set; } = 0.98;
    public double Sensitivity { get; set; } = 1.0;
    public ResponseCurveType CurveType { get; set; } = ResponseCurveType.Linear;
    public double CurveExponent { get; set; } = 1.4;
    public bool HairTriggerMode { get; set; }
    public double HairTriggerThreshold { get; set; } = 0.15;
}

public sealed class GyroConfiguration
{
    public GyroOutputMode OutputMode { get; set; } = GyroOutputMode.Disabled;
    public GyroActivationMode ActivationMode { get; set; } = GyroActivationMode.WhileL2Held;
    public ControllerButton ActivationButton { get; set; } = ControllerButton.LeftShoulder;
    public double SensitivityX { get; set; } = 2.5;
    public double SensitivityY { get; set; } = 2.0;
    public double NoiseThresholdDegPerSec { get; set; } = 0.6;
    public double SmoothingFactor { get; set; } = 0.25;
    public bool InvertYaw { get; set; }
    public bool InvertPitch { get; set; }
}

public sealed class ForceFeedbackConfiguration
{
    public bool Enabled { get; set; } = true;
    public double OverallStrength { get; set; } = 1.0;
    public double LargeMotorGain { get; set; } = 1.0;
    public double SmallMotorGain { get; set; } = 1.0;
}

public enum TouchpadMode
{
    /// <summary>Touch is ignored; the touchpad click goes out as the virtual controller's touchpad button.</summary>
    Disabled,
    /// <summary>One finger moves the mouse, two fingers scroll.</summary>
    Mouse
}

/// <summary>DualSense touchpad: mouse, two-finger scroll and click zones.</summary>
public sealed class TouchpadConfiguration
{
    public TouchpadMode Mode { get; set; } = TouchpadMode.Disabled;

    /// <summary>Pixels per full touchpad width × this factor (1.0 ≈ 1000 px).</summary>
    public double MouseSensitivity { get; set; } = 1.0;

    public bool TwoFingerScroll { get; set; } = true;

    /// <summary>1.0 ≈ 10 wheel notches for a full touchpad height.</summary>
    public double ScrollSensitivity { get; set; } = 1.0;

    public bool InvertScroll { get; set; }

    /// <summary>
    /// Splits the click into two halves: the left half presses <see cref="LeftZoneButton"/> and the right half
    /// <see cref="RightZoneButton"/>, instead of the normal touchpad button.
    /// </summary>
    public bool SplitClickZones { get; set; }

    public ControllerButton LeftZoneButton { get; set; } = ControllerButton.Back;
    public ControllerButton RightZoneButton { get; set; } = ControllerButton.Start;

    /// <summary>In mouse mode: left half = left click, right half = right click (takes priority over the zones).</summary>
    public bool ClickAsMouseButtons { get; set; } = true;
}

/// <summary>
/// DualSense adaptive trigger effect (positions 0–9 along the travel, strength 1–8).
/// - Resistance: constant resistance from <see cref="StartPosition"/> to the end.
/// - Weapon: "trigger" type click between <see cref="StartPosition"/> and <see cref="EndPosition"/>.
/// - Vibration: vibrates from <see cref="StartPosition"/> at <see cref="FrequencyHz"/>.
/// </summary>
public sealed class AdaptiveTriggerConfiguration
{
    public AdaptiveTriggerMode Mode { get; set; } = AdaptiveTriggerMode.Off;
    public int StartPosition { get; set; } = 2;
    public int EndPosition { get; set; } = 6;
    public int Strength { get; set; } = 5;
    public int FrequencyHz { get; set; } = 30;
}

public sealed class TurboButtonConfig
{
    public ControllerButton Button { get; set; }
    public bool Enabled { get; set; }
    public double FrequencyHz { get; set; } = 15.0;
    public double DutyCycle { get; set; } = 0.5;
}

public enum MacroStepType
{
    PressButton,
    ReleaseButton,
    TapButton,
    PressVirtualKey,
    ReleaseVirtualKey,
    WaitMilliseconds
}

public sealed class MacroStep
{
    public MacroStepType StepType { get; set; }
    public ControllerButton Button { get; set; }
    public ushort VirtualKeyCode { get; set; }
    public int DurationMs { get; set; } = 50;
}

public sealed class MacroDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Combo Macro";
    public ControllerButton TriggerButton { get; set; } = ControllerButton.Paddle1;
    public bool RepeatWhileHeld { get; set; }
    public List<MacroStep> Steps { get; set; } = new();
}

public sealed class MappingLayer
{
    public string Name { get; set; } = "Base Layer";
    public ControllerButton? ShiftModifierButton { get; set; }
    public Dictionary<ControllerButton, ControllerButton> ButtonRemaps { get; set; } = new();
    public Dictionary<ControllerButton, ushort> ButtonToKeyboardKey { get; set; } = new();
    public bool LeftStickToWasd { get; set; }
    public bool RightStickToMouse { get; set; }
    public double StickMouseSensitivity { get; set; } = 14.0;
}

public sealed class ControllerProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New Profile";
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = "Custom";
    public bool IsBuiltInPreset { get; set; }
    public VirtualControllerType TargetControllerType { get; set; } = VirtualControllerType.Xbox360;
    public string LightbarHex { get; set; } = "#00E5FF";
    public bool HidePhysicalControllerWithHidHide { get; set; } = true;
    public int PollingRateHz { get; set; } = 1000;

    public List<string> LinkedProcessNames { get; set; } = new();

    public StickConfiguration LeftStick { get; set; } = new();
    public StickConfiguration RightStick { get; set; } = new();
    public TriggerConfiguration LeftTrigger { get; set; } = new();
    public TriggerConfiguration RightTrigger { get; set; } = new();
    public GyroConfiguration Gyro { get; set; } = new();
    public ForceFeedbackConfiguration ForceFeedback { get; set; } = new();

    // DualSense extras (Phase 8). Missing in old JSON → default values (off).
    public TouchpadConfiguration Touchpad { get; set; } = new();
    public AdaptiveTriggerConfiguration LeftAdaptiveTrigger { get; set; } = new();
    public AdaptiveTriggerConfiguration RightAdaptiveTrigger { get; set; } = new();

    public MappingLayer BaseLayer { get; set; } = new();
    public List<MappingLayer> ShiftLayers { get; set; } = new();
    public List<TurboButtonConfig> TurboConfigs { get; set; } = new();
    public List<MacroDefinition> Macros { get; set; } = new();
}
