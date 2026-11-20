using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using VortexPad.Core.Models;
using VortexPad.Core.Profiles;

namespace VortexPad.ViewModels;

/// <summary>
/// Root of the profile editor. Wraps the <see cref="ControllerProfile"/> that the UI edits (the window's
/// <c>_activeProfile</c>) and fires <see cref="ProfileEdited"/> on every change, with the name of what changed,
/// so the window publishes the snapshot to the 1000 Hz loop and reacts to changes that need it
/// (virtual controller type, HidHide, name, LED…).
/// </summary>
public sealed class ProfileEditorViewModel : EditableViewModel
{
    private ControllerProfile _profile = new();
    private string? _lightbarError;
    private int _curveStickIndex = 1;
    private MacroViewModel? _selectedMacro;
    private MappingLayerViewModel? _selectedLayer;

    public ProfileEditorViewModel() : base(null)
    {
        AddShiftLayerCommand = new RelayCommand(AddShiftLayer);
        RemoveShiftLayerCommand = new RelayCommand<MappingLayerViewModel>(RemoveShiftLayer);
        AddTurboCommand = new RelayCommand(AddTurbo);
        RemoveTurboCommand = new RelayCommand<TurboViewModel>(RemoveTurbo);
        AddMacroCommand = new RelayCommand(AddMacro);
        RemoveMacroCommand = new RelayCommand<MacroViewModel>(RemoveMacro);
        ResetCurveStickCommand = new RelayCommand(() => CurveStick.ResetToDefaults());
        Load(_profile);
    }

    /// <summary>Fired after any edit. The argument is the name of the property or section that changed.</summary>
    public event Action<string>? ProfileEdited;

    protected override void NotifyEdited(string propertyName) => ProfileEdited?.Invoke(propertyName);

    public ControllerProfile Profile => _profile;

    // ---------------- Sections ----------------

    public StickViewModel LeftStick { get; private set; } = null!;
    public StickViewModel RightStick { get; private set; } = null!;
    public TriggerViewModel LeftTrigger { get; private set; } = null!;
    public TriggerViewModel RightTrigger { get; private set; } = null!;
    public GyroViewModel Gyro { get; private set; } = null!;
    public ForceFeedbackViewModel ForceFeedback { get; private set; } = null!;
    public TouchpadViewModel Touchpad { get; private set; } = null!;
    public AdaptiveTriggerViewModel LeftAdaptiveTrigger { get; private set; } = null!;
    public AdaptiveTriggerViewModel RightAdaptiveTrigger { get; private set; } = null!;
    public MappingLayerViewModel BaseLayer { get; private set; } = null!;
    public ObservableCollection<MappingLayerViewModel> ShiftLayers { get; } = new();
    public ObservableCollection<TurboViewModel> Turbos { get; } = new();
    public ObservableCollection<MacroViewModel> Macros { get; } = new();

    public IRelayCommand AddShiftLayerCommand { get; }
    public IRelayCommand<MappingLayerViewModel> RemoveShiftLayerCommand { get; }
    public IRelayCommand AddTurboCommand { get; }
    public IRelayCommand<TurboViewModel> RemoveTurboCommand { get; }
    public IRelayCommand AddMacroCommand { get; }
    public IRelayCommand<MacroViewModel> RemoveMacroCommand { get; }
    /// <summary>Restores the defaults of the stick shown in the curve.</summary>
    public IRelayCommand ResetCurveStickCommand { get; }

    public MacroViewModel? SelectedMacro
    {
        get => _selectedMacro;
        set => SetProperty(ref _selectedMacro, value);
    }

    public MappingLayerViewModel? SelectedLayer
    {
        get => _selectedLayer;
        set => SetProperty(ref _selectedLayer, value);
    }

    /// <summary>0 = left stick, 1 = right stick: which one the curve graph shows and edits.</summary>
    public int CurveStickIndex
    {
        get => _curveStickIndex;
        set
        {
            if (SetProperty(ref _curveStickIndex, Math.Clamp(value, 0, 1)))
                OnPropertyChanged(nameof(CurveStick));
        }
    }

    public StickViewModel CurveStick => _curveStickIndex == 0 ? LeftStick : RightStick;

    private bool _editingTriggers;

    /// <summary>
    /// Selector of the "Sticks y gatillos" section: 0 = left stick, 1 = right stick, 2 = triggers.
    /// Choosing the triggers keeps the last stick in the curve.
    /// </summary>
    public int AnalogSelection
    {
        get => _editingTriggers ? 2 : _curveStickIndex;
        set
        {
            bool triggers = value == 2;
            if (!triggers) CurveStickIndex = value;
            if (_editingTriggers != triggers)
            {
                _editingTriggers = triggers;
                OnPropertyChanged(nameof(IsEditingTriggers));
                OnPropertyChanged(nameof(IsEditingStick));
            }
            OnPropertyChanged();
        }
    }

    public bool IsEditingTriggers => _editingTriggers;
    public bool IsEditingStick => !_editingTriggers;

    /// <summary>All layers (base + Shift) for the layer selector.</summary>
    public IEnumerable<MappingLayerViewModel> AllLayers => new[] { BaseLayer }.Concat(ShiftLayers);

    // ---------------- General ----------------

    public string Name
    {
        get => _profile.Name;
        set => Edit(_profile.Name, string.IsNullOrWhiteSpace(value) ? _profile.Name : value.Trim(), v => _profile.Name = v);
    }

    public string Description
    {
        get => _profile.Description;
        set => Edit(_profile.Description, value ?? string.Empty, v => _profile.Description = v);
    }

    public bool IsBuiltInPreset => _profile.IsBuiltInPreset;

    public VirtualControllerType TargetControllerType
    {
        get => _profile.TargetControllerType;
        set => Edit(_profile.TargetControllerType, value, v => _profile.TargetControllerType = v);
    }

    public bool HidePhysicalControllerWithHidHide
    {
        get => _profile.HidePhysicalControllerWithHidHide;
        set => Edit(_profile.HidePhysicalControllerWithHidHide, value, v => _profile.HidePhysicalControllerWithHidHide = v);
    }

    public int PollingRateHz
    {
        get => _profile.PollingRateHz;
        set => Edit(_profile.PollingRateHz, Math.Clamp(value, 60, 2000), v => _profile.PollingRateHz = v);
    }

    /// <summary>Lightbar color as #RRGGBB. Invalid values are not applied and set <see cref="LightbarError"/>.</summary>
    public string LightbarHex
    {
        get => _profile.LightbarHex;
        set
        {
            if (TryNormalizeHex(value, out string normalized))
            {
                LightbarError = null;
                Edit(_profile.LightbarHex, normalized, v => _profile.LightbarHex = v);
            }
            else
            {
                LightbarError = "Usa el formato #RRGGBB (por ejemplo #00E5FF).";
            }
        }
    }

    public string? LightbarError
    {
        get => _lightbarError;
        private set => SetProperty(ref _lightbarError, value);
    }

    /// <summary>Linked processes as "eldenring, cod, forza" (with or without .exe).</summary>
    public string LinkedProcessesText
    {
        get => string.Join(", ", _profile.LinkedProcessNames);
        set
        {
            var list = (value ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (list.SequenceEqual(_profile.LinkedProcessNames, StringComparer.OrdinalIgnoreCase)) return;
            _profile.LinkedProcessNames = list;
            OnPropertyChanged();
            NotifyEdited(nameof(LinkedProcessesText));
        }
    }

    // ---------------- Load ----------------

    /// <summary>Loads a profile for editing (edits are applied to that same instance).</summary>
    public void Load(ControllerProfile profile)
    {
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _lightbarError = null;

        LeftStick = new StickViewModel(profile.LeftStick, "Stick izquierdo", NotifyEdited);
        RightStick = new StickViewModel(profile.RightStick, "Stick derecho", NotifyEdited);
        LeftTrigger = new TriggerViewModel(profile.LeftTrigger, "Gatillo izquierdo (LT / L2)", NotifyEdited);
        RightTrigger = new TriggerViewModel(profile.RightTrigger, "Gatillo derecho (RT / R2)", NotifyEdited);
        Gyro = new GyroViewModel(profile.Gyro, NotifyEdited);
        ForceFeedback = new ForceFeedbackViewModel(profile.ForceFeedback, NotifyEdited);
        profile.Touchpad ??= new TouchpadConfiguration();
        profile.LeftAdaptiveTrigger ??= new AdaptiveTriggerConfiguration();
        profile.RightAdaptiveTrigger ??= new AdaptiveTriggerConfiguration();
        Touchpad = new TouchpadViewModel(profile.Touchpad, NotifyEdited);
        LeftAdaptiveTrigger = new AdaptiveTriggerViewModel(profile.LeftAdaptiveTrigger, "Gatillo adaptativo L2", NotifyEdited);
        RightAdaptiveTrigger = new AdaptiveTriggerViewModel(profile.RightAdaptiveTrigger, "Gatillo adaptativo R2", NotifyEdited);
        BaseLayer = new MappingLayerViewModel(profile.BaseLayer, isBaseLayer: true, NotifyEdited);

        ShiftLayers.Clear();
        foreach (var layer in profile.ShiftLayers)
            ShiftLayers.Add(new MappingLayerViewModel(layer, isBaseLayer: false, NotifyEdited));

        Turbos.Clear();
        foreach (var turbo in profile.TurboConfigs)
            Turbos.Add(new TurboViewModel(turbo, NotifyEdited));

        Macros.Clear();
        foreach (var macro in profile.Macros)
            Macros.Add(new MacroViewModel(macro, NotifyEdited));

        _selectedMacro = Macros.FirstOrDefault();
        _selectedLayer = BaseLayer;

        // Refresh every binding (empty name = "all properties")
        OnPropertyChanged(string.Empty);
    }

    // ---------------- Collections ----------------

    private void AddShiftLayer()
    {
        var usedModifiers = ShiftLayers.Select(l => l.ShiftModifierButton).ToHashSet();
        var modifier = EditorOptions.RealButtons.Select(o => o.Value)
            .FirstOrDefault(b => !usedModifiers.Contains(b), ControllerButton.Back);

        var layer = new MappingLayer { Name = $"Capa Shift {ShiftLayers.Count + 1}", ShiftModifierButton = modifier };
        _profile.ShiftLayers.Add(layer);
        var vm = new MappingLayerViewModel(layer, isBaseLayer: false, NotifyEdited);
        ShiftLayers.Add(vm);
        SelectedLayer = vm;
        OnPropertyChanged(nameof(AllLayers));
        NotifyEdited(nameof(ShiftLayers));
    }

    private void RemoveShiftLayer(MappingLayerViewModel? layer)
    {
        if (layer == null || layer.IsBaseLayer || !ShiftLayers.Remove(layer)) return;
        _profile.ShiftLayers.Remove(layer.Model);
        if (ReferenceEquals(SelectedLayer, layer)) SelectedLayer = BaseLayer;
        OnPropertyChanged(nameof(AllLayers));
        NotifyEdited(nameof(ShiftLayers));
    }

    private void AddTurbo()
    {
        var used = Turbos.Select(t => t.Button).ToHashSet();
        var button = EditorOptions.RealButtons.Select(o => o.Value)
            .FirstOrDefault(b => !used.Contains(b), ControllerButton.South);

        var turbo = new TurboButtonConfig { Button = button, Enabled = true, FrequencyHz = 15, DutyCycle = 0.5 };
        _profile.TurboConfigs.Add(turbo);
        Turbos.Add(new TurboViewModel(turbo, NotifyEdited));
        NotifyEdited(nameof(Turbos));
    }

    private void RemoveTurbo(TurboViewModel? turbo)
    {
        if (turbo == null || !Turbos.Remove(turbo)) return;
        _profile.TurboConfigs.Remove(turbo.Model);
        NotifyEdited(nameof(Turbos));
    }

    private void AddMacro()
    {
        var macro = new MacroDefinition
        {
            Name = $"Macro {Macros.Count + 1}",
            TriggerButton = ControllerButton.Paddle1,
            Steps = { new MacroStep { StepType = MacroStepType.TapButton, Button = ControllerButton.South, DurationMs = 50 } }
        };
        _profile.Macros.Add(macro);
        var vm = new MacroViewModel(macro, NotifyEdited);
        Macros.Add(vm);
        SelectedMacro = vm;
        NotifyEdited(nameof(Macros));
    }

    private void RemoveMacro(MacroViewModel? macro)
    {
        if (macro == null || !Macros.Remove(macro)) return;
        _profile.Macros.Remove(macro.Model);
        if (ReferenceEquals(SelectedMacro, macro)) SelectedMacro = Macros.FirstOrDefault();
        NotifyEdited(nameof(Macros));
    }

    // ---------------- Validation ----------------

    public static bool TryNormalizeHex(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value)) return false;
        string clean = value.Trim().TrimStart('#');
        if (clean.Length != 6 || !int.TryParse(clean, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _))
            return false;
        normalized = "#" + clean.ToUpperInvariant();
        return true;
    }
}
