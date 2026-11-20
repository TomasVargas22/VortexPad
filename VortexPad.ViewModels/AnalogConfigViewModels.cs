using VortexPad.Core.Models;
using VortexPad.Core.Processing;
using VortexPad.Core.Profiles;

namespace VortexPad.ViewModels;

/// <summary>Analog stick: deadzones, curve, sensitivity and inversion.</summary>
public sealed class StickViewModel : EditableViewModel
{
    private readonly StickConfiguration _m;

    public StickViewModel(StickConfiguration model, string title, Action<string>? onEdited) : base(onEdited)
    {
        _m = model;
        Title = title;
    }

    public string Title { get; }

    /// <summary>Puts every setting of this stick back to the default values (one edit per property).</summary>
    public void ResetToDefaults()
    {
        var d = new StickConfiguration();
        DeadzoneMode = d.DeadzoneMode;
        InnerDeadzone = d.InnerDeadzone;
        OuterDeadzone = d.OuterDeadzone;
        AntiDeadzone = d.AntiDeadzone;
        AxialDeadzone = d.AxialDeadzone;
        Sensitivity = d.Sensitivity;
        InvertX = d.InvertX;
        InvertY = d.InvertY;
        CurveType = d.CurveType;
        CurveExponent = d.CurveExponent;
        BezierP1 = d.BezierP1;
        BezierP2 = d.BezierP2;
    }

    public DeadzoneMode DeadzoneMode
    {
        get => _m.DeadzoneMode;
        set { if (Edit(_m.DeadzoneMode, value, v => _m.DeadzoneMode = v)) OnPropertyChanged(nameof(UsesAxialDeadzone)); }
    }

    public bool UsesAxialDeadzone => _m.DeadzoneMode == DeadzoneMode.Hybrid;

    public double InnerDeadzone
    {
        get => _m.InnerDeadzone;
        set => EditClamped(_m.InnerDeadzone, value, 0.0, 0.85, v => _m.InnerDeadzone = v);
    }

    public double OuterDeadzone
    {
        get => _m.OuterDeadzone;
        set => EditClamped(_m.OuterDeadzone, value, 0.10, 1.0, v => _m.OuterDeadzone = v);
    }

    public double AntiDeadzone
    {
        get => _m.AntiDeadzone;
        set => EditClamped(_m.AntiDeadzone, value, 0.0, 0.5, v => _m.AntiDeadzone = v);
    }

    public double AxialDeadzone
    {
        get => _m.AxialDeadzone;
        set => EditClamped(_m.AxialDeadzone, value, 0.0, 0.8, v => _m.AxialDeadzone = v);
    }

    public double Sensitivity
    {
        get => _m.Sensitivity;
        set => EditClamped(_m.Sensitivity, value, 0.1, 3.0, v => _m.Sensitivity = v);
    }

    public bool InvertX
    {
        get => _m.InvertX;
        set => Edit(_m.InvertX, value, v => _m.InvertX = v);
    }

    public bool InvertY
    {
        get => _m.InvertY;
        set => Edit(_m.InvertY, value, v => _m.InvertY = v);
    }

    public ResponseCurveType CurveType
    {
        get => _m.CurveType;
        set
        {
            if (Edit(_m.CurveType, value, v => _m.CurveType = v))
            {
                OnPropertyChanged(nameof(IsBezier));
                OnPropertyChanged(nameof(UsesExponent));
            }
        }
    }

    public bool IsBezier => _m.CurveType == ResponseCurveType.CustomBezier;
    public bool UsesExponent => _m.CurveType is ResponseCurveType.Exponential or ResponseCurveType.SCurve;

    public double CurveExponent
    {
        get => _m.CurveExponent;
        set => EditClamped(_m.CurveExponent, value, 0.2, 5.0, v => _m.CurveExponent = v);
    }

    /// <summary>Height of Bézier control point 1 (x = 1/3), in [0, 1].</summary>
    public double BezierP1
    {
        get => _m.BezierP1;
        set => EditClamped(_m.BezierP1, value, 0.0, 1.0, v => _m.BezierP1 = v);
    }

    /// <summary>Height of Bézier control point 2 (x = 2/3), in [0, 1].</summary>
    public double BezierP2
    {
        get => _m.BezierP2;
        set => EditClamped(_m.BezierP2, value, 0.0, 1.0, v => _m.BezierP2 = v);
    }

    /// <summary>
    /// Samples the full response (deadzones + curve + anti-deadzone + sensitivity) pushing the stick to the right.
    /// Returns pairs (input, output) in [0, 1] for drawing the graph.
    /// </summary>
    public IReadOnlyList<(double Input, double Output)> SampleResponse(int steps = 60)
    {
        steps = Math.Max(2, steps);
        var points = new (double, double)[steps + 1];
        for (int i = 0; i <= steps; i++)
        {
            double x = (double)i / steps;
            var (outX, _) = SignalProcessor.ProcessStick2D(x, 0.0, _m);
            points[i] = (x, Math.Clamp(_m.InvertX ? -outX : outX, 0.0, 1.0));
        }
        return points;
    }

    /// <summary>
    /// Input position (raw x in [0, 1]) where a Bézier control point is drawn:
    /// the curve parameter t lives between the inner and outer deadzones.
    /// </summary>
    public double BezierHandleInput(double t) =>
        _m.InnerDeadzone + (t * (Math.Max(_m.OuterDeadzone, _m.InnerDeadzone + 0.05) - _m.InnerDeadzone));
}

/// <summary>Analog trigger L2 / R2.</summary>
public sealed class TriggerViewModel : EditableViewModel
{
    private readonly TriggerConfiguration _m;

    public TriggerViewModel(TriggerConfiguration model, string title, Action<string>? onEdited) : base(onEdited)
    {
        _m = model;
        Title = title;
    }

    public string Title { get; }

    public double InnerDeadzone
    {
        get => _m.InnerDeadzone;
        set => EditClamped(_m.InnerDeadzone, value, 0.0, 0.8, v => _m.InnerDeadzone = v);
    }

    public double OuterDeadzone
    {
        get => _m.OuterDeadzone;
        set => EditClamped(_m.OuterDeadzone, value, 0.1, 1.0, v => _m.OuterDeadzone = v);
    }

    public double Sensitivity
    {
        get => _m.Sensitivity;
        set => EditClamped(_m.Sensitivity, value, 0.1, 3.0, v => _m.Sensitivity = v);
    }

    public ResponseCurveType CurveType
    {
        get => _m.CurveType;
        set { if (Edit(_m.CurveType, value, v => _m.CurveType = v)) OnPropertyChanged(nameof(UsesExponent)); }
    }

    public bool UsesExponent => _m.CurveType is ResponseCurveType.Exponential or ResponseCurveType.SCurve;

    public double CurveExponent
    {
        get => _m.CurveExponent;
        set => EditClamped(_m.CurveExponent, value, 0.2, 5.0, v => _m.CurveExponent = v);
    }

    public bool HairTriggerMode
    {
        get => _m.HairTriggerMode;
        set => Edit(_m.HairTriggerMode, value, v => _m.HairTriggerMode = v);
    }

    public double HairTriggerThreshold
    {
        get => _m.HairTriggerThreshold;
        set => EditClamped(_m.HairTriggerThreshold, value, 0.01, 0.9, v => _m.HairTriggerThreshold = v);
    }
}

/// <summary>Gyroscope: output, activation, sensitivity and filtering.</summary>
public sealed class GyroViewModel : EditableViewModel
{
    private readonly GyroConfiguration _m;

    public GyroViewModel(GyroConfiguration model, Action<string>? onEdited) : base(onEdited)
    {
        _m = model;
    }

    public GyroOutputMode OutputMode
    {
        get => _m.OutputMode;
        set { if (Edit(_m.OutputMode, value, v => _m.OutputMode = v)) OnPropertyChanged(nameof(IsEnabled)); }
    }

    public bool IsEnabled => _m.OutputMode != GyroOutputMode.Disabled;

    public GyroActivationMode ActivationMode
    {
        get => _m.ActivationMode;
        set { if (Edit(_m.ActivationMode, value, v => _m.ActivationMode = v)) OnPropertyChanged(nameof(NeedsActivationButton)); }
    }

    public bool NeedsActivationButton =>
        _m.ActivationMode is GyroActivationMode.WhileButtonHeld or GyroActivationMode.ToggleButton;

    public ControllerButton ActivationButton
    {
        get => _m.ActivationButton;
        set => Edit(_m.ActivationButton, value == ControllerButton.None ? _m.ActivationButton : value,
            v => _m.ActivationButton = v);
    }

    public double SensitivityX
    {
        get => _m.SensitivityX;
        set => EditClamped(_m.SensitivityX, value, 0.1, 10.0, v => _m.SensitivityX = v);
    }

    public double SensitivityY
    {
        get => _m.SensitivityY;
        set => EditClamped(_m.SensitivityY, value, 0.1, 10.0, v => _m.SensitivityY = v);
    }

    public double NoiseThresholdDegPerSec
    {
        get => _m.NoiseThresholdDegPerSec;
        set => EditClamped(_m.NoiseThresholdDegPerSec, value, 0.0, 10.0, v => _m.NoiseThresholdDegPerSec = v);
    }

    public double SmoothingFactor
    {
        get => _m.SmoothingFactor;
        set => EditClamped(_m.SmoothingFactor, value, 0.0, 0.95, v => _m.SmoothingFactor = v);
    }

    public bool InvertYaw
    {
        get => _m.InvertYaw;
        set => Edit(_m.InvertYaw, value, v => _m.InvertYaw = v);
    }

    public bool InvertPitch
    {
        get => _m.InvertPitch;
        set => Edit(_m.InvertPitch, value, v => _m.InvertPitch = v);
    }
}

/// <summary>Vibration sent to the physical controller.</summary>
public sealed class ForceFeedbackViewModel : EditableViewModel
{
    private readonly ForceFeedbackConfiguration _m;

    public ForceFeedbackViewModel(ForceFeedbackConfiguration model, Action<string>? onEdited) : base(onEdited)
    {
        _m = model;
    }

    public bool Enabled
    {
        get => _m.Enabled;
        set => Edit(_m.Enabled, value, v => _m.Enabled = v);
    }

    public double OverallStrength
    {
        get => _m.OverallStrength;
        set => EditClamped(_m.OverallStrength, value, 0.0, 2.0, v => _m.OverallStrength = v);
    }

    public double LargeMotorGain
    {
        get => _m.LargeMotorGain;
        set => EditClamped(_m.LargeMotorGain, value, 0.0, 2.0, v => _m.LargeMotorGain = v);
    }

    public double SmallMotorGain
    {
        get => _m.SmallMotorGain;
        set => EditClamped(_m.SmallMotorGain, value, 0.0, 2.0, v => _m.SmallMotorGain = v);
    }
}
