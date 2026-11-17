using VortexPad.AI;
using VortexPad.Core.Automation;
using VortexPad.Core.Models;
using VortexPad.Core.Processing;
using VortexPad.Core.Profiles;
using Xunit;

namespace VortexPad.CoreTests;

public class VortexPadCoreTests
{
    [Fact]
    public void RadialDeadzone_ZeroesDeflectionInsideInnerThreshold_AndScalesOutside()
    {
        var config = new StickConfiguration
        {
            DeadzoneMode = DeadzoneMode.Radial,
            InnerDeadzone = 0.10,
            OuterDeadzone = 1.00,
            AntiDeadzone = 0.0,
            CurveType = ResponseCurveType.Linear,
            Sensitivity = 1.0
        };

        // Inside radial deadzone: sqrt(0.06^2 + 0.06^2) = 0.0848 < 0.10
        var inside = SignalProcessor.ProcessStick2D(0.06, 0.06, config);
        Assert.Equal(0.0, inside.X, 5);
        Assert.Equal(0.0, inside.Y, 5);

        // Midpoint between 0.10 and 1.00 along X axis (0.55) -> 0.50 output
        var mid = SignalProcessor.ProcessStick2D(0.55, 0.0, config);
        Assert.Equal(0.50, mid.X, 4);
        Assert.Equal(0.0, mid.Y, 4);

        // Full deflection -> 1.0
        var full = SignalProcessor.ProcessStick2D(1.0, 0.0, config);
        Assert.Equal(1.0, full.X, 4);
    }

    [Fact]
    public void AxialDeadzone_EliminatesCrossAxisDrift_WhileKeepingPrimaryAxis()
    {
        var config = new StickConfiguration
        {
            DeadzoneMode = DeadzoneMode.Axial,
            InnerDeadzone = 0.10,
            OuterDeadzone = 1.0,
            CurveType = ResponseCurveType.Linear
        };

        // X is at 0.55, Y has minor drift of 0.05 (< 0.10 axial deadzone)
        var result = SignalProcessor.ProcessStick2D(0.55, 0.05, config);
        Assert.True(result.X > 0.45);
        Assert.Equal(0.0, result.Y, 5);
    }

    [Fact]
    public void AntiDeadzone_BoostsMinimalDeflectionAboveTargetFloor()
    {
        var config = new StickConfiguration
        {
            DeadzoneMode = DeadzoneMode.Radial,
            InnerDeadzone = 0.05,
            OuterDeadzone = 1.0,
            AntiDeadzone = 0.15,
            CurveType = ResponseCurveType.Linear
        };

        // Just above inner deadzone (0.06) should start above the 0.15 Anti-Deadzone floor
        var result = SignalProcessor.ProcessStick2D(0.06, 0.0, config);
        Assert.True(result.X >= 0.15, $"Expected X >= 0.15, got {result.X}");
    }

    [Fact]
    public void ResponseCurves_SatisfyEndpointsAndCharacteristicShapes()
    {
        // Endpoints
        foreach (var curve in Enum.GetValues<ResponseCurveType>())
        {
            Assert.Equal(0.0, SignalProcessor.EvaluateResponseCurve(0.0, curve, 2.0), 6);
            Assert.Equal(1.0, SignalProcessor.EvaluateResponseCurve(1.0, curve, 2.0), 6);
        }

        // Exponential with gamma = 2.0 at t = 0.5 -> 0.25 (finer center control)
        double expMid = SignalProcessor.EvaluateResponseCurve(0.5, ResponseCurveType.Exponential, 2.0);
        Assert.Equal(0.25, expMid, 5);

        // S-Curve is symmetric at t = 0.5 -> 0.5, and slower than linear at t = 0.25
        double sMid = SignalProcessor.EvaluateResponseCurve(0.5, ResponseCurveType.SCurve, 2.0);
        double sQuarter = SignalProcessor.EvaluateResponseCurve(0.25, ResponseCurveType.SCurve, 2.0);
        Assert.Equal(0.5, sMid, 5);
        Assert.True(sQuarter < 0.25);

        // Custom Bezier monotonic check
        double bez1 = SignalProcessor.EvaluateResponseCurve(0.3, ResponseCurveType.CustomBezier, 1.0, 0.15, 0.85);
        double bez2 = SignalProcessor.EvaluateResponseCurve(0.7, ResponseCurveType.CustomBezier, 1.0, 0.15, 0.85);
        Assert.True(bez2 > bez1);
    }

    [Fact]
    public void BuiltInPresets_XboxPs5AndAi_AreValidAndRoundTripJsonCleanly()
    {
        var presets = BuiltInPresets.CreateAll();
        Assert.Equal(3, presets.Count);

        var xbox = presets.Single(p => p.Id == BuiltInPresets.XboxStandardId);
        var ps5 = presets.Single(p => p.Id == BuiltInPresets.Ps5DualSenseId);
        var ai = presets.Single(p => p.Id == BuiltInPresets.AiSmartProfileId);

        Assert.Equal(VirtualControllerType.Xbox360, xbox.TargetControllerType);
        Assert.Equal(0.08, xbox.LeftStick.InnerDeadzone, 4);

        Assert.Equal(VirtualControllerType.DualShock4, ps5.TargetControllerType);
        Assert.Equal(GyroOutputMode.RightStick, ps5.Gyro.OutputMode);

        Assert.Equal(ResponseCurveType.SCurve, ai.RightStick.CurveType);
        Assert.NotEmpty(ai.ShiftLayers);

        // Round-trip JSON serialization
        string json = ProfileRepository.SerializeProfile(ps5);
        var restored = ProfileRepository.DeserializeProfile(json);
        Assert.NotNull(restored);
        Assert.Equal(ps5.Name, restored!.Name);
        Assert.Equal(ps5.TargetControllerType, restored.TargetControllerType);
        Assert.Equal(ps5.Gyro.SensitivityX, restored.Gyro.SensitivityX, 4);
    }

    [Fact]
    public void TurboOscillator_PulsesButtonAtConfiguredFrequency()
    {
        var engine = new MacroAndTurboEngine();
        var turboCfg = new TurboButtonConfig
        {
            Button = ControllerButton.South,
            Enabled = true,
            FrequencyHz = 10.0, // Period = 0.100s (ON for 0.050s, OFF for 0.050s)
            DutyCycle = 0.5
        };

        // Initial edge -> ON
        Assert.True(engine.EvaluateTurbo(ControllerButton.South, physicallyHeld: true, turboCfg, deltaSeconds: 0.0));

        // +30ms (still in first half of 100ms period) -> ON
        Assert.True(engine.EvaluateTurbo(ControllerButton.South, physicallyHeld: true, turboCfg, deltaSeconds: 0.030));

        // +30ms (total 60ms, now in second half of 100ms period) -> OFF
        Assert.False(engine.EvaluateTurbo(ControllerButton.South, physicallyHeld: true, turboCfg, deltaSeconds: 0.030));

        // +50ms (total 110ms -> wraps to 10ms of next cycle) -> ON
        Assert.True(engine.EvaluateTurbo(ControllerButton.South, physicallyHeld: true, turboCfg, deltaSeconds: 0.050));

        // Releasing button resets turbo
        Assert.False(engine.EvaluateTurbo(ControllerButton.South, physicallyHeld: false, turboCfg, deltaSeconds: 0.010));
    }

    [Fact]
    public void ShiftLayer_AndMacros_TransformOutputCorrectly()
    {
        var engine = new MacroAndTurboEngine();
        var profile = BuiltInPresets.CreateAiSmartPreset();

        // Add a test macro triggered by Paddle1 that taps North (Y) for 40ms
        profile.Macros.Add(new MacroDefinition
        {
            Id = "test-macro",
            TriggerButton = ControllerButton.Paddle1,
            Steps =
            {
                new MacroStep { StepType = MacroStepType.TapButton, Button = ControllerButton.North, DurationMs = 40 }
            }
        });

        // 1. Hold Back (Shift Modifier) + deflect Right Stick -> converts Right Stick to MouseDelta
        var inputShift = new PhysicalInputState
        {
            RX = 0.8,
            RY = 0.0,
            PressedButtons = { ControllerButton.Back, ControllerButton.Paddle1 }
        };

        var outFrame1 = engine.ProcessFrame(inputShift, profile, deltaSeconds: 0.010);
        Assert.True(outFrame1.MouseDeltaX > 0.5);
        Assert.True(outFrame1.IsButtonActive(ControllerButton.North)); // Macro active during 40ms window

        // Advance past 50ms -> Macro completes and releases North
        var outFrame2 = engine.ProcessFrame(inputShift, profile, deltaSeconds: 0.050);
        Assert.False(outFrame2.IsButtonActive(ControllerButton.North));
    }

    [Fact]
    public void AiCalibrator_IdentifiesControllers_AndComputesDriftDeadzone()
    {
        var ai = new SmartMapperAndCalibrator();

        var dualSense = ai.IdentifyController(0x054C, 0x0CE6, "Wireless Controller");
        Assert.Equal(BuiltInPresets.Ps5DualSenseId, dualSense.RecommendedPresetId);
        Assert.True(dualSense.SupportsGyroImu);

        var samples = new List<PhysicalInputState>
        {
            new() { LX = 0.03, LY = -0.04, RX = 0.01, RY = 0.01 }, // Left radius = 0.05
            new() { LX = 0.032, LY = -0.041, RX = 0.012, RY = 0.01 }
        };

        var cal = ai.CalibrateStickDrift(samples);
        Assert.True(cal.RecommendedLeftDeadzone > 0.05);
        Assert.True(cal.RecommendedRightDeadzone < cal.RecommendedLeftDeadzone);
    }
}
