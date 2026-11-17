using VortexPad.Core.Models;
using VortexPad.Core.Profiles;
using VortexPad.ViewModels;
using Xunit;

namespace VortexPad.CoreTests;

public class ProfileEditorViewModelTests
{
    private static (ProfileEditorViewModel Vm, ControllerProfile Profile, List<string> Edits) Create()
    {
        var profile = BuiltInPresets.CreateAiSmartPreset();
        var vm = new ProfileEditorViewModel();
        vm.Load(profile);
        var edits = new List<string>();
        vm.ProfileEdited += edits.Add;
        return (vm, profile, edits);
    }

    [Fact]
    public void Sticks_AreEditedIndependently_AndNotifyTheRoot()
    {
        var (vm, profile, edits) = Create();

        vm.LeftStick.InnerDeadzone = 0.12;
        vm.RightStick.Sensitivity = 1.7;

        Assert.Equal(0.12, profile.LeftStick.InnerDeadzone);
        Assert.Equal(0.04, profile.RightStick.InnerDeadzone); // unchanged
        Assert.Equal(1.7, profile.RightStick.Sensitivity);
        Assert.Equal(new[] { "InnerDeadzone", "Sensitivity" }, edits);
    }

    [Fact]
    public void Ranges_AreClamped_AndNoOpEditsDoNotNotify()
    {
        var (vm, profile, edits) = Create();

        vm.LeftStick.InnerDeadzone = 5.0;
        vm.RightTrigger.HairTriggerThreshold = -1;
        vm.Gyro.SmoothingFactor = 3;
        vm.PollingRateHz = 10;

        Assert.Equal(0.85, profile.LeftStick.InnerDeadzone);
        Assert.Equal(0.01, profile.RightTrigger.HairTriggerThreshold);
        Assert.Equal(0.95, profile.Gyro.SmoothingFactor);
        Assert.Equal(60, profile.PollingRateHz);

        edits.Clear();
        vm.LeftStick.InnerDeadzone = 0.85; // same value
        vm.LeftStick.InnerDeadzone = double.NaN;
        Assert.Empty(edits);
    }

    [Fact]
    public void ConditionalFields_FollowTheMode()
    {
        var (vm, _, _) = Create();
        var changed = new List<string?>();
        vm.RightStick.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.RightStick.CurveType = ResponseCurveType.CustomBezier;
        Assert.True(vm.RightStick.IsBezier);
        Assert.False(vm.RightStick.UsesExponent);
        Assert.Contains(nameof(StickViewModel.IsBezier), changed);

        vm.Gyro.ActivationMode = GyroActivationMode.ToggleButton;
        Assert.True(vm.Gyro.NeedsActivationButton);
        vm.Gyro.ActivationButton = ControllerButton.None; // not allowed: keeps the previous one
        Assert.NotEqual(ControllerButton.None, vm.Gyro.ActivationButton);
    }

    [Fact]
    public void SampleResponse_ReflectsDeadzoneAndBezier()
    {
        var (vm, _, _) = Create();
        var stick = vm.RightStick;
        stick.InnerDeadzone = 0.2;
        stick.AntiDeadzone = 0;
        stick.CurveType = ResponseCurveType.CustomBezier;
        stick.BezierP1 = 0.0;
        stick.BezierP2 = 0.0;

        var points = stick.SampleResponse(100);
        Assert.Equal(101, points.Count);
        Assert.All(points.Where(p => p.Input <= 0.2), p => Assert.Equal(0.0, p.Output));
        Assert.Equal(1.0, points[^1].Output, 3);

        // With both control points low, the curve stays below the diagonal in the middle
        var mid = points.First(p => p.Input >= 0.6);
        Assert.True(mid.Output < 0.5);
        Assert.Equal(0.2, stick.BezierHandleInput(0.0), 6);
    }

    [Fact]
    public void LightbarHex_IsValidatedAndNormalized()
    {
        var (vm, profile, _) = Create();

        vm.LightbarHex = "ff8800";
        Assert.Equal("#FF8800", profile.LightbarHex);
        Assert.Null(vm.LightbarError);

        vm.LightbarHex = "rojo";
        Assert.Equal("#FF8800", profile.LightbarHex);
        Assert.NotNull(vm.LightbarError);
    }

    [Fact]
    public void Name_CannotBeEmpty_AndLinkedProcessesAreParsed()
    {
        var (vm, profile, edits) = Create();
        string original = profile.Name;

        vm.Name = "   ";
        Assert.Equal(original, profile.Name);

        vm.LinkedProcessesText = "eldenring, cod.exe ,, eldenring";
        Assert.Equal(new[] { "eldenring", "cod.exe" }, profile.LinkedProcessNames);
        Assert.Contains(nameof(ProfileEditorViewModel.LinkedProcessesText), edits);
    }

    [Fact]
    public void ShiftLayers_AddRemove_AndRemapsSyncToTheModel()
    {
        var (vm, profile, edits) = Create();
        int before = profile.ShiftLayers.Count;

        vm.AddShiftLayerCommand.Execute(null);
        Assert.Equal(before + 1, profile.ShiftLayers.Count);
        var layer = vm.SelectedLayer!;
        Assert.True(layer.IsShiftLayer);
        Assert.NotEqual(ControllerButton.None, layer.ShiftModifierButton);

        layer.AddRemapCommand.Execute(null);
        var row = layer.Remaps[0];
        row.Source = ControllerButton.North;
        row.Target = ControllerButton.West;
        Assert.Equal(ControllerButton.West, layer.Model.ButtonRemaps[ControllerButton.North]);

        layer.AddKeyBindingCommand.Execute(null);
        layer.KeyBindings[0].Source = ControllerButton.North; // same button as the remap → warning
        Assert.True(layer.HasDuplicateSources);
        layer.KeyBindings[0].VirtualKey = 0x51; // Q
        Assert.Equal((ushort)0x51, layer.Model.ButtonToKeyboardKey[ControllerButton.North]);

        layer.RemoveRemapCommand.Execute(row);
        Assert.Empty(layer.Model.ButtonRemaps);

        vm.RemoveShiftLayerCommand.Execute(layer);
        Assert.Equal(before, profile.ShiftLayers.Count);
        Assert.Same(vm.BaseLayer, vm.SelectedLayer);
        Assert.Contains("ShiftLayers", edits);
    }

    [Fact]
    public void BaseLayer_CannotHaveAModifierOrBeRemoved()
    {
        var (vm, profile, _) = Create();

        vm.BaseLayer.ShiftModifierButton = ControllerButton.South;
        Assert.Null(profile.BaseLayer.ShiftModifierButton);

        vm.RemoveShiftLayerCommand.Execute(vm.BaseLayer);
        Assert.NotNull(profile.BaseLayer);
    }

    [Fact]
    public void Turbos_AddEditRemove()
    {
        var (vm, profile, _) = Create();
        int before = profile.TurboConfigs.Count;

        vm.AddTurboCommand.Execute(null);
        var turbo = vm.Turbos[^1];
        Assert.NotEqual(profile.TurboConfigs[0].Button, turbo.Button); // picks a free button
        turbo.FrequencyHz = 500;
        Assert.Equal(100, turbo.Model.FrequencyHz);

        vm.RemoveTurboCommand.Execute(turbo);
        Assert.Equal(before, profile.TurboConfigs.Count);
    }

    [Fact]
    public void MacroSteps_AddMoveRemove_KeepModelOrder()
    {
        var (vm, profile, _) = Create();

        vm.AddMacroCommand.Execute(null);
        var macro = vm.SelectedMacro!;
        Assert.Same(profile.Macros[^1], macro.Model);

        macro.AddStepCommand.Execute(null);
        macro.Steps[1].StepType = MacroStepType.WaitMilliseconds;
        macro.Steps[1].DurationMs = 120;
        Assert.False(macro.Steps[1].UsesButton);
        Assert.Equal(170, macro.TotalDurationMs);

        macro.MoveStepUpCommand.Execute(macro.Steps[1]);
        Assert.Equal(MacroStepType.WaitMilliseconds, macro.Model.Steps[0].StepType);
        Assert.Equal(MacroStepType.WaitMilliseconds, macro.Steps[0].StepType);

        macro.Steps[0].DurationMs = 0; // minimum 1 ms
        Assert.Equal(1, macro.Model.Steps[0].DurationMs);

        macro.RemoveStepCommand.Execute(macro.Steps[0]);
        Assert.Single(macro.Model.Steps);
        Assert.Equal(MacroStepType.TapButton, macro.Model.Steps[0].StepType);

        vm.RemoveMacroCommand.Execute(macro);
        Assert.DoesNotContain(macro.Model, profile.Macros);
    }

    [Fact]
    public void Load_ReplacesEverySection_AndNotifiesAllProperties()
    {
        var (vm, _, edits) = Create();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        var xbox = BuiltInPresets.CreateXboxStandardPreset();
        vm.Load(xbox);

        Assert.Same(xbox, vm.Profile);
        Assert.Equal(xbox.Name, vm.Name);
        Assert.Empty(vm.ShiftLayers);
        Assert.Contains(string.Empty, raised);
        Assert.Empty(edits); // loading is not an edit

        vm.CurveStickIndex = 0;
        Assert.Same(vm.LeftStick, vm.CurveStick);
    }
}

public class ProfileRepositoryManagementTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vortexpad-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void Delete_RemovesCustomProfile_AndRestoresBuiltInPreset()
    {
        var repo = new ProfileRepository(_dir);
        var custom = repo.CloneProfile(BuiltInPresets.CreateXboxStandardPreset(), "Mío");
        repo.SaveProfile(custom);

        var preset = BuiltInPresets.CreateXboxStandardPreset();
        preset.Name = "Xbox modificado";
        repo.SaveProfile(preset);

        Assert.Contains(repo.LoadAllWithBuiltIns(), p => p.Name == "Xbox modificado");

        Assert.True(repo.DeleteProfile(custom.Id));
        Assert.True(repo.DeleteProfile(preset.Id));
        Assert.False(repo.DeleteProfile(preset.Id)); // nothing left

        var all = repo.LoadAllWithBuiltIns();
        Assert.DoesNotContain(all, p => p.Id == custom.Id);
        Assert.Equal(BuiltInPresets.CreateXboxStandardPreset().Name, all.Single(p => p.Id == preset.Id).Name);
    }

    [Fact]
    public void ExportImport_RoundTrips_AndAvoidsIdCollisions()
    {
        Directory.CreateDirectory(_dir);
        string file = Path.Combine(_dir, "export.json");
        var original = BuiltInPresets.CreateAiSmartPreset();
        original.Macros.Add(new MacroDefinition { Name = "M", Steps = { new MacroStep { DurationMs = 33 } } });

        ProfileRepository.ExportProfile(original, file);
        var imported = ProfileRepository.ImportProfile(file, new[] { original.Id });

        Assert.NotEqual(original.Id, imported.Id);   // collides with the preset → new Id
        Assert.False(imported.IsBuiltInPreset);
        Assert.Equal("Custom", imported.Category);
        Assert.Equal(original.Name, imported.Name);
        Assert.Equal(33, imported.Macros.Single().Steps.Single().DurationMs);
        Assert.Equal(original.ShiftLayers.Count, imported.ShiftLayers.Count);

        var noCollision = ProfileRepository.ImportProfile(file, Array.Empty<string>());
        Assert.Equal(original.Id, noCollision.Id);
    }

    [Fact]
    public void Import_RejectsInvalidFiles()
    {
        Directory.CreateDirectory(_dir);
        string file = Path.Combine(_dir, "bad.json");
        File.WriteAllText(file, "{ esto no es json");

        Assert.Throws<InvalidDataException>(() => ProfileRepository.ImportProfile(file, Array.Empty<string>()));
    }
}
