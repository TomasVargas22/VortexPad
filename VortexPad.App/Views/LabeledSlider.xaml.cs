using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace VortexPad.App.Views;

/// <summary>Slider with a label and a formatted value (e.g. "8.0 %", "1.80", "15 Hz").</summary>
public partial class LabeledSlider : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(LabeledSlider), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(double), typeof(LabeledSlider),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnDisplayChanged));

    public static readonly DependencyProperty MinimumProperty =
        DependencyProperty.Register(nameof(Minimum), typeof(double), typeof(LabeledSlider), new PropertyMetadata(0.0));

    public static readonly DependencyProperty MaximumProperty =
        DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(LabeledSlider), new PropertyMetadata(1.0));

    public static readonly DependencyProperty StepProperty =
        DependencyProperty.Register(nameof(Step), typeof(double), typeof(LabeledSlider), new PropertyMetadata(0.01,
            // Page Up / Page Down = ten small steps (keyboard spec in especificacion.md)
            (d, e) => ((LabeledSlider)d).Part.LargeChange = (double)e.NewValue * 10));

    /// <summary>
    /// Value format: "percent" (0.08 → 8.0 %), or a .NET format string with {0} (e.g. "{0:F2}", "{0:F0} Hz").
    /// </summary>
    public static readonly DependencyProperty FormatProperty =
        DependencyProperty.Register(nameof(Format), typeof(string), typeof(LabeledSlider),
            new PropertyMetadata("{0:F2}", OnDisplayChanged));

    private static readonly DependencyPropertyKey DisplayTextKey =
        DependencyProperty.RegisterReadOnly(nameof(DisplayText), typeof(string), typeof(LabeledSlider), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty DisplayTextProperty = DisplayTextKey.DependencyProperty;

    public LabeledSlider()
    {
        InitializeComponent();
        Part.LargeChange = Step * 10;
        UpdateDisplay();
    }

    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Minimum { get => (double)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public double Step { get => (double)GetValue(StepProperty); set => SetValue(StepProperty, value); }
    public string Format { get => (string)GetValue(FormatProperty); set => SetValue(FormatProperty, value); }
    public string DisplayText => (string)GetValue(DisplayTextProperty);

    private static void OnDisplayChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((LabeledSlider)d).UpdateDisplay();

    private void UpdateDisplay()
    {
        string text = Format == "percent"
            ? string.Format(CultureInfo.CurrentCulture, "{0:F1} %", Value * 100.0)
            : string.Format(CultureInfo.CurrentCulture, Format, Value);
        SetValue(DisplayTextKey, text);
    }
}
