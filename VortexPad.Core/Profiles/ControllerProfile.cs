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

    public MappingLayer BaseLayer { get; set; } = new();
    public List<MappingLayer> ShiftLayers { get; set; } = new();
    public List<TurboButtonConfig> TurboConfigs { get; set; } = new();
    public List<MacroDefinition> Macros { get; set; } = new();
}
