using VortexPad.Core.Models;
using VortexPad.Core.Profiles;

namespace VortexPad.ViewModels;

/// <summary>DualSense touchpad: mouse, scroll and click zones.</summary>
public sealed class TouchpadViewModel : EditableViewModel
{
    private readonly TouchpadConfiguration _m;

    public TouchpadViewModel(TouchpadConfiguration model, Action<string>? onEdited) : base(onEdited)
    {
        _m = model;
    }

    public TouchpadMode Mode
    {
        get => _m.Mode;
        set
        {
            if (Edit(_m.Mode, value, v => _m.Mode = v))
            {
                OnPropertyChanged(nameof(IsMouseMode));
                OnPropertyChanged(nameof(UsesZones));
            }
        }
    }

    public bool IsMouseMode => _m.Mode == TouchpadMode.Mouse;

    public double MouseSensitivity
    {
        get => _m.MouseSensitivity;
        set => EditClamped(_m.MouseSensitivity, value, 0.1, 10.0, v => _m.MouseSensitivity = v);
    }

    public bool TwoFingerScroll
    {
        get => _m.TwoFingerScroll;
        set => Edit(_m.TwoFingerScroll, value, v => _m.TwoFingerScroll = v);
    }

    public double ScrollSensitivity
    {
        get => _m.ScrollSensitivity;
        set => EditClamped(_m.ScrollSensitivity, value, 0.1, 10.0, v => _m.ScrollSensitivity = v);
    }

    public bool InvertScroll
    {
        get => _m.InvertScroll;
        set => Edit(_m.InvertScroll, value, v => _m.InvertScroll = v);
    }

    public bool ClickAsMouseButtons
    {
        get => _m.ClickAsMouseButtons;
        set { if (Edit(_m.ClickAsMouseButtons, value, v => _m.ClickAsMouseButtons = v)) OnPropertyChanged(nameof(UsesZones)); }
    }

    public bool SplitClickZones
    {
        get => _m.SplitClickZones;
        set { if (Edit(_m.SplitClickZones, value, v => _m.SplitClickZones = v)) OnPropertyChanged(nameof(UsesZones)); }
    }

    /// <summary>The zone buttons apply (in mouse mode, mouse clicks take priority).</summary>
    public bool UsesZones => _m.SplitClickZones && !(IsMouseMode && _m.ClickAsMouseButtons);

    public ControllerButton LeftZoneButton
    {
        get => _m.LeftZoneButton;
        set => Edit(_m.LeftZoneButton, value, v => _m.LeftZoneButton = v);
    }

    public ControllerButton RightZoneButton
    {
        get => _m.RightZoneButton;
        set => Edit(_m.RightZoneButton, value, v => _m.RightZoneButton = v);
    }
}

/// <summary>Adaptive trigger effect for L2 or R2.</summary>
public sealed class AdaptiveTriggerViewModel : EditableViewModel
{
    private readonly AdaptiveTriggerConfiguration _m;

    public AdaptiveTriggerViewModel(AdaptiveTriggerConfiguration model, string title, Action<string>? onEdited) : base(onEdited)
    {
        _m = model;
        Title = title;
    }

    public string Title { get; }

    public AdaptiveTriggerMode Mode
    {
        get => _m.Mode;
        set
        {
            if (!Edit(_m.Mode, value, v => _m.Mode = v)) return;
            // The weapon effect needs start 2–7 and end greater than the start
            if (value == AdaptiveTriggerMode.Weapon)
            {
                StartPosition = _m.StartPosition;
                EndPosition = _m.EndPosition;
            }
            OnPropertyChanged(nameof(IsEnabled));
            OnPropertyChanged(nameof(UsesEnd));
            OnPropertyChanged(nameof(UsesFrequency));
        }
    }

    public bool IsEnabled => _m.Mode != AdaptiveTriggerMode.Off;
    public bool UsesEnd => _m.Mode == AdaptiveTriggerMode.Weapon;
    public bool UsesFrequency => _m.Mode == AdaptiveTriggerMode.Vibration;

    /// <summary>Position along the travel where the effect starts (0 = released, 9 = end).</summary>
    public double StartPosition
    {
        get => _m.StartPosition;
        set
        {
            int min = _m.Mode == AdaptiveTriggerMode.Weapon ? 2 : 0;
            int max = _m.Mode == AdaptiveTriggerMode.Weapon ? 7 : 9;
            int v = (int)Math.Round(Math.Clamp(value, min, max));
            if (Edit(_m.StartPosition, v, x => _m.StartPosition = x) && _m.EndPosition <= v)
            {
                EndPosition = v + 1;
            }
        }
    }

    public double EndPosition
    {
        get => _m.EndPosition;
        set
        {
            int v = (int)Math.Round(Math.Clamp(value, _m.StartPosition + 1, 8));
            Edit(_m.EndPosition, v, x => _m.EndPosition = x);
        }
    }

    public double Strength
    {
        get => _m.Strength;
        set => Edit(_m.Strength, (int)Math.Round(Math.Clamp(value, 1, 8)), v => _m.Strength = v);
    }

    public double FrequencyHz
    {
        get => _m.FrequencyHz;
        set => Edit(_m.FrequencyHz, (int)Math.Round(Math.Clamp(value, 1, 255)), v => _m.FrequencyHz = v);
    }
}
