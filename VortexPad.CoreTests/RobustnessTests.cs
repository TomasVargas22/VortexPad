using VortexPad.Core.Devices;
using VortexPad.Core.Diagnostics;
using VortexPad.Core.Settings;
using VortexPad.ViewModels;
using Xunit;

namespace VortexPad.CoreTests;

/// <summary>VortexLog is global: these tests do not run in parallel with others that touch it.</summary>
[CollectionDefinition("VortexLog", DisableParallelization = true)]
public class VortexLogCollection
{
}

[Collection("VortexLog")]
public class VortexLogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vortexpad-log-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        VortexLog.Configure(null);
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private sealed class MemorySink : ILogSink
    {
        public List<LogEntry> Entries { get; } = new();
        public void Enqueue(in LogEntry entry) { lock (Entries) Entries.Add(entry); }
        public void Flush(TimeSpan timeout) { }
    }

    [Fact]
    public void FileSink_WritesFormattedLines_WithException()
    {
        using var sink = new RotatingFileLogSink(_dir);
        VortexLog.Configure(sink);

        VortexLog.Info("Prueba", "hola");
        VortexLog.Error("Prueba", "falló", new InvalidOperationException("detalle"));
        VortexLog.Flush(TimeSpan.FromSeconds(2));

        string text = File.ReadAllText(sink.CurrentFilePath);
        Assert.Contains("INF [Prueba] hola", text);
        Assert.Contains("ERR [Prueba] falló", text);
        Assert.Contains("InvalidOperationException: detalle", text);
    }

    [Fact]
    public void FileSink_RotatesAndKeepsAtMostMaxFiles()
    {
        using var sink = new RotatingFileLogSink(_dir, maxBytes: 2048, maxFiles: 3);
        VortexLog.Configure(sink);

        string payload = new('x', 400);
        for (int i = 0; i < 60; i++) VortexLog.Info("Rotación", $"{i:D3} {payload}");
        VortexLog.Flush(TimeSpan.FromSeconds(5));

        var files = Directory.GetFiles(_dir, "vortexpad*.log").Select(Path.GetFileName).OrderBy(f => f).ToArray();
        Assert.Equal(new[] { "vortexpad.1.log", "vortexpad.2.log", "vortexpad.log" }, files);
        Assert.All(Directory.GetFiles(_dir), f => Assert.True(new FileInfo(f).Length < 2048 + 1024));
        Assert.Contains("059", File.ReadAllText(sink.CurrentFilePath)); // the newest is in the current file
    }

    [Fact]
    public void ErrorThrottled_WritesOncePerInterval_AndCountsSuppressed()
    {
        var sink = new MemorySink();
        VortexLog.Configure(sink);

        Assert.True(VortexLog.ErrorThrottled("k", TimeSpan.FromMilliseconds(200), "Src", "boom"));
        for (int i = 0; i < 50; i++) Assert.False(VortexLog.ErrorThrottled("k", TimeSpan.FromMilliseconds(200), "Src", "boom"));
        Assert.True(VortexLog.ErrorThrottled("otra", TimeSpan.FromMilliseconds(200), "Src", "distinto")); // separate key

        Thread.Sleep(250);
        Assert.True(VortexLog.ErrorThrottled("k", TimeSpan.FromMilliseconds(200), "Src", "boom"));

        Assert.Equal(3, sink.Entries.Count);
        Assert.Contains("+50 repeticiones omitidas", sink.Entries[2].Message);
    }

    [Fact]
    public void Levels_BelowMinimum_AreDiscarded_AndNoSinkIsSafe()
    {
        VortexLog.Configure(null);
        VortexLog.Error("X", "no sink, no crash");
        Assert.False(VortexLog.ErrorThrottled("k2", TimeSpan.FromSeconds(1), "X", "y"));

        var sink = new MemorySink();
        VortexLog.Configure(sink);
        VortexLog.Debug("X", "invisible"); // default MinimumLevel = Info
        VortexLog.Warn("X", "visible");
        Assert.Single(sink.Entries);
        Assert.Equal(LogLevel.Warning, sink.Entries[0].Level);
    }
}

public class AppSettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vortexpad-settings-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_dir, "settings.json");

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void Store_MissingOrCorruptFile_GivesDefaults()
    {
        var store = new AppSettingsStore(FilePath);
        var defaults = store.Load();
        Assert.True(defaults.AutoSwitchEnabled);
        Assert.True(defaults.StartEmulationOnLaunch);
        Assert.False(defaults.StartWithWindows);

        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "{ roto");
        Assert.True(store.Load().MinimizeToTray);
    }

    [Fact]
    public void Store_RoundTrips_WithoutLeavingTempFiles()
    {
        var store = new AppSettingsStore(FilePath);
        store.Save(new AppSettings { LastProfileId = "p1", LastDeviceId = "d1", AutoSwitchEnabled = false, StartWithWindows = true });

        var loaded = store.Load();
        Assert.Equal("p1", loaded.LastProfileId);
        Assert.Equal("d1", loaded.LastDeviceId);
        Assert.False(loaded.AutoSwitchEnabled);
        Assert.True(loaded.StartWithWindows);
        Assert.False(File.Exists(FilePath + ".tmp"));
    }

    [Fact]
    public void ViewModel_SavesEachChange_AndRaisesSettingChanged()
    {
        var vm = new AppSettingsViewModel(new AppSettingsStore(FilePath));
        var changed = new List<string>();
        vm.SettingChanged += changed.Add;

        vm.AutoSwitchEnabled = false;
        vm.AutoSwitchEnabled = false; // no change → no event
        vm.StartWithWindows = true;
        vm.RememberProfile("perfil-x");   // saved, no event
        vm.RememberDevice("mando-y");

        Assert.Equal(new[] { "AutoSwitchEnabled", "StartWithWindows" }, changed);

        var reloaded = new AppSettingsStore(FilePath).Load();
        Assert.False(reloaded.AutoSwitchEnabled);
        Assert.True(reloaded.StartWithWindows);
        Assert.Equal("perfil-x", reloaded.LastProfileId);
        Assert.Equal("mando-y", reloaded.LastDeviceId);
    }
}

public class DeviceSelectionPolicyTests
{
    private sealed record Dev(string Id, string Name, bool Missing = false);

    private static DeviceSelectionPolicy.Result<Dev> Merge(IReadOnlyList<Dev> fresh, Dev? previous, string? savedId) =>
        DeviceSelectionPolicy.Merge(fresh, previous, savedId, d => d.Id, d => d with { Name = d.Name + " (desconectado)", Missing = true });

    private static readonly Dev PadA = new("a", "Mando A");
    private static readonly Dev PadB = new("b", "Mando B");
    private static readonly Dev Virtual = new("virtual", "Simulador");

    [Fact]
    public void KeepsCurrentSelection_WhenStillConnected()
    {
        var r = Merge(new[] { PadA, PadB, Virtual }, PadB, savedId: "a");
        Assert.Same(PadB, r.Selected);
        Assert.False(r.SelectedIsMissing);
        Assert.Equal(3, r.Items.Count);
    }

    [Fact]
    public void UnpluggedController_StaysSelectedAsPlaceholder_ThenComesBack()
    {
        var unplugged = Merge(new[] { PadA, Virtual }, PadB, savedId: null);
        Assert.True(unplugged.SelectedIsMissing);
        Assert.Equal("Mando B (desconectado)", unplugged.Selected!.Name);
        Assert.Equal(3, unplugged.Items.Count);

        // Reconnected: the placeholder (same Id) matches the real device again
        var back = Merge(new[] { PadA, PadB, Virtual }, unplugged.Selected, savedId: null);
        Assert.Same(PadB, back.Selected);
        Assert.False(back.SelectedIsMissing);
        Assert.Equal(3, back.Items.Count);
    }

    [Fact]
    public void Startup_UsesSavedDevice_OrFallsBackToFirst()
    {
        Assert.Same(PadB, Merge(new[] { PadA, PadB, Virtual }, previous: null, savedId: "B").Selected); // case-insensitive
        Assert.Same(PadA, Merge(new[] { PadA, Virtual }, previous: null, savedId: "gone").Selected);
        Assert.Null(Merge(Array.Empty<Dev>(), previous: null, savedId: null).Selected);
    }
}
