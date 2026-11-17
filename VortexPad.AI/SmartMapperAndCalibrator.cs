using VortexPad.Core.Models;
using VortexPad.Core.Profiles;

namespace VortexPad.AI;

public enum GameGenrePreset
{
    GeneralAction,
    CompetitiveFps,
    RacingSim,
    FightingArcade,
    SoulslikeRpg
}

public sealed record DriftCalibrationResult(
    double MeasuredLeftMaxDrift,
    double MeasuredRightMaxDrift,
    double LeftNoiseStdDev,
    double RightNoiseStdDev,
    double RecommendedLeftDeadzone,
    double RecommendedRightDeadzone,
    string DiagnosticSummary);

public sealed record ControllerSignatureInfo(
    string RecognizedBrand,
    string RecognizedModel,
    string RecommendedPresetId,
    bool SupportsGyroImu,
    bool SupportsLightbar,
    string Summary);

public sealed class SmartMapperAndCalibrator
{
    private static readonly Dictionary<(int Vid, int Pid), (string Brand, string Model, string PresetId, bool HasGyro, bool HasLed)> KnownDevices = new()
    {
        [(0x054C, 0x0CE6)] = ("Sony", "DualSense Wireless Controller (PS5)", BuiltInPresets.Ps5DualSenseId, true, true),
        [(0x054C, 0x0DF2)] = ("Sony", "DualSense Edge Wireless Controller (PS5)", BuiltInPresets.Ps5DualSenseId, true, true),
        [(0x054C, 0x05C4)] = ("Sony", "DualShock 4 Controller (Gen 1)", BuiltInPresets.Ps5DualSenseId, true, true),
        [(0x054C, 0x09CC)] = ("Sony", "DualShock 4 Controller (Gen 2)", BuiltInPresets.Ps5DualSenseId, true, true),
        [(0x045E, 0x028E)] = ("Microsoft", "Xbox 360 Controller", BuiltInPresets.XboxStandardId, false, false),
        [(0x045E, 0x02EA)] = ("Microsoft", "Xbox One S Controller", BuiltInPresets.XboxStandardId, false, false),
        [(0x045E, 0x0B12)] = ("Microsoft", "Xbox Series X|S Controller", BuiltInPresets.XboxStandardId, false, false),
        [(0x057E, 0x2009)] = ("Nintendo", "Switch Pro Controller", BuiltInPresets.AiSmartProfileId, true, false),
        [(0x046D, 0xC216)] = ("Logitech", "Gamepad F310 (DirectInput)", BuiltInPresets.AiSmartProfileId, false, false),
        [(0x046D, 0xC219)] = ("Logitech", "Gamepad F710 Wireless (DirectInput)", BuiltInPresets.AiSmartProfileId, false, false),
        [(0x2DC8, 0x3106)] = ("8BitDo", "Ultimate Wireless Controller", BuiltInPresets.AiSmartProfileId, true, false)
    };

    /// <summary>
    /// Identifies a physical controller by VID/PID and product name, returning the recommended preset and capabilities.
    /// </summary>
    public ControllerSignatureInfo IdentifyController(int vendorId, int productId, string? productName)
    {
        if (KnownDevices.TryGetValue((vendorId, productId), out var known))
        {
            return new ControllerSignatureInfo(
                known.Brand,
                known.Model,
                known.PresetId,
                known.HasGyro,
                known.HasLed,
                $"Detectado {known.Brand} {known.Model} (VID: 0x{vendorId:X4}, PID: 0x{productId:X4}). Perfil recomendado: {known.PresetId}.");
        }

        string name = productName ?? string.Empty;
        if (name.Contains("DualSense", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("PS5", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("DualShock", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("PlayStation", StringComparison.OrdinalIgnoreCase) ||
            vendorId == 0x054C)
        {
            return new ControllerSignatureInfo(
                "Sony / Compatible PS",
                string.IsNullOrWhiteSpace(name) ? "PlayStation Controller" : name,
                BuiltInPresets.Ps5DualSenseId,
                true,
                true,
                "Detectado mando estilo PlayStation por firma HID/nombre. Se recomienda el perfil PS5/DualSense con Giroscopio activo.");
        }

        if (name.Contains("Xbox", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("XInput", StringComparison.OrdinalIgnoreCase) ||
            vendorId == 0x045E)
        {
            return new ControllerSignatureInfo(
                "Microsoft / Compatible Xbox",
                string.IsNullOrWhiteSpace(name) ? "Xbox Controller" : name,
                BuiltInPresets.XboxStandardId,
                false,
                false,
                "Detectado mando estilo Xbox. Se recomienda el perfil Xbox Standard.");
        }

        return new ControllerSignatureInfo(
            "DirectInput / HID Genérico",
            string.IsNullOrWhiteSpace(name) ? "Generic DirectInput Gamepad" : name,
            BuiltInPresets.AiSmartProfileId,
            false,
            false,
            "Detectado mando DirectInput/HID genérico. Se recomienda AI Smart Profile con auto-calibración de deadzone y curvas S-Curve.");
    }

    /// <summary>
    /// Analyzes resting stick samples to measure hardware center drift and noise, calculating the optimal inner deadzone.
    /// </summary>
    public DriftCalibrationResult CalibrateStickDrift(IReadOnlyList<PhysicalInputState> restingSamples)
    {
        if (restingSamples == null || restingSamples.Count == 0)
        {
            return new DriftCalibrationResult(
                0.0, 0.0, 0.0, 0.0, 0.06, 0.06,
                "Sin muestras de entrada; usando deadzone segura del 6%.");
        }

        double maxLeftRad = 0;
        double maxRightRad = 0;
        var leftRadii = new double[restingSamples.Count];
        var rightRadii = new double[restingSamples.Count];

        for (int i = 0; i < restingSamples.Count; i++)
        {
            var s = restingSamples[i];
            double lr = Math.Sqrt((s.LX * s.LX) + (s.LY * s.LY));
            double rr = Math.Sqrt((s.RX * s.RX) + (s.RY * s.RY));
            leftRadii[i] = lr;
            rightRadii[i] = rr;
            if (lr > maxLeftRad) maxLeftRad = lr;
            if (rr > maxRightRad) maxRightRad = rr;
        }

        double leftStd = ComputeStdDev(leftRadii);
        double rightStd = ComputeStdDev(rightRadii);

        // Recommended deadzone = max resting drift + 3*sigma noise + 1.5% safety margin, clamped to [0.02, 0.35]
        double recLeft = Math.Clamp(Math.Round(maxLeftRad + (3.0 * leftStd) + 0.015, 3), 0.02, 0.35);
        double recRight = Math.Clamp(Math.Round(maxRightRad + (3.0 * rightStd) + 0.015, 3), 0.02, 0.35);

        string summary =
            $"Auto-calibración completada ({restingSamples.Count} muestras): " +
            $"Drift Izq={maxLeftRad * 100:F1}% (±{leftStd * 100:F2}%) → Deadzone {recLeft * 100:F1}% | " +
            $"Drift Der={maxRightRad * 100:F1}% (±{rightStd * 100:F2}%) → Deadzone {recRight * 100:F1}%.";

        return new DriftCalibrationResult(
            Math.Round(maxLeftRad, 4),
            Math.Round(maxRightRad, 4),
            Math.Round(leftStd, 4),
            Math.Round(rightStd, 4),
            recLeft,
            recRight,
            summary);
    }

    /// <summary>
    /// Applies AI genre tuning and drift calibration to a target profile.
    /// </summary>
    public string ApplySmartTuning(
        ControllerProfile profile,
        GameGenrePreset genre,
        DriftCalibrationResult? calibration = null)
    {
        if (calibration != null)
        {
            profile.LeftStick.InnerDeadzone = calibration.RecommendedLeftDeadzone;
            profile.RightStick.InnerDeadzone = calibration.RecommendedRightDeadzone;
        }

        switch (genre)
        {
            case GameGenrePreset.CompetitiveFps:
                profile.LeftStick.CurveType = ResponseCurveType.Linear;
                profile.LeftStick.Sensitivity = 1.05;
                profile.RightStick.CurveType = ResponseCurveType.SCurve;
                profile.RightStick.CurveExponent = 1.90;
                profile.RightStick.AntiDeadzone = 0.05;
                profile.LeftTrigger.HairTriggerMode = true;
                profile.LeftTrigger.HairTriggerThreshold = 0.15;
                profile.RightTrigger.HairTriggerMode = true;
                profile.RightTrigger.HairTriggerThreshold = 0.12;
                profile.Gyro.OutputMode = GyroOutputMode.MouseDelta;
                profile.Gyro.ActivationMode = GyroActivationMode.WhileL2Held;
                profile.Gyro.SensitivityX = 3.0;
                profile.Gyro.SensitivityY = 2.4;
                return "AI Tuning aplicado: [Competitive FPS] — S-Curve (γ=1.9) en stick derecho con Anti-Deadzone 5%, Hair-Triggers instantáneos (12%) y Giroscopio a Ratón al apuntar con L2.";

            case GameGenrePreset.RacingSim:
                profile.LeftStick.DeadzoneMode = DeadzoneMode.Radial;
                profile.LeftStick.CurveType = ResponseCurveType.Exponential;
                profile.LeftStick.CurveExponent = 1.55;
                profile.LeftStick.AntiDeadzone = 0.02;
                profile.LeftTrigger.HairTriggerMode = false;
                profile.LeftTrigger.CurveType = ResponseCurveType.Exponential;
                profile.LeftTrigger.CurveExponent = 1.35;
                profile.RightTrigger.HairTriggerMode = false;
                profile.RightTrigger.CurveType = ResponseCurveType.Exponential;
                profile.RightTrigger.CurveExponent = 1.35;
                profile.Gyro.OutputMode = GyroOutputMode.Disabled;
                return "AI Tuning aplicado: [Racing / Sim] — Dirección progresiva exponencial (γ=1.55) en stick izquierdo y recorrido analógico completo en pedales L2/R2.";

            case GameGenrePreset.FightingArcade:
                profile.LeftStick.DeadzoneMode = DeadzoneMode.Hybrid;
                profile.LeftStick.InnerDeadzone = 0.10;
                profile.LeftStick.AxialDeadzone = 0.04;
                profile.LeftStick.CurveType = ResponseCurveType.Linear;
                profile.LeftTrigger.HairTriggerMode = true;
                profile.LeftTrigger.HairTriggerThreshold = 0.10;
                profile.RightTrigger.HairTriggerMode = true;
                profile.RightTrigger.HairTriggerThreshold = 0.10;
                profile.Gyro.OutputMode = GyroOutputMode.Disabled;
                return "AI Tuning aplicado: [Fighting / Arcade] — Modo Híbrido con snap axial para diagonales limpias y Hair-Triggers ultra rápidos (10%).";

            case GameGenrePreset.SoulslikeRpg:
                profile.LeftStick.CurveType = ResponseCurveType.Linear;
                profile.RightStick.CurveType = ResponseCurveType.Exponential;
                profile.RightStick.CurveExponent = 1.40;
                profile.LeftTrigger.HairTriggerMode = false;
                profile.RightTrigger.HairTriggerMode = false;
                return "AI Tuning aplicado: [Soulslike / Action RPG] — Movimiento 360° reactivo y cámara exponencial suave (γ=1.40).";

            default:
                profile.LeftStick.CurveType = ResponseCurveType.SCurve;
                profile.LeftStick.CurveExponent = 1.50;
                profile.RightStick.CurveType = ResponseCurveType.SCurve;
                profile.RightStick.CurveExponent = 1.65;
                return "AI Tuning aplicado: [General Action] — Balance óptimo S-Curve en ambos sticks con filtrado de ruido.";
        }
    }

    public GameGenrePreset InferGenreFromProcessName(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return GameGenrePreset.GeneralAction;
        string p = processName.ToLowerInvariant();

        if (p.Contains("cod") || p.Contains("valorant") || p.Contains("cs2") || p.Contains("apex") ||
            p.Contains("overwatch") || p.Contains("halo") || p.Contains("doom") || p.Contains("battlefield"))
            return GameGenrePreset.CompetitiveFps;

        if (p.Contains("forza") || p.Contains("assetto") || p.Contains("f1_") || p.Contains("dirt") ||
            p.Contains("nfs") || p.Contains("iracing") || p.Contains("beamng"))
            return GameGenrePreset.RacingSim;

        if (p.Contains("streetfighter") || p.Contains("sf6") || p.Contains("tekken") || p.Contains("mk1") ||
            p.Contains("guiltygear") || p.Contains("dbfz"))
            return GameGenrePreset.FightingArcade;

        if (p.Contains("eldenring") || p.Contains("darksouls") || p.Contains("sekiro") || p.Contains("liesofp") ||
            p.Contains("witcher") || p.Contains("monsterhunter"))
            return GameGenrePreset.SoulslikeRpg;

        return GameGenrePreset.GeneralAction;
    }

    private static double ComputeStdDev(double[] values)
    {
        if (values.Length <= 1) return 0.0;
        double mean = values.Average();
        double sumSq = 0.0;
        for (int i = 0; i < values.Length; i++)
        {
            double diff = values[i] - mean;
            sumSq += diff * diff;
        }
        return Math.Sqrt(sumSq / (values.Length - 1));
    }
}
