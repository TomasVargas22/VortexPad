using VortexPad.Emulation;
using Xunit;

namespace VortexPad.WindowsTests;

public sealed class HidHideIntegrationServiceTests : IDisposable
{
    private const string PadUsb = @"HID\VID_054C&PID_0CE6&MI_03\7&2A5E1B6&0&0000";
    private const string PadSecondInterface = @"HID\VID_054C&PID_0CE6&MI_03\7&2A5E1B6&0&0001";
    private const string UserHiddenDevice = @"HID\VID_046D&PID_C216\6&111&0&0000";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vortexpad-hidhide-" + Guid.NewGuid().ToString("N"));
    private readonly FakeHidHideCli _cli = new();
    private readonly string _sessionPath;
    private readonly string _exePath;

    public HidHideIntegrationServiceTests()
    {
        Directory.CreateDirectory(_dir);
        _sessionPath = Path.Combine(_dir, "hidhide-session.json");
        _exePath = Path.Combine(_dir, "VortexPad.App.exe");
        File.WriteAllText(_exePath, "fake");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private HidHideIntegrationService CreateService() => new(_cli, _sessionPath, _exePath);

    [Fact]
    public void Hide_RegistersApp_HidesDevice_EnablesCloak_AndSavesSession()
    {
        var service = CreateService();

        Assert.True(service.HidePhysicalDevices(new[] { PadUsb }));

        Assert.Contains(_exePath, _cli.Apps);
        Assert.Contains(PadUsb, _cli.Hidden);
        Assert.True(_cli.CloakOn);
        Assert.True(service.IsDeviceCurrentlyCloaked);
        Assert.True(File.Exists(_sessionPath));
    }

    [Fact]
    public void Restore_UndoesOnlyWhatVortexPadChanged()
    {
        // The user already had another device hidden and the cloak on
        _cli.Hidden.Add(UserHiddenDevice);
        _cli.CloakOn = true;
        var service = CreateService();

        service.HidePhysicalDevices(new[] { PadUsb });
        service.RestorePhysicalDevice();

        Assert.DoesNotContain(PadUsb, _cli.Hidden);
        Assert.Contains(UserHiddenDevice, _cli.Hidden);  // not touched
        Assert.True(_cli.CloakOn);                        // it was already on: not turned off
        Assert.Equal(0, _cli.CountCalls("--cloak-off"));
        Assert.False(File.Exists(_sessionPath));
    }

    [Fact]
    public void Restore_TurnsCloakOff_OnlyIfVortexPadTurnedItOn()
    {
        var service = CreateService();
        service.HidePhysicalDevices(new[] { PadUsb });
        Assert.True(_cli.CloakOn);

        service.RestorePhysicalDevice();
        Assert.False(_cli.CloakOn);
    }

    [Fact]
    public void DeviceTheUserAlreadyHid_IsNotUnhiddenOnRestore()
    {
        _cli.Hidden.Add(PadUsb); // hidden by the user beforehand
        var service = CreateService();

        service.HidePhysicalDevices(new[] { PadUsb, PadSecondInterface });
        service.RestorePhysicalDevice();

        Assert.Contains(PadUsb, _cli.Hidden);
        Assert.DoesNotContain(PadSecondInterface, _cli.Hidden);
        Assert.Equal(1, _cli.CountCalls("--dev-hide"));
    }

    [Fact]
    public void FailureMidway_RollsBack_AndReportsTheCliError()
    {
        _cli.Override = args => args.Contains(PadSecondInterface) && args.StartsWith("--dev-hide")
            ? new HidHideCliResult(5, "Access denied")
            : null;
        var service = CreateService();

        Assert.False(service.HidePhysicalDevices(new[] { PadUsb, PadSecondInterface }));

        Assert.Empty(_cli.Hidden);            // the first one was unhidden again
        Assert.False(_cli.CloakOn);
        Assert.False(service.IsDeviceCurrentlyCloaked);
        Assert.Contains("Access denied", service.StatusText);
        Assert.Contains("administrador", service.StatusText);
        Assert.False(File.Exists(_sessionPath));
    }

    [Fact]
    public void AppRegistrationFailure_DoesNotHideAnything()
    {
        _cli.Override = args => args.StartsWith("--app-reg") ? new HidHideCliResult(1, "") : null;
        var service = CreateService();

        Assert.False(service.HidePhysicalDevices(new[] { PadUsb }));
        Assert.Empty(_cli.Hidden);
        Assert.Contains("código 1", service.StatusText);
    }

    [Fact]
    public void ChangingDevice_RestoresThePreviousOneFirst()
    {
        const string OtherPad = @"HID\VID_054C&PID_09CC\8&222&0&0000";
        var service = CreateService();

        service.HidePhysicalDevices(new[] { PadUsb });
        service.HidePhysicalDevices(new[] { OtherPad });

        Assert.DoesNotContain(PadUsb, _cli.Hidden);
        Assert.Contains(OtherPad, _cli.Hidden);
        Assert.True(_cli.CloakOn);
    }

    [Fact]
    public void RecoverPreviousSession_UndoesWhatACrashedSessionLeftHidden()
    {
        // Session 1 hides and "crashes" (no Restore or Dispose)
        CreateService().HidePhysicalDevices(new[] { PadUsb });
        Assert.True(File.Exists(_sessionPath));

        // Session 2 starts and recovers
        var next = CreateService();
        next.RecoverPreviousSession();

        Assert.Empty(_cli.Hidden);
        Assert.False(_cli.CloakOn);
        Assert.False(File.Exists(_sessionPath));
        Assert.Contains("sesión anterior", next.StatusText);
    }

    [Fact]
    public void RecoverPreviousSession_WithCorruptFile_DoesNotThrow_AndDeletesIt()
    {
        File.WriteAllText(_sessionPath, "{ roto");
        var service = CreateService();

        service.RecoverPreviousSession();

        Assert.False(File.Exists(_sessionPath));
        Assert.Equal(0, _cli.CountCalls("--dev-unhide"));
    }

    [Fact]
    public void NotInstalled_DoesNothing()
    {
        _cli.IsAvailable = false;
        var service = CreateService();

        Assert.False(service.IsHidHideInstalled);
        Assert.False(service.HidePhysicalDevices(new[] { PadUsb }));
        service.RestorePhysicalDevice();
        service.RecoverPreviousSession();
        Assert.Empty(_cli.Calls);
        Assert.Contains("no instalado", service.StatusText);
    }

    [Fact]
    public void NoInstanceIds_ReturnsFalse_WithExplanation()
    {
        var service = CreateService();
        Assert.False(service.HidePhysicalDevices(Array.Empty<string>()));
        Assert.Contains("no tiene interfaz HID", service.StatusText);
        Assert.Empty(_cli.Hidden);
    }

    [Fact]
    public void Dispose_RestoresTheDevice()
    {
        var service = CreateService();
        service.HidePhysicalDevices(new[] { PadUsb });

        service.Dispose();

        Assert.Empty(_cli.Hidden);
        Assert.False(_cli.CloakOn);
    }
}
