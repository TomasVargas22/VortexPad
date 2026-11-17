using VortexPad.Core.Automation;
using VortexPad.Core.Engine;
using VortexPad.Core.Models;
using VortexPad.Core.Profiles;
using Xunit;

namespace VortexPad.CoreTests;

public class PipelinePerformanceTests
{
    /// <summary>Profile that goes through every branch of the pipeline: shift layer, turbo, keys, WASD, gyro and macro.</summary>
    private static ControllerProfile CreateFullFeatureProfile()
    {
        var profile = BuiltInPresets.CreateAiSmartPreset();
        profile.TurboConfigs.Add(new TurboButtonConfig { Button = ControllerButton.South, Enabled = true, FrequencyHz = 20 });
        profile.BaseLayer.LeftStickToWasd = true;
        profile.BaseLayer.ButtonToKeyboardKey[ControllerButton.North] = 0x20; // space
        profile.BaseLayer.ButtonRemaps[ControllerButton.East] = ControllerButton.West;
        profile.Macros.Add(new MacroDefinition
        {
            Id = "perf-macro",
            TriggerButton = ControllerButton.Paddle1,
            RepeatWhileHeld = true,
            Steps =
            {
                new MacroStep { StepType = MacroStepType.TapButton, Button = ControllerButton.DPadUp, DurationMs = 3 },
                new MacroStep { StepType = MacroStepType.PressVirtualKey, VirtualKeyCode = 0x45, DurationMs = 2 },
                new MacroStep { StepType = MacroStepType.ReleaseVirtualKey, VirtualKeyCode = 0x45, DurationMs = 2 },
                new MacroStep { StepType = MacroStepType.WaitMilliseconds, DurationMs = 4 }
            }
        });
        return profile;
    }

    private static void FillInput(PhysicalInputState input, int frame)
    {
        double t = frame * 0.001;
        input.LX = Math.Sin(t * 3);
        input.LY = Math.Cos(t * 3);
        input.RX = Math.Sin(t * 5) * 0.7;
        input.RY = Math.Cos(t * 2) * 0.7;
        input.L2 = 0.9; // activates the gyro (WhileL2Held)
        input.R2 = (frame % 50) / 50.0;
        input.GyroYaw = Math.Sin(t * 7) * 60;
        input.GyroPitch = Math.Cos(t * 7) * 30;

        input.PressedButtons.Clear();
        input.PressedButtons.Add(ControllerButton.Paddle1); // macro held (repeats)
        input.PressedButtons.Add(ControllerButton.South);   // turbo
        if (frame % 3 == 0) input.PressedButtons.Add(ControllerButton.North);
        if (frame % 4 == 0) input.PressedButtons.Add(ControllerButton.East);
        if (frame % 200 < 100) input.PressedButtons.Add(ControllerButton.Back); // shift layer on/off
    }

    [Fact]
    public void ProcessFrame_SteadyState_AllocatesZeroBytes()
    {
        var engine = new MacroAndTurboEngine();
        var profile = CreateFullFeatureProfile();
        var input = new PhysicalInputState();
        var output = new VirtualOutputState();

        // Warm-up: JIT, HashSet/Dictionary capacity, macro creation
        for (int i = 0; i < 5000; i++)
        {
            FillInput(input, i);
            engine.ProcessFrame(input, profile, 0.001, output);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 5000; i < 15000; i++)
        {
            FillInput(input, i);
            engine.ProcessFrame(input, profile, 0.001, output);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
    }

    [Fact]
    public void ProcessFrame_ReusedOutput_MatchesAllocatingOverload()
    {
        var engineA = new MacroAndTurboEngine();
        var engineB = new MacroAndTurboEngine();
        var profile = CreateFullFeatureProfile();
        var input = new PhysicalInputState();
        var reused = new VirtualOutputState();

        for (int i = 0; i < 2000; i++)
        {
            FillInput(input, i);
            var fresh = engineA.ProcessFrame(input, profile, 0.001);
            engineB.ProcessFrame(input, profile, 0.001, reused);

            Assert.Equal(fresh.LX, reused.LX);
            Assert.Equal(fresh.RY, reused.RY);
            Assert.Equal(fresh.R2, reused.R2);
            Assert.Equal(fresh.MouseDeltaX, reused.MouseDeltaX);
            Assert.Equal(fresh.MouseDeltaY, reused.MouseDeltaY);
            Assert.True(fresh.ActiveButtons.SetEquals(reused.ActiveButtons), $"Botones distintos en frame {i}");
            Assert.True(fresh.ActiveVirtualKeys.SetEquals(reused.ActiveVirtualKeys), $"Teclas distintas en frame {i}");
            Assert.Equal(fresh.LightbarR, reused.LightbarR);
        }
    }

    [Fact]
    public void ProfileCache_RebuildsWhenANewSnapshotIsPublished()
    {
        var engine = new MacroAndTurboEngine();
        var snapshotA = BuiltInPresets.CreateXboxStandardPreset();
        var input = new PhysicalInputState { PressedButtons = { ControllerButton.South } };
        var output = new VirtualOutputState();

        // No turbo: South held during 30 ms always active
        for (int i = 0; i < 30; i++)
        {
            engine.ProcessFrame(input, snapshotA, 0.001, output);
            Assert.True(output.IsButtonActive(ControllerButton.South));
        }

        // New snapshot with 10 Hz turbo → at some point within 100 ms it must release
        var snapshotB = ProfileRepository.DeepCopy(snapshotA);
        snapshotB.TurboConfigs.Add(new TurboButtonConfig { Button = ControllerButton.South, Enabled = true, FrequencyHz = 10 });

        bool sawRelease = false;
        for (int i = 0; i < 100; i++)
        {
            engine.ProcessFrame(input, snapshotB, 0.001, output);
            sawRelease |= !output.IsButtonActive(ControllerButton.South);
        }
        Assert.True(sawRelease);
    }

    [Fact]
    public void ProfileCache_InvalidateAppliesInPlaceMutation()
    {
        var engine = new MacroAndTurboEngine();
        var profile = BuiltInPresets.CreateXboxStandardPreset();
        var output = new VirtualOutputState();

        engine.ProcessFrame(new PhysicalInputState(), profile, 0.001, output);
        Assert.Equal(0x10, output.LightbarR); // #107C10

        profile.LightbarHex = "#FF0000";
        engine.InvalidateProfileCache();
        engine.ProcessFrame(new PhysicalInputState(), profile, 0.001, output);
        Assert.Equal(0xFF, output.LightbarR);
        Assert.Equal(0x00, output.LightbarG);
    }

    [Fact]
    public void DeepCopy_IsIndependentFromTheOriginal()
    {
        var original = BuiltInPresets.CreateAiSmartPreset();
        var copy = ProfileRepository.DeepCopy(original);

        Assert.Equal(original.Id, copy.Id);
        Assert.Equal(original.Name, copy.Name);
        Assert.NotSame(original.RightStick, copy.RightStick);
        Assert.Equal(original.ShiftLayers.Count, copy.ShiftLayers.Count);

        original.RightStick.InnerDeadzone = 0.5;
        original.ShiftLayers.Clear();
        Assert.NotEqual(0.5, copy.RightStick.InnerDeadzone);
        Assert.Single(copy.ShiftLayers);
    }

    private sealed class Frame
    {
        public long A;
        public long B;
    }

    [Fact]
    public void TripleBuffer_SingleThread_ReturnsLatestPublished()
    {
        var buffer = new TripleBuffer<Frame>(() => new Frame());

        buffer.WriteBuffer.A = 1;
        buffer.Publish();
        buffer.WriteBuffer.A = 2;
        buffer.Publish();

        Assert.Equal(2, buffer.ReadLatest().A);
        // Nothing new: returns the same frame again
        Assert.Equal(2, buffer.ReadLatest().A);

        buffer.WriteBuffer.A = 3;
        buffer.Publish();
        Assert.Equal(3, buffer.ReadLatest().A);
    }

    [Fact]
    public async Task TripleBuffer_Concurrent_NeverReturnsTornOrOlderFrames()
    {
        var buffer = new TripleBuffer<Frame>(() => new Frame());
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        var producer = Task.Run(() =>
        {
            long seq = 0;
            while (!cts.IsCancellationRequested)
            {
                seq++;
                var f = buffer.WriteBuffer;
                f.A = seq;
                f.B = seq; // the consumer must see A == B (frame not half-written)
                buffer.Publish();
            }
        });

        long last = 0;
        long reads = 0;
        while (!cts.IsCancellationRequested)
        {
            var f = buffer.ReadLatest();
            long a = f.A, b = f.B;
            Assert.Equal(a, b);
            Assert.True(a >= last, $"Frame más viejo: {a} < {last}");
            last = a;
            reads++;
        }

        await producer;
        Assert.True(reads > 0);
        Assert.True(last > 0);
    }
}
