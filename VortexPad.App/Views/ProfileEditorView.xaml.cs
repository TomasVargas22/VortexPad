using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using VortexPad.Core.Models;
using VortexPad.ViewModels;
using Point = System.Windows.Point;
using UserControl = System.Windows.Controls.UserControl;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace VortexPad.App.Views;

/// <summary>
/// Full profile editor. All the logic lives in <see cref="ProfileEditorViewModel"/>; here only the
/// response curve is drawn and the Bézier control points are dragged.
/// </summary>
public partial class ProfileEditorView : UserControl
{
    private const double CanvasW = 300.0;
    private const double CanvasH = 150.0;
    private const double HandleSize = 14.0;

    private ProfileEditorViewModel? _vm;
    private Ellipse? _dragging;

    public ProfileEditorView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm != null)
        {
            _vm.ProfileEdited -= OnProfileEdited;
            _vm.PropertyChanged -= OnVmPropertyChanged;
        }

        _vm = e.NewValue as ProfileEditorViewModel;

        if (_vm != null)
        {
            _vm.ProfileEdited += OnProfileEdited;
            _vm.PropertyChanged += OnVmPropertyChanged;
        }
        RedrawCurve();
    }

    private void OnProfileEdited(string _) => RedrawCurve();

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // "" = profile reloaded; CurveStick = the user switched stick in the graph
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(ProfileEditorViewModel.CurveStick))
        {
            RedrawCurve();
        }
    }

    private void RedrawCurve()
    {
        if (_vm == null) return;
        var stick = _vm.CurveStick;

        var points = new PointCollection();
        foreach (var (input, output) in stick.SampleResponse(60))
        {
            points.Add(new Point(input * CanvasW, (1.0 - output) * CanvasH));
        }
        PolyCurve.Points = points;

        bool bezier = stick.IsBezier;
        var visibility = bezier ? Visibility.Visible : Visibility.Collapsed;
        HandleP1.Visibility = HandleP2.Visibility = LineHandle1.Visibility = LineHandle2.Visibility = visibility;

        if (bezier)
        {
            var p1 = HandlePosition(stick, 1.0 / 3.0, stick.BezierP1);
            var p2 = HandlePosition(stick, 2.0 / 3.0, stick.BezierP2);
            PlaceHandle(HandleP1, p1);
            PlaceHandle(HandleP2, p2);

            double startX = stick.BezierHandleInput(0.0) * CanvasW;
            double endX = stick.BezierHandleInput(1.0) * CanvasW;
            SetLine(LineHandle1, new Point(startX, CanvasH), p1);
            SetLine(LineHandle2, p2, new Point(endX, 0));
        }

        string curve = stick.CurveType switch
        {
            ResponseCurveType.Exponential => $"Exponencial γ={stick.CurveExponent:F2}",
            ResponseCurveType.SCurve => $"S-Curve γ={stick.CurveExponent:F2}",
            ResponseCurveType.CustomBezier => $"Bézier ({stick.BezierP1:F2}, {stick.BezierP2:F2}) — arrastra los puntos",
            _ => "Lineal"
        };
        TxtCurveSummary.Text = $"{stick.Title}: {curve} | DZ {stick.InnerDeadzone * 100:F1}% | Anti-DZ {stick.AntiDeadzone * 100:F1}%";
    }

    private static Point HandlePosition(StickViewModel stick, double t, double value) =>
        new(stick.BezierHandleInput(t) * CanvasW, (1.0 - value) * CanvasH);

    private static void PlaceHandle(FrameworkElement handle, Point center)
    {
        Canvas.SetLeft(handle, center.X - (HandleSize / 2));
        Canvas.SetTop(handle, center.Y - (HandleSize / 2));
    }

    private static void SetLine(Line line, Point a, Point b)
    {
        line.X1 = a.X;
        line.Y1 = a.Y;
        line.X2 = b.X;
        line.Y2 = b.Y;
    }

    /// <summary>Red dot that follows the stick live (raw magnitude vs. output magnitude, both in [0, 1]).</summary>
    public void UpdateLiveDot(double rawMagnitude, double outputMagnitude)
    {
        Canvas.SetLeft(DotLive, (Math.Clamp(rawMagnitude, 0, 1) * CanvasW) - 5.0);
        Canvas.SetTop(DotLive, ((1.0 - Math.Clamp(outputMagnitude, 0, 1)) * CanvasH) - 5.0);
    }

    // ---------------- Dragging the Bézier points ----------------

    private void Handle_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Ellipse handle && handle.CaptureMouse())
        {
            _dragging = handle;
            e.Handled = true;
        }
    }

    private void Handle_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragging == null || _vm == null || !ReferenceEquals(sender, _dragging)) return;

        double y = e.GetPosition(CanvasCurve).Y;
        double value = Math.Clamp(1.0 - (y / CanvasH), 0.0, 1.0);

        if (ReferenceEquals(_dragging, HandleP1))
            _vm.CurveStick.BezierP1 = value;
        else
            _vm.CurveStick.BezierP2 = value;
        // The edit fires ProfileEdited → redraw + publish to the loop
    }

    private void Handle_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _dragging?.ReleaseMouseCapture();
        _dragging = null;
    }
}
