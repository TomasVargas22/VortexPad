using VortexPad.Core.Models;
using VortexPad.Core.Profiles;
using VortexPad.Emulation;
using VortexPad.Input;
using Xunit;
using Injected = VortexPad.Emulation.NativeKeyboardMouseInjector.InjectedInput;

namespace VortexPad.WindowsTests;

public class NativeKeyboardMouseInjectorTests
{
    private const ushort VK_W = 0x57, VK_A = 0x41;

    private static (NativeKeyboardMouseInjector Injector, List<Injected[]> Batches) Create()
    {
        var batches = new List<Injected[]>();
        return (new NativeKeyboardMouseInjector(batches.Add), batches);
    }

    private static VirtualOutputState Keys(params ushort[] vks)
    {
        var state = new VirtualOutputState();
        foreach (var vk in vks) state.ActiveVirtualKeys.Add(vk);
        return state;
    }

    [Fact]
    public void Keys_SendOnlyChanges_DownAndUp()
    {
        var (injector, batches) = Create();

        injector.ApplyOutput(Keys(VK_W));
        injector.ApplyOutput(Keys(VK_W));        // no change → nothing sent
        injector.ApplyOutput(Keys(VK_W, VK_A));
        injector.ApplyOutput(Keys(VK_A));
        injector.ApplyOutput(Keys());

        Assert.Equal(4, batches.Count);
        Assert.Equal(new[] { new Injected(false, 0, 0, VK_W, false) }, batches[0]);
        Assert.Equal(new[] { new Injected(false, 0, 0, VK_A, false) }, batches[1]);
        Assert.Equal(new[] { new Injected(false, 0, 0, VK_W, true) }, batches[2]);
        Assert.Equal(new[] { new Injected(false, 0, 0, VK_A, true) }, batches[3]);
    }

    [Fact]
    public void Mouse_AccumulatesSubPixelMovement()
    {
        var (injector, batches) = Create();

        for (int i = 0; i < 3; i++)
        {
            injector.ApplyOutput(new VirtualOutputState { MouseDeltaX = 0.4, MouseDeltaY = -0.6 });
        }

        // 0.4 → 0.8 → 1.2: only the third frame moves 1 px in X; Y: -0.6 → -1.2 (frame 2) → -0.8
        Assert.Equal(2, batches.Count);
        Assert.Equal(new Injected(true, 0, -1, 0, false), batches[0].Single());
        Assert.Equal(new Injected(true, 1, 0, 0, false), batches[1].Single());
    }

    [Fact]
    public void ReleaseAllKeys_AndDispose_LiftEveryPressedKey()
    {
        var (injector, batches) = Create();
        injector.ApplyOutput(Keys(VK_W, VK_A));
        batches.Clear();

        injector.ReleaseAllKeys();
        Assert.Equal(2, batches.Single().Length);
        Assert.All(batches.Single(), i => Assert.True(i.KeyUp));

        batches.Clear();
        injector.Dispose(); // nothing pressed anymore
        Assert.Empty(batches);
    }

    [Fact]
    public void Disabled_SendsNothing()
    {
        var (injector, batches) = Create();
        injector.Enabled = false;
        injector.ApplyOutput(new VirtualOutputState { MouseDeltaX = 50 });
        Assert.Empty(batches);
    }
}

public class InputProviderTests
{
    [Fact]
    public void DiscoverDevices_AlwaysOffersTheSimulator_AndItProducesRestingNoise()
    {
        using var provider = new DirectInputAndHidProvider();
        var devices = provider.DiscoverDevices();

        var simulator = devices.Last();
        Assert.Equal(DirectInputAndHidProvider.VirtualTestPadId, simulator.UniqueId);
        Assert.Empty(provider.ResolveHidInstanceIds(simulator)); // nothing to hide with HidHide

        var state = provider.PollCurrentState(simulator);
        Assert.InRange(Math.Abs(state.LX), 0, 0.05);
        Assert.InRange(Math.Abs(state.RY), 0, 0.05);
    }

    [Fact]
    public void AsDisconnected_MarksOnceAndKeepsTheId()
    {
        var device = new DetectedPhysicalDevice("id-1", "Mando", 1, 2, "path", true, true, Guid.Empty);

        var missing = device.AsDisconnected().AsDisconnected();

        Assert.Equal("id-1", missing.UniqueId);
        Assert.Equal("Mando (desconectado)", missing.DisplayName);
        Assert.True(missing.IsDisconnectedPlaceholder);
    }

    [Fact]
    public void VirtualDeviceDetector_UnknownOrInvalidPaths_AreNotVirtual()
    {
        Assert.False(VirtualDeviceDetector.IsViGEmVirtual(null));
        Assert.False(VirtualDeviceDetector.IsViGEmVirtual("not a path"));
        Assert.False(VirtualDeviceDetector.IsViGEmVirtual(@"\\?\hid#vid_0000&pid_0000#0&0&0&0#{4d1e55b2-f16f-11cf-88cb-001111000030}"));
    }

    [Fact]
    public void SonyReader_WithMissingDevice_ReportsDisconnected_AndStopsCleanly()
    {
        using var reader = new DualSenseHidReader(@"\\?\hid#vid_054c&pid_0ce6#no-existe#{4d1e55b2-f16f-11cf-88cb-001111000030}");
        reader.Start();
        Thread.Sleep(150);

        Assert.False(reader.IsConnected);
        Assert.False(reader.CopyLatestInto(new PhysicalInputState()));
        Assert.Contains("desconectado", reader.StatusText);

        reader.SetOutput(new VortexPad.Core.Hid.SonyOutputState(255, 255, 1, 2, 3)); // no device: must not throw
        reader.Stop();
    }

    [Fact]
    public void ForegroundGameWatcher_MatchesLinkedProcessesIgnoringExeAndCase()
    {
        using var watcher = new ForegroundGameWatcher { AutoSwitchEnabled = false };
        var elden = new ControllerProfile { Name = "Elden", LinkedProcessNames = { "eldenring.exe" } };
        var forza = new ControllerProfile { Name = "Forza", LinkedProcessNames = { "ForzaHorizon5" } };
        watcher.MonitoredProfiles = new[] { elden, forza };

        Assert.Same(elden, watcher.FindMatchingProfile("EldenRing"));
        Assert.Same(forza, watcher.FindMatchingProfile("forzahorizon5.exe"));
        Assert.Null(watcher.FindMatchingProfile("notepad"));
        Assert.Null(watcher.FindMatchingProfile(""));
    }
}

public class ViGEmServiceWithoutDriverTests
{
    [Fact]
    public void KeyboardMouseMode_WorksWithoutTouchingViGEm()
    {
        using var service = new ViGEmVirtualControllerService();

        Assert.True(service.StartVirtualController(VirtualControllerType.KeyboardMouse));
        Assert.True(service.IsControllerConnected);
        service.SubmitState(new VirtualOutputState { LX = 1 }); // nothing to send to ViGEm: must not throw

        service.StopVirtualController();
        Assert.False(service.IsControllerConnected);
    }
}

public class MouseWheelAndButtonsInjectionTests
{
    private const uint LeftDown = 0x0002, LeftUp = 0x0004, RightDown = 0x0008, Wheel = 0x0800;

    [Fact]
    public void Wheel_AccumulatesFractions_AndButtonsSendOnlyChanges()
    {
        var batches = new List<Injected[]>();
        var injector = new NativeKeyboardMouseInjector(batches.Add);

        injector.ApplyOutput(new VirtualOutputState { MouseWheelDelta = 0.6 });  // < 1 unit: nothing
        injector.ApplyOutput(new VirtualOutputState { MouseWheelDelta = 0.6, MouseLeftButton = true });
        injector.ApplyOutput(new VirtualOutputState { MouseLeftButton = true }); // no change
        injector.ApplyOutput(new VirtualOutputState { MouseRightButton = true });

        Assert.Equal(2, batches.Count);
        Assert.Equal(new[]
        {
            new Injected(true, 0, 0, 0, false, Wheel, 1),
            new Injected(true, 0, 0, 0, false, LeftDown, 0)
        }, batches[0]);
        Assert.Equal(new[]
        {
            new Injected(true, 0, 0, 0, false, LeftUp, 0),
            new Injected(true, 0, 0, 0, false, RightDown, 0)
        }, batches[1]);
    }

    [Fact]
    public void ReleaseAllKeys_AlsoReleasesMouseButtons()
    {
        var batches = new List<Injected[]>();
        var injector = new NativeKeyboardMouseInjector(batches.Add);
        injector.ApplyOutput(new VirtualOutputState { MouseLeftButton = true, MouseRightButton = true });
        batches.Clear();

        injector.ReleaseAllKeys();

        Assert.Equal(new uint[] { LeftUp, 0x0010 }, batches.Single().Select(i => i.MouseFlags));
    }
}
