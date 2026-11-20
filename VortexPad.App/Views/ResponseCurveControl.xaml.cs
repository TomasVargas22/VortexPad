using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using VortexPad.ViewModels;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using UserControl = System.Windows.Controls.UserControl;

namespace VortexPad.App.Views;

/// <summary>
/// Response curve of a stick (deadzones + curve + sensitivity), drawn at the control's size.
/// With <see cref="IsEditable"/> the two Bézier points can be dragged or moved with the arrow keys.
/// </summary>
public partial class ResponseCurveControl : UserControl
{
    private const double HandleSize = 16.0;
    private const double LiveDotSize = 10.0;

    public static readonly DependencyProperty StickProperty =
        DependencyProperty.Register(nameof(Stick), typeof(StickViewModel), typeof(ResponseCurveControl),
            new PropertyMetadata(null, OnStickChanged));

    public static readonly DependencyProperty IsEditableProperty =
        DependencyProperty.Register(nameof(IsEditable), typeof(bool), typeof(ResponseCurveControl),
            new PropertyMetadata(false, (d, _) => ((ResponseCurveControl)d).Redraw()));

    private Ellipse? _dragging;
    private double _liveRaw, _liveOut;

    public ResponseCurveControl()
    {
        InitializeComponent();
    }

    public StickViewModel? Stick
    {
        get => (StickViewModel?)GetValue(StickProperty);
        set => SetValue(StickProperty, value);
    }

    public bool IsEditable
    {
        get => (bool)GetValue(IsEditableProperty);
        set => SetValue(IsEditableProperty, value);
    }

    private double W => Math.Max(1, Plot.ActualWidth);
    private double H => Math.Max(1, Plot.ActualHeight);

    private static void OnStickChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var self = (ResponseCurveControl)d;
        if (e.OldValue is StickViewModel oldStick) oldStick.PropertyChanged -= self.OnStickPropertyChanged;
        if (e.NewValue is StickViewModel newStick) newStick.PropertyChanged += self.OnStickPropertyChanged;
        self.Redraw();
    }

    private void OnStickPropertyChanged(object? sender, PropertyChangedEventArgs e) => Redraw();

    private void Plot_SizeChanged(object sender, SizeChangedEventArgs e) => Redraw();

    private void Redraw()
    {
        double w = W, h = H;
        SetLine(GridV, new Point(w / 2, 0), new Point(w / 2, h));
        SetLine(GridH, new Point(0, h / 2), new Point(w, h / 2));
        SetLine(Diagonal, new Point(0, h), new Point(w, 0));

        var stick = Stick;
        if (stick == null)
        {
            PolyCurve.Points = new PointCollection();
            HandleP1.Visibility = HandleP2.Visibility = LineHandle1.Visibility = LineHandle2.Visibility = Visibility.Collapsed;
            return;
        }

        var points = new PointCollection();
        foreach (var (input, output) in stick.SampleResponse(80))
        {
            points.Add(new Point(input * w, (1.0 - output) * h));
        }
        PolyCurve.Points = points;

        bool showHandles = IsEditable && stick.IsBezier;
        var visibility = showHandles ? Visibility.Visible : Visibility.Collapsed;
        HandleP1.Visibility = HandleP2.Visibility = LineHandle1.Visibility = LineHandle2.Visibility = visibility;

        if (showHandles)
        {
            var p1 = new Point(stick.BezierHandleInput(1.0 / 3.0) * w, (1.0 - stick.BezierP1) * h);
            var p2 = new Point(stick.BezierHandleInput(2.0 / 3.0) * w, (1.0 - stick.BezierP2) * h);
            Place(HandleP1, p1, HandleSize);
            Place(HandleP2, p2, HandleSize);
            SetLine(LineHandle1, new Point(stick.BezierHandleInput(0.0) * w, h), p1);
            SetLine(LineHandle2, p2, new Point(stick.BezierHandleInput(1.0) * w, 0));
        }

        PlaceLiveDot();
    }

    /// <summary>Live point: raw magnitude vs. output magnitude, both in [0, 1].</summary>
    public void UpdateLiveDot(double rawMagnitude, double outputMagnitude)
    {
        _liveRaw = Math.Clamp(rawMagnitude, 0, 1);
        _liveOut = Math.Clamp(outputMagnitude, 0, 1);
        PlaceLiveDot();
    }

    private void PlaceLiveDot() => Place(DotLive, new Point(_liveRaw * W, (1.0 - _liveOut) * H), LiveDotSize);

    private static void Place(FrameworkElement element, Point center, double size)
    {
        Canvas.SetLeft(element, center.X - (size / 2));
        Canvas.SetTop(element, center.Y - (size / 2));
    }

    private static void SetLine(Line line, Point a, Point b)
    {
        line.X1 = a.X;
        line.Y1 = a.Y;
        line.X2 = b.X;
        line.Y2 = b.Y;
    }

    // ---------------- Mouse ----------------

    private void Handle_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Ellipse handle && handle.CaptureMouse())
        {
            _dragging = handle;
            handle.Focus();
            e.Handled = true;
        }
    }

    private void Handle_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragging == null || Stick == null || !ReferenceEquals(sender, _dragging)) return;
        double value = Math.Clamp(1.0 - (e.GetPosition(Plot).Y / H), 0.0, 1.0);
        SetHandleValue(_dragging, value);
    }

    private void Handle_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _dragging?.ReleaseMouseCapture();
        _dragging = null;
    }

    // ---------------- Keyboard (WCAG 2.1.1) ----------------

    private void Handle_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not Ellipse handle || Stick == null) return;
        double step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 0.1 : 0.01;
        double delta = e.Key switch
        {
            Key.Up or Key.Right => step,
            Key.Down or Key.Left => -step,
            _ => 0
        };
        if (delta == 0) return;

        double current = ReferenceEquals(handle, HandleP1) ? Stick.BezierP1 : Stick.BezierP2;
        SetHandleValue(handle, Math.Clamp(current + delta, 0.0, 1.0));
        e.Handled = true;
    }

    private void Handle_FocusChanged(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is Ellipse handle)
        {
            handle.Stroke = handle.IsKeyboardFocused
                ? (System.Windows.Media.Brush)FindResource("Brush.Accent")
                : (System.Windows.Media.Brush)FindResource("Brush.Bg");
            handle.StrokeThickness = handle.IsKeyboardFocused ? 3 : 2;
        }
    }

    private void SetHandleValue(Ellipse handle, double value)
    {
        if (Stick == null) return;
        if (ReferenceEquals(handle, HandleP1))
            Stick.BezierP1 = value;
        else
            Stick.BezierP2 = value;
        // The edit fires PropertyChanged → Redraw, and ProfileEdited → publish to the loop
        AutomationProperties.SetHelpText(handle, $"{value * 100:F0} %");
    }
}
