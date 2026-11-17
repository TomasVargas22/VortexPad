using VortexPad.AI;
using VortexPad.Core.Automation;
using VortexPad.Core.Models;
using VortexPad.Core.Profiles;
using Xunit;

namespace VortexPad.CoreTests;

public class MacroAndCalibrationTests
{
    private static MacroDefinition Combo() => new()
    {
        Id = "ui-combo",
        TriggerButton = ControllerButton.None,
        Steps =
        {
            new MacroStep { StepType = MacroStepType.TapButton, Button = ControllerButton.South, DurationMs = 90 },
            new MacroStep { StepType = MacroStepType.TapButton, Button = ControllerButton.East, DurationMs = 90 },
            new MacroStep { StepType = MacroStepType.TapButton, Button = ControllerButton.West, DurationMs = 90 },
            new MacroStep { StepType = MacroStepType.TapButton, Button = ControllerButton.North, DurationMs = 90 }
        }
    };

    /// <summary>Advances <paramref name="ms"/> frames of 1 ms and returns the output of the last one.</summary>
    private static VirtualOutputState Run(MacroAndTurboEngine engine, ControllerProfile profile, PhysicalInputState input, int ms)
    {
        var output = new VirtualOutputState();
        for (int i = 0; i < ms; i++)
        {
            engine.ProcessFrame(input, profile, 0.001, output);
        }
        return output;
    }

    [Fact]
    public void EnqueuedMacro_RunsThroughThePipeline_InOrder()
    {
        var engine = new MacroAndTurboEngine();
        var profile = BuiltInPresets.CreateXboxStandardPreset();
        var idle = new PhysicalInputState();

        engine.EnqueueMacro(Combo());

        var o = Run(engine, profile, idle, 1);
        Assert.True(o.IsButtonActive(ControllerButton.South));

        o = Run(engine, profile, idle, 95);   // ~96 ms
        Assert.False(o.IsButtonActive(ControllerButton.South));
        Assert.True(o.IsButtonActive(ControllerButton.East));

        o = Run(engine, profile, idle, 180);  // ~276 ms
        Assert.True(o.IsButtonActive(ControllerButton.North));

        o = Run(engine, profile, idle, 100);  // past 360 ms
        Assert.Empty(o.ActiveButtons);
        Assert.Equal(0, engine.ActiveMacroCount);
    }

    [Fact]
    public void EnqueuedMacro_CombinesWithPhysicalInput()
    {
        var engine = new MacroAndTurboEngine();
        var profile = BuiltInPresets.CreateXboxStandardPreset();
        var input = new PhysicalInputState { PressedButtons = { ControllerButton.LeftShoulder } };

        engine.EnqueueMacro(Combo());
        var o = Run(engine, profile, input, 10);

        Assert.True(o.IsButtonActive(ControllerButton.South));
        Assert.True(o.IsButtonActive(ControllerButton.LeftShoulder)); // the physical input is not overwritten
    }

    [Fact]
    public void EnqueueMacro_IsSafeFromAnotherThread()
    {
        var engine = new MacroAndTurboEngine();
        var profile = BuiltInPresets.CreateXboxStandardPreset();
        var idle = new PhysicalInputState();
        var output = new VirtualOutputState();

        var producer = Task.Run(() =>
        {
            for (int i = 0; i < 500; i++) engine.EnqueueMacro(Combo());
        });

        bool sawSouth = false;
        while (!producer.IsCompleted)
        {
            engine.ProcessFrame(idle, profile, 0.001, output);
            sawSouth |= output.IsButtonActive(ControllerButton.South);
        }
        engine.ProcessFrame(idle, profile, 0.001, output);
        sawSouth |= output.IsButtonActive(ControllerButton.South);

        Assert.True(sawSouth);
        Assert.Equal(1, engine.ActiveMacroCount); // same Id: restarts, does not pile up
    }

    private static ControllerProfile ProfileWithHoldMacro(string id)
    {
        var p = BuiltInPresets.CreateXboxStandardPreset();
        p.Id = id;
        p.Macros.Add(new MacroDefinition
        {
            Id = "hold-north",
            TriggerButton = ControllerButton.Paddle1,
            Steps = { new MacroStep { StepType = MacroStepType.PressButton, Button = ControllerButton.North, DurationMs = 1000 } }
        });
        return p;
    }

    [Fact]
    public void SwitchingToAnotherProfile_ReleasesRunningMacros_AndDoesNotRetriggerHeldButton()
    {
        var engine = new MacroAndTurboEngine();
        var a = ProfileWithHoldMacro("profile-a");
        var b = ProfileWithHoldMacro("profile-b");
        var holdingTrigger = new PhysicalInputState { PressedButtons = { ControllerButton.Paddle1 } };

        Assert.True(Run(engine, a, holdingTrigger, 10).IsButtonActive(ControllerButton.North));

        // Profile switch while still holding the trigger
        var afterSwitch = Run(engine, b, holdingTrigger, 10);
        Assert.False(afterSwitch.IsButtonActive(ControllerButton.North));
        Assert.Equal(0, engine.ActiveMacroCount);

        // Release and press again → B's macro does fire
        Run(engine, b, new PhysicalInputState(), 5);
        Assert.True(Run(engine, b, holdingTrigger, 5).IsButtonActive(ControllerButton.North));
    }

    [Fact]
    public void NewSnapshotOfTheSameProfile_KeepsRunningMacros()
    {
        var engine = new MacroAndTurboEngine();
        var original = ProfileWithHoldMacro("same-id");
        var holdingTrigger = new PhysicalInputState { PressedButtons = { ControllerButton.Paddle1 } };

        Run(engine, original, holdingTrigger, 10);
        var snapshot = ProfileRepository.DeepCopy(original); // e.g. the user moved a slider
        snapshot.RightStick.Sensitivity = 1.5;

        Assert.True(Run(engine, snapshot, holdingTrigger, 10).IsButtonActive(ControllerButton.North));
    }

    [Fact]
    public void RequestCancelAllMacros_ReleasesEverythingNextFrame()
    {
        var engine = new MacroAndTurboEngine();
        var profile = BuiltInPresets.CreateXboxStandardPreset();
        var idle = new PhysicalInputState();

        engine.EnqueueMacro(Combo());
        Assert.True(Run(engine, profile, idle, 5).IsButtonActive(ControllerButton.South));

        engine.RequestCancelAllMacros();
        Assert.Empty(Run(engine, profile, idle, 1).ActiveButtons);
    }

    [Fact]
    public void RepeatWhileHeld_StopsMidStepOnRelease_AndRepeatsWhileHeld()
    {
        var engine = new MacroAndTurboEngine();
        var profile = BuiltInPresets.CreateXboxStandardPreset();
        profile.Macros.Add(new MacroDefinition
        {
            Id = "rapid",
            TriggerButton = ControllerButton.Paddle2,
            RepeatWhileHeld = true,
            Steps =
            {
                new MacroStep { StepType = MacroStepType.TapButton, Button = ControllerButton.West, DurationMs = 10 },
                new MacroStep { StepType = MacroStepType.WaitMilliseconds, DurationMs = 10 }
            }
        });
        var held = new PhysicalInputState { PressedButtons = { ControllerButton.Paddle2 } };

        // Cycles of 20 ms: West active in the first half of each one
        Assert.True(Run(engine, profile, held, 5).IsButtonActive(ControllerButton.West));
        Assert.False(Run(engine, profile, held, 10).IsButtonActive(ControllerButton.West));  // ~15 ms
        Assert.True(Run(engine, profile, held, 10).IsButtonActive(ControllerButton.West));   // ~25 ms: repeated

        // Release mid-tap → released right away
        Assert.False(Run(engine, profile, new PhysicalInputState(), 1).IsButtonActive(ControllerButton.West));
        Assert.Equal(0, engine.ActiveMacroCount);
    }

    private static PhysicalInputState RestingNoise(int i) => new()
    {
        LX = Math.Sin(i * 0.37) * 0.02,
        LY = Math.Cos(i * 0.21) * 0.015,
        RX = Math.Sin(i * 0.11) * 0.03,
        RY = 0.01
    };

    [Fact]
    public void Recorder_SamplesForTheWholeDuration_AndComputesDeadzone()
    {
        var recorder = new DriftCalibrationRecorder(durationSeconds: 2.0, maxRateHz: 1000);
        Assert.True(recorder.Start());
        Assert.False(recorder.Start()); // already running

        int i = 0;
        while (recorder.IsRecording && i < 5000)
        {
            recorder.AddSample(RestingNoise(i++), 0.001);
        }

        Assert.Equal(DriftCalibrationStatus.Completed, recorder.Status);
        Assert.InRange(recorder.SampleCount, 1999, 2001);
        Assert.Equal(1.0, recorder.Progress, 3);

        var result = recorder.BuildResult(new SmartMapperAndCalibrator());
        Assert.NotNull(result);
        Assert.True(result!.RecommendedLeftDeadzone > result.MeasuredLeftMaxDrift);
        Assert.True(result.RecommendedRightDeadzone > result.MeasuredRightMaxDrift);
        Assert.Contains($"{recorder.SampleCount} muestras", result.DiagnosticSummary);
    }

    [Fact]
    public void Recorder_DetectsMovement_AndSupportsCancel()
    {
        var calibrator = new SmartMapperAndCalibrator();

        var moved = new DriftCalibrationRecorder(1.0);
        moved.Start();
        moved.AddSample(RestingNoise(0), 0.001);
        moved.AddSample(new PhysicalInputState { LX = 0.8 }, 0.001);
        Assert.Equal(DriftCalibrationStatus.MovementDetected, moved.Status);
        Assert.Null(moved.BuildResult(calibrator));

        var cancelled = new DriftCalibrationRecorder(1.0);
        cancelled.Start();
        cancelled.AddSample(RestingNoise(0), 0.001);
        cancelled.Cancel();
        cancelled.AddSample(RestingNoise(1), 0.001); // ignored
        Assert.Equal(DriftCalibrationStatus.Cancelled, cancelled.Status);
        Assert.Equal(1, cancelled.SampleCount);
        Assert.Null(cancelled.BuildResult(calibrator));

        // Can be restarted after finishing
        Assert.True(cancelled.Start());
        Assert.Equal(0, cancelled.SampleCount);
    }

    [Fact]
    public void Recorder_AddSample_DoesNotAllocate()
    {
        var recorder = new DriftCalibrationRecorder(10.0, 1000);
        var sample = RestingNoise(3);
        recorder.AddSample(sample, 0.001); // idle: warm-up
        recorder.Start();
        recorder.AddSample(sample, 0.001);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 2000; i++) recorder.AddSample(sample, 0.001);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void Calibrator_SpanOverload_MatchesListOverload()
    {
        var calibrator = new SmartMapperAndCalibrator();
        var samples = Enumerable.Range(0, 300).Select(RestingNoise).ToList();

        var fromList = calibrator.CalibrateStickDrift(samples);
        var fromSpans = calibrator.CalibrateStickDrift(
            samples.Select(s => s.LX).ToArray(), samples.Select(s => s.LY).ToArray(),
            samples.Select(s => s.RX).ToArray(), samples.Select(s => s.RY).ToArray());

        Assert.Equal(fromList, fromSpans);
    }
}
