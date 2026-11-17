using VortexPad.Core.Models;
using VortexPad.Core.Profiles;

namespace VortexPad.Core.Processing;

public static class SignalProcessor
{
    /// <summary>
    /// Evaluates a normalized response curve for t in [0, 1].
    /// </summary>
    public static double EvaluateResponseCurve(
        double t,
        ResponseCurveType curveType,
        double exponent = 1.8,
        double bezierP1 = 0.25,
        double bezierP2 = 0.50)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        if (t <= 0.0) return 0.0;
        if (t >= 1.0) return 1.0;

        return curveType switch
        {
            ResponseCurveType.Linear => t,
            ResponseCurveType.Exponential => Math.Pow(t, Math.Clamp(exponent, 0.2, 5.0)),
            ResponseCurveType.SCurve => EvaluateSCurve(t, exponent),
            ResponseCurveType.CustomBezier => EvaluateCubicBezier1D(t, bezierP1, bezierP2),
            _ => t
        };
    }

    private static double EvaluateSCurve(double t, double steepness)
    {
        // Tunable logistic / power S-curve: precision around center & mid, smooth saturation at edges
        double k = Math.Clamp(steepness, 1.0, 4.0);
        double a = Math.Pow(t, k);
        double b = Math.Pow(1.0 - t, k);
        return a / (a + b);
    }

    private static double EvaluateCubicBezier1D(double t, double p1, double p2)
    {
        // P0 = 0, P1 = p1, P2 = p2, P3 = 1
        double u = 1.0 - t;
        double tt = t * t;
        double uu = u * u;
        double ttt = tt * t;
        double value = (3.0 * uu * t * p1) + (3.0 * u * tt * p2) + ttt;
        return Math.Clamp(value, 0.0, 1.0);
    }

    /// <summary>
    /// Processes a 2D analog stick vector (x, y in [-1, 1]) applying axial/radial deadzones,
    /// outer saturation, response curve, anti-deadzone, sensitivity, and inversion.
    /// </summary>
    public static (double X, double Y) ProcessStick2D(double rawX, double rawY, StickConfiguration config)
    {
        double x = Math.Clamp(rawX, -1.0, 1.0);
        double y = Math.Clamp(rawY, -1.0, 1.0);

        if (config.InvertX) x = -x;
        if (config.InvertY) y = -y;

        // 1. Axial deadzone (used in Axial or Hybrid mode)
        if (config.DeadzoneMode is DeadzoneMode.Axial or DeadzoneMode.Hybrid)
        {
            double axialDz = Math.Clamp(config.AxialDeadzone, 0.0, 0.8);
            if (config.DeadzoneMode == DeadzoneMode.Axial)
            {
                axialDz = Math.Clamp(config.InnerDeadzone, 0.0, 0.8);
            }

            x = Apply1DDeadzone(x, axialDz);
            y = Apply1DDeadzone(y, axialDz);

            if (config.DeadzoneMode == DeadzoneMode.Axial)
            {
                x = ApplyPostCurve1D(x, config);
                y = ApplyPostCurve1D(y, config);
                return (x, y);
            }
        }

        // 2. Radial deadzone in 2D polar space
        double magnitude = Math.Sqrt((x * x) + (y * y));
        double inner = Math.Clamp(config.InnerDeadzone, 0.0, 0.85);
        double outer = Math.Clamp(config.OuterDeadzone, inner + 0.05, 1.0);

        if (magnitude <= inner || magnitude < 1e-6)
        {
            return (0.0, 0.0);
        }

        double dirX = x / magnitude;
        double dirY = y / magnitude;

        // Normalize into [0, 1] between inner deadzone and outer saturation
        double normalizedMag = Math.Clamp((magnitude - inner) / (outer - inner), 0.0, 1.0);

        // 3. Apply response curve
        double curvedMag = EvaluateResponseCurve(
            normalizedMag,
            config.CurveType,
            config.CurveExponent,
            config.BezierP1,
            config.BezierP2);

        // 4. Apply Anti-Deadzone compensation if > 0
        double antiDz = Math.Clamp(config.AntiDeadzone, 0.0, 0.5);
        if (antiDz > 0.0 && curvedMag > 0.0)
        {
            curvedMag = antiDz + (curvedMag * (1.0 - antiDz));
        }

        // 5. Apply sensitivity & clamp to unit circle/square
        curvedMag = Math.Clamp(curvedMag * Math.Clamp(config.Sensitivity, 0.1, 3.0), 0.0, 1.0);

        double outX = Math.Clamp(dirX * curvedMag, -1.0, 1.0);
        double outY = Math.Clamp(dirY * curvedMag, -1.0, 1.0);
        return (outX, outY);
    }

    private static double Apply1DDeadzone(double value, double deadzone)
    {
        double abs = Math.Abs(value);
        if (abs <= deadzone) return 0.0;
        double sign = Math.Sign(value);
        return sign * ((abs - deadzone) / (1.0 - deadzone));
    }

    private static double ApplyPostCurve1D(double value, StickConfiguration config)
    {
        if (Math.Abs(value) < 1e-6) return 0.0;
        double sign = Math.Sign(value);
        double abs = Math.Clamp(Math.Abs(value) / Math.Max(0.1, config.OuterDeadzone), 0.0, 1.0);
        double curved = EvaluateResponseCurve(abs, config.CurveType, config.CurveExponent, config.BezierP1, config.BezierP2);
        double antiDz = Math.Clamp(config.AntiDeadzone, 0.0, 0.5);
        if (antiDz > 0.0)
        {
            curved = antiDz + (curved * (1.0 - antiDz));
        }
        return Math.Clamp(sign * curved * Math.Clamp(config.Sensitivity, 0.1, 3.0), -1.0, 1.0);
    }

    /// <summary>
    /// Processes an analog trigger value in [0, 1].
    /// </summary>
    public static double ProcessTrigger(double rawTrigger, TriggerConfiguration config)
    {
        double v = Math.Clamp(rawTrigger, 0.0, 1.0);
        double inner = Math.Clamp(config.InnerDeadzone, 0.0, 0.8);
        double outer = Math.Clamp(config.OuterDeadzone, inner + 0.05, 1.0);

        if (v <= inner) return 0.0;

        if (config.HairTriggerMode && v >= config.HairTriggerThreshold)
        {
            return 1.0;
        }

        double normalized = Math.Clamp((v - inner) / (outer - inner), 0.0, 1.0);
        double curved = EvaluateResponseCurve(normalized, config.CurveType, config.CurveExponent);
        return Math.Clamp(curved * Math.Clamp(config.Sensitivity, 0.1, 3.0), 0.0, 1.0);
    }
}

public sealed class GyroProcessor
{
    private double _smoothedYaw;
    private double _smoothedPitch;
    private bool _toggleActive;
    private bool _prevToggleButtonPressed;

    public void Reset()
    {
        _smoothedYaw = 0;
        _smoothedPitch = 0;
        _toggleActive = false;
        _prevToggleButtonPressed = false;
    }

    public (double OutX, double OutY, bool IsActive) Process(
        PhysicalInputState input,
        GyroConfiguration config)
    {
        if (config.OutputMode == GyroOutputMode.Disabled)
        {
            return (0.0, 0.0, false);
        }

        bool isActive = config.ActivationMode switch
        {
            GyroActivationMode.AlwaysOn => true,
            GyroActivationMode.WhileL2Held => input.L2 > 0.25,
            GyroActivationMode.WhileButtonHeld => input.IsPressed(config.ActivationButton),
            GyroActivationMode.ToggleButton => UpdateToggleState(input.IsPressed(config.ActivationButton)),
            _ => false
        };

        if (!isActive)
        {
            _smoothedYaw = 0;
            _smoothedPitch = 0;
            return (0.0, 0.0, false);
        }

        double yaw = config.InvertYaw ? -input.GyroYaw : input.GyroYaw;
        double pitch = config.InvertPitch ? -input.GyroPitch : input.GyroPitch;

        if (Math.Abs(yaw) < config.NoiseThresholdDegPerSec) yaw = 0.0;
        if (Math.Abs(pitch) < config.NoiseThresholdDegPerSec) pitch = 0.0;

        double alpha = 1.0 - Math.Clamp(config.SmoothingFactor, 0.0, 0.95);
        _smoothedYaw = (_smoothedYaw * (1.0 - alpha)) + (yaw * alpha);
        _smoothedPitch = (_smoothedPitch * (1.0 - alpha)) + (pitch * alpha);

        if (config.OutputMode == GyroOutputMode.MouseDelta)
        {
            double dx = _smoothedYaw * config.SensitivityX * 0.15;
            double dy = -_smoothedPitch * config.SensitivityY * 0.15;
            return (dx, dy, true);
        }
        else
        {
            // Normalize angular velocity (e.g. 180 deg/s full deflection) to [-1, 1] stick offset
            double rx = Math.Clamp((_smoothedYaw / 120.0) * config.SensitivityX, -1.0, 1.0);
            double ry = Math.Clamp((_smoothedPitch / 120.0) * config.SensitivityY, -1.0, 1.0);
            return (rx, ry, true);
        }
    }

    private bool UpdateToggleState(bool currentlyPressed)
    {
        if (currentlyPressed && !_prevToggleButtonPressed)
        {
            _toggleActive = !_toggleActive;
        }
        _prevToggleButtonPressed = currentlyPressed;
        return _toggleActive;
    }
}
