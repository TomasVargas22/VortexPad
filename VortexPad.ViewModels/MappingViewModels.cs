using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using VortexPad.Core.Models;
using VortexPad.Core.Profiles;

namespace VortexPad.ViewModels;

/// <summary>Row "physical button → virtual button".</summary>
public sealed class ButtonRemapRow : EditableViewModel
{
    private ControllerButton _source;
    private ControllerButton _target;

    public ButtonRemapRow(ControllerButton source, ControllerButton target, Action<string> onEdited) : base(onEdited)
    {
        _source = source;
        _target = target;
    }

    public ControllerButton Source
    {
        get => _source;
        set => Edit(_source, value, v => _source = v);
    }

    /// <summary>Target button; "(ninguno)" disables the physical button.</summary>
    public ControllerButton Target
    {
        get => _target;
        set => Edit(_target, value, v => _target = v);
    }
}

/// <summary>Row "physical button → keyboard key".</summary>
public sealed class KeyBindingRow : EditableViewModel
{
    private ControllerButton _source;
    private ushort _virtualKey;

    public KeyBindingRow(ControllerButton source, ushort virtualKey, Action<string> onEdited) : base(onEdited)
    {
        _source = source;
        _virtualKey = virtualKey;
    }

    public ControllerButton Source
    {
        get => _source;
        set => Edit(_source, value, v => _source = v);
    }

    public ushort VirtualKey
    {
        get => _virtualKey;
        set => Edit(_virtualKey, value, v => _virtualKey = v);
    }
}

/// <summary>
/// Mapping layer (base or Shift). The remap and key rows are written back to the model's dictionaries
/// on every change; if two rows use the same physical button, the last one wins and <see cref="HasDuplicateSources"/> warns.
/// </summary>
public sealed class MappingLayerViewModel : EditableViewModel
{
    private readonly MappingLayer _m;

    public MappingLayerViewModel(MappingLayer model, bool isBaseLayer, Action<string>? onEdited) : base(onEdited)
    {
        _m = model;
        IsBaseLayer = isBaseLayer;

        foreach (var (src, dst) in model.ButtonRemaps)
            Remaps.Add(new ButtonRemapRow(src, dst, OnRowEdited));
        foreach (var (src, vk) in model.ButtonToKeyboardKey)
            KeyBindings.Add(new KeyBindingRow(src, vk, OnRowEdited));

        AddRemapCommand = new RelayCommand(AddRemap);
        RemoveRemapCommand = new RelayCommand<ButtonRemapRow>(r => { if (r != null && Remaps.Remove(r)) SyncToModel(); });
        AddKeyBindingCommand = new RelayCommand(AddKeyBinding);
        RemoveKeyBindingCommand = new RelayCommand<KeyBindingRow>(r => { if (r != null && KeyBindings.Remove(r)) SyncToModel(); });
    }

    public MappingLayer Model => _m;
    public bool IsBaseLayer { get; }
    public bool IsShiftLayer => !IsBaseLayer;

    public ObservableCollection<ButtonRemapRow> Remaps { get; } = new();
    public ObservableCollection<KeyBindingRow> KeyBindings { get; } = new();

    public IRelayCommand AddRemapCommand { get; }
    public IRelayCommand<ButtonRemapRow> RemoveRemapCommand { get; }
    public IRelayCommand AddKeyBindingCommand { get; }
    public IRelayCommand<KeyBindingRow> RemoveKeyBindingCommand { get; }

    public string Name
    {
        get => _m.Name;
        set => Edit(_m.Name, string.IsNullOrWhiteSpace(value) ? _m.Name : value.Trim(), v => _m.Name = v);
    }

    /// <summary>Button that activates the layer while held (Shift layers only; "(ninguno)" is not allowed).</summary>
    public ControllerButton ShiftModifierButton
    {
        get => _m.ShiftModifierButton ?? ControllerButton.None;
        set
        {
            if (IsBaseLayer || value == ControllerButton.None) return;
            Edit(_m.ShiftModifierButton ?? ControllerButton.None, value, v => _m.ShiftModifierButton = v);
        }
    }

    public bool LeftStickToWasd
    {
        get => _m.LeftStickToWasd;
        set => Edit(_m.LeftStickToWasd, value, v => _m.LeftStickToWasd = v);
    }

    public bool RightStickToMouse
    {
        get => _m.RightStickToMouse;
        set => Edit(_m.RightStickToMouse, value, v => _m.RightStickToMouse = v);
    }

    public double StickMouseSensitivity
    {
        get => _m.StickMouseSensitivity;
        set => EditClamped(_m.StickMouseSensitivity, value, 1.0, 50.0, v => _m.StickMouseSensitivity = v);
    }

    public bool HasDuplicateSources =>
        Remaps.Select(r => r.Source).Concat(KeyBindings.Select(k => k.Source))
            .Where(b => b != ControllerButton.None)
            .GroupBy(b => b).Any(g => g.Count() > 1);

    private void AddRemap()
    {
        Remaps.Add(new ButtonRemapRow(FirstFreeSource(), ControllerButton.South, OnRowEdited));
        SyncToModel();
    }

    private void AddKeyBinding()
    {
        KeyBindings.Add(new KeyBindingRow(FirstFreeSource(), 0x20, OnRowEdited));
        SyncToModel();
    }

    private ControllerButton FirstFreeSource()
    {
        var used = Remaps.Select(r => r.Source).Concat(KeyBindings.Select(k => k.Source)).ToHashSet();
        return EditorOptions.RealButtons.Select(o => o.Value).FirstOrDefault(b => !used.Contains(b), ControllerButton.Paddle1);
    }

    private void OnRowEdited(string _) => SyncToModel();

    private void SyncToModel()
    {
        _m.ButtonRemaps = Remaps
            .Where(r => r.Source != ControllerButton.None)
            .GroupBy(r => r.Source)
            .ToDictionary(g => g.Key, g => g.Last().Target);

        _m.ButtonToKeyboardKey = KeyBindings
            .Where(k => k.Source != ControllerButton.None && k.VirtualKey != 0)
            .GroupBy(k => k.Source)
            .ToDictionary(g => g.Key, g => g.Last().VirtualKey);

        OnPropertyChanged(nameof(HasDuplicateSources));
        NotifyEdited(IsBaseLayer ? "BaseLayer" : "ShiftLayers");
    }
}

/// <summary>Turbo / rapid-fire for one button.</summary>
public sealed class TurboViewModel : EditableViewModel
{
    private readonly TurboButtonConfig _m;

    public TurboViewModel(TurboButtonConfig model, Action<string>? onEdited) : base(onEdited)
    {
        _m = model;
    }

    public TurboButtonConfig Model => _m;

    public ControllerButton Button
    {
        get => _m.Button;
        set => Edit(_m.Button, value == ControllerButton.None ? _m.Button : value, v => _m.Button = v);
    }

    public bool Enabled
    {
        get => _m.Enabled;
        set => Edit(_m.Enabled, value, v => _m.Enabled = v);
    }

    public double FrequencyHz
    {
        get => _m.FrequencyHz;
        set => EditClamped(_m.FrequencyHz, value, 0.5, 100.0, v => _m.FrequencyHz = v);
    }

    public double DutyCycle
    {
        get => _m.DutyCycle;
        set => EditClamped(_m.DutyCycle, value, 0.1, 0.9, v => _m.DutyCycle = v);
    }
}

/// <summary>One macro step.</summary>
public sealed class MacroStepViewModel : EditableViewModel
{
    private readonly MacroStep _m;

    public MacroStepViewModel(MacroStep model, Action<string>? onEdited) : base(onEdited)
    {
        _m = model;
    }

    public MacroStep Model => _m;

    public MacroStepType StepType
    {
        get => _m.StepType;
        set
        {
            if (Edit(_m.StepType, value, v => _m.StepType = v))
            {
                OnPropertyChanged(nameof(UsesButton));
                OnPropertyChanged(nameof(UsesKey));
            }
        }
    }

    public bool UsesButton => _m.StepType is MacroStepType.PressButton or MacroStepType.ReleaseButton or MacroStepType.TapButton;
    public bool UsesKey => _m.StepType is MacroStepType.PressVirtualKey or MacroStepType.ReleaseVirtualKey;

    public ControllerButton Button
    {
        get => _m.Button;
        set => Edit(_m.Button, value, v => _m.Button = v);
    }

    public ushort VirtualKeyCode
    {
        get => _m.VirtualKeyCode;
        set => Edit(_m.VirtualKeyCode, value, v => _m.VirtualKeyCode = v);
    }

    public int DurationMs
    {
        get => _m.DurationMs;
        set => Edit(_m.DurationMs, Math.Clamp(value, 1, 10_000), v => _m.DurationMs = v);
    }
}

/// <summary>Macro: trigger button, repetition and an ordered list of steps.</summary>
public sealed class MacroViewModel : EditableViewModel
{
    private readonly MacroDefinition _m;

    public MacroViewModel(MacroDefinition model, Action<string>? onEdited) : base(onEdited)
    {
        _m = model;
        foreach (var step in model.Steps) Steps.Add(new MacroStepViewModel(step, OnStepEdited));

        AddStepCommand = new RelayCommand(() => AddStep(MacroStepType.TapButton));
        AddWaitStepCommand = new RelayCommand(() => AddStep(MacroStepType.WaitMilliseconds));
        RemoveStepCommand = new RelayCommand<MacroStepViewModel>(RemoveStep);
        MoveStepUpCommand = new RelayCommand<MacroStepViewModel>(s => MoveStep(s, -1));
        MoveStepDownCommand = new RelayCommand<MacroStepViewModel>(s => MoveStep(s, +1));
    }

    public MacroDefinition Model => _m;
    public ObservableCollection<MacroStepViewModel> Steps { get; } = new();

    /// <summary>Adds a "tap button" step.</summary>
    public IRelayCommand AddStepCommand { get; }
    /// <summary>Adds a "wait" step.</summary>
    public IRelayCommand AddWaitStepCommand { get; }
    public IRelayCommand<MacroStepViewModel> RemoveStepCommand { get; }
    public IRelayCommand<MacroStepViewModel> MoveStepUpCommand { get; }
    public IRelayCommand<MacroStepViewModel> MoveStepDownCommand { get; }

    public string Name
    {
        get => _m.Name;
        set => Edit(_m.Name, string.IsNullOrWhiteSpace(value) ? _m.Name : value.Trim(), v => _m.Name = v);
    }

    public ControllerButton TriggerButton
    {
        get => _m.TriggerButton;
        set => Edit(_m.TriggerButton, value, v => _m.TriggerButton = v);
    }

    public bool RepeatWhileHeld
    {
        get => _m.RepeatWhileHeld;
        set => Edit(_m.RepeatWhileHeld, value, v => _m.RepeatWhileHeld = v);
    }

    /// <summary>Total duration of one run of the macro.</summary>
    public int TotalDurationMs => _m.Steps.Sum(s => Math.Max(1, s.DurationMs));

    private void AddStep(MacroStepType type)
    {
        var step = type == MacroStepType.WaitMilliseconds
            ? new MacroStep { StepType = MacroStepType.WaitMilliseconds, DurationMs = 80 }
            : new MacroStep { StepType = MacroStepType.TapButton, Button = ControllerButton.South, DurationMs = 50 };
        _m.Steps.Add(step);
        Steps.Add(new MacroStepViewModel(step, OnStepEdited));
        OnStepsChanged();
    }

    private void RemoveStep(MacroStepViewModel? step)
    {
        if (step == null) return;
        int index = Steps.IndexOf(step);
        if (index < 0) return;
        Steps.RemoveAt(index);
        _m.Steps.RemoveAt(index);
        OnStepsChanged();
    }

    private void MoveStep(MacroStepViewModel? step, int offset)
    {
        if (step == null) return;
        int from = Steps.IndexOf(step);
        int to = from + offset;
        if (from < 0 || to < 0 || to >= Steps.Count) return;
        Steps.Move(from, to);
        var model = _m.Steps[from];
        _m.Steps.RemoveAt(from);
        _m.Steps.Insert(to, model);
        OnStepsChanged();
    }

    private void OnStepsChanged()
    {
        OnPropertyChanged(nameof(TotalDurationMs));
        NotifyEdited("Macros");
    }

    private void OnStepEdited(string _)
    {
        OnPropertyChanged(nameof(TotalDurationMs));
        NotifyEdited("Macros");
    }
}
