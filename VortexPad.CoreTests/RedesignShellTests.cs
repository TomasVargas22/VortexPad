using VortexPad.Core.Models;
using VortexPad.Core.Profiles;
using VortexPad.ViewModels;
using Xunit;

namespace VortexPad.CoreTests;

/// <summary>Logic added for the redesign (R5): autosave, navigation, status chips and new editor commands.</summary>
public class RedesignShellTests
{
    // ---------------- Autosaver ----------------

    /// <summary>Delay controlled by the test: each scheduled save waits until <see cref="Release"/>.</summary>
    private sealed class ManualDelay
    {
        private readonly List<TaskCompletionSource> _waits = new();
        public int Scheduled => _waits.Count;

        public Task Wait(TimeSpan _, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            ct.Register(() => tcs.TrySetCanceled(ct));
            _waits.Add(tcs);
            return tcs.Task;
        }

        public void ReleaseAll()
        {
            foreach (var w in _waits) w.TrySetResult();
        }
    }

    private static (Autosaver<int> Saver, List<int> Saved, ManualDelay Delay, Func<int> SetValue) CreateSaver(Action<int>? persist = null)
    {
        int value = 0;
        var saved = new List<int>();
        var delay = new ManualDelay();
        var saver = new Autosaver<int>(() => value, persist ?? saved.Add, TimeSpan.FromMilliseconds(800), delay.Wait);
        return (saver, saved, delay, () => ++value);
    }

    [Fact]
    public async Task Autosaver_DebouncesEdits_AndSavesTheLatestSnapshotOnce()
    {
        var (saver, saved, delay, next) = CreateSaver();

        next(); saver.MarkDirty();
        next(); saver.MarkDirty();
        next(); saver.MarkDirty();
        Assert.Equal(SaveStatus.Pending, saver.Status);
        Assert.Empty(saved);

        delay.ReleaseAll();
        await saver.LastRun;

        Assert.Equal(new[] { 3 }, saved);
        Assert.Equal(SaveStatus.Saved, saver.Status);
        Assert.False(saver.HasPendingChanges);
    }

    [Fact]
    public async Task Autosaver_Flush_SavesPendingNow_AndTheTimerDoesNotSaveAgain()
    {
        var (saver, saved, delay, next) = CreateSaver();
        next(); saver.MarkDirty();

        Assert.True(saver.Flush());
        Assert.Equal(new[] { 1 }, saved);

        delay.ReleaseAll();
        await saver.LastRun;
        Assert.Single(saved);
        Assert.Equal(SaveStatus.Saved, saver.Status);
    }

    [Fact]
    public void Autosaver_FlushWithoutChanges_DoesNothing()
    {
        var (saver, saved, _, _) = CreateSaver();
        Assert.True(saver.Flush());
        Assert.Empty(saved);
        Assert.Equal(SaveStatus.Idle, saver.Status);
    }

    [Fact]
    public async Task Autosaver_ReportsFailure_WithTheErrorMessage()
    {
        var (saver, _, delay, next) = CreateSaver(_ => throw new IOException("disco lleno"));
        next(); saver.MarkDirty();

        delay.ReleaseAll();
        await saver.LastRun;

        Assert.Equal(SaveStatus.Failed, saver.Status);
        Assert.Equal("disco lleno", saver.LastError);
        Assert.False(saver.HasPendingChanges);
    }

    // ---------------- Navigation ----------------

    [Fact]
    public void Navigation_Ctrl3_GoesToTheLastEditorSection()
    {
        var nav = new NavigationState();
        Assert.Equal(AppSection.Sticks, nav.SectionForShortcut(3));

        nav.Navigate(AppSection.Gyro);
        nav.Navigate(AppSection.Macros); // Macros has its own shortcut: not an "editor section" for Ctrl+3
        Assert.Equal(AppSection.Gyro, nav.SectionForShortcut(3));
        Assert.Equal(AppSection.LivePanel, nav.SectionForShortcut(1));
        Assert.Equal(AppSection.Settings, nav.SectionForShortcut(5));
        Assert.Null(nav.SectionForShortcut(9));
    }

    [Fact]
    public void Navigation_DualSense_OnlyWhenAvailable_AndStaysVisibleWhileOpen()
    {
        var nav = new NavigationState();
        nav.Navigate(AppSection.DualSense);
        Assert.Equal(AppSection.LivePanel, nav.Current); // not available: ignored

        nav.DualSenseAvailable = true;
        nav.Navigate(AppSection.DualSense);
        Assert.Equal(AppSection.DualSense, nav.Current);

        nav.DualSenseAvailable = false; // controller unplugged while the section is open
        Assert.True(nav.DualSenseVisible);
        Assert.Equal(AppSection.DualSense, nav.SectionForShortcut(3));

        nav.Navigate(AppSection.LivePanel);
        Assert.False(nav.DualSenseVisible);
        Assert.Equal(AppSection.Sticks, nav.SectionForShortcut(3));
    }

    [Theory]
    [InlineData(1360, null, false)]
    [InlineData(1120, null, true)]
    [InlineData(1120, false, false)]
    [InlineData(1600, true, true)]
    public void Navigation_RailCollapse_FollowsWidthUnlessPinned(double width, bool? pinned, bool expected)
    {
        Assert.Equal(expected, NavigationState.ShouldCollapseRail(width, pinned));
    }

    // ---------------- Status chips ----------------

    [Fact]
    public void StatusItem_Worst_PicksTheMostSevere_AndAccessibleTextHasLabelAndValue()
    {
        var vigem = new StatusItem("ViGEmBus", "Listo", StatusSeverity.Ok);
        var hidHide = new StatusItem("HidHide");
        hidHide.Set("Requiere permisos", StatusSeverity.Warning);
        var saved = new StatusItem("", "Guardado", StatusSeverity.Ok);

        Assert.Same(hidHide, StatusItem.Worst(new[] { vigem, hidHide, saved }));
        Assert.Equal("HidHide: Requiere permisos", hidHide.AccessibleText);
        Assert.Equal("Guardado", saved.AccessibleText);
        Assert.Equal(StatusItem.GlyphFor(StatusSeverity.Warning), hidHide.Glyph);
        Assert.Null(StatusItem.Worst(Array.Empty<StatusItem>()));
    }

    // ---------------- Editor commands ----------------

    [Fact]
    public void ResetCurveStick_RestoresDefaults_OnlyForTheSelectedStick()
    {
        var profile = BuiltInPresets.CreateAiSmartPreset();
        var vm = new ProfileEditorViewModel();
        vm.Load(profile);
        vm.CurveStickIndex = 1;
        vm.RightStick.Sensitivity = 2.2;
        vm.RightStick.CurveType = ResponseCurveType.CustomBezier;
        double leftDeadzone = profile.LeftStick.InnerDeadzone;

        vm.ResetCurveStickCommand.Execute(null);

        var defaults = new StickConfiguration();
        Assert.Equal(defaults.Sensitivity, profile.RightStick.Sensitivity);
        Assert.Equal(defaults.CurveType, profile.RightStick.CurveType);
        Assert.Equal(defaults.InnerDeadzone, profile.RightStick.InnerDeadzone);
        Assert.Equal(leftDeadzone, profile.LeftStick.InnerDeadzone);
    }

    [Fact]
    public void AnalogSelection_Triggers_KeepsTheLastStickInTheCurve()
    {
        var vm = new ProfileEditorViewModel();
        vm.Load(BuiltInPresets.CreateAiSmartPreset());

        vm.AnalogSelection = 0;
        Assert.Same(vm.LeftStick, vm.CurveStick);
        Assert.True(vm.IsEditingStick);

        vm.AnalogSelection = 2;
        Assert.True(vm.IsEditingTriggers);
        Assert.Equal(2, vm.AnalogSelection);
        Assert.Same(vm.LeftStick, vm.CurveStick);

        vm.AnalogSelection = 1;
        Assert.False(vm.IsEditingTriggers);
        Assert.Same(vm.RightStick, vm.CurveStick);
    }

    [Fact]
    public void Macro_AddWaitStep_AddsAWait_AndUpdatesTheDuration()
    {
        var macro = new MacroDefinition { Name = "Prueba" };
        var vm = new MacroViewModel(macro, null);

        vm.AddStepCommand.Execute(null);
        vm.AddWaitStepCommand.Execute(null);

        Assert.Equal(MacroStepType.TapButton, macro.Steps[0].StepType);
        Assert.Equal(MacroStepType.WaitMilliseconds, macro.Steps[1].StepType);
        Assert.Equal(50 + 80, vm.TotalDurationMs);
    }
}
