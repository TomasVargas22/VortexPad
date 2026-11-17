using System.Text.Json;
using System.Text.Json.Serialization;
using VortexPad.Core.Models;

namespace VortexPad.Core.Profiles;

public static class BuiltInPresets
{
    public const string XboxStandardId = "preset-xbox-standard";
    public const string Ps5DualSenseId = "preset-ps5-dualsense";
    public const string AiSmartProfileId = "preset-ai-smart";

    public static IReadOnlyList<ControllerProfile> CreateAll() => new[]
    {
        CreateXboxStandardPreset(),
        CreatePs5DualSensePreset(),
        CreateAiSmartPreset()
    };

    /// <summary>
    /// 1. Xbox (Standard / Series) preset:
    /// Classic XInput Xbox 360 / Series X|S layout, 8% radial deadzone, linear sticks, full analog triggers, Force Feedback enabled.
    /// </summary>
    public static ControllerProfile CreateXboxStandardPreset()
    {
        return new ControllerProfile
        {
            Id = XboxStandardId,
            Name = "Xbox (Standard / Series)",
            Description = "Mapeo oficial Xbox 360 / Series X|S (XInput). Deadzone radial del 8%, curva lineal en sticks, gatillos analógicos completos y Force Feedback activado.",
            Category = "Xbox",
            IsBuiltInPreset = true,
            TargetControllerType = VirtualControllerType.Xbox360,
            LightbarHex = "#107C10",
            HidePhysicalControllerWithHidHide = true,
            PollingRateHz = 1000,
            LeftStick = new StickConfiguration
            {
                DeadzoneMode = DeadzoneMode.Radial,
                InnerDeadzone = 0.08,
                OuterDeadzone = 0.98,
                AntiDeadzone = 0.0,
                Sensitivity = 1.0,
                CurveType = ResponseCurveType.Linear
            },
            RightStick = new StickConfiguration
            {
                DeadzoneMode = DeadzoneMode.Radial,
                InnerDeadzone = 0.08,
                OuterDeadzone = 0.98,
                AntiDeadzone = 0.0,
                Sensitivity = 1.0,
                CurveType = ResponseCurveType.Linear
            },
            LeftTrigger = new TriggerConfiguration
            {
                InnerDeadzone = 0.03,
                OuterDeadzone = 0.98,
                CurveType = ResponseCurveType.Linear
            },
            RightTrigger = new TriggerConfiguration
            {
                InnerDeadzone = 0.03,
                OuterDeadzone = 0.98,
                CurveType = ResponseCurveType.Linear
            },
            Gyro = new GyroConfiguration
            {
                OutputMode = GyroOutputMode.Disabled
            },
            ForceFeedback = new ForceFeedbackConfiguration
            {
                Enabled = true,
                OverallStrength = 1.0,
                LargeMotorGain = 1.0,
                SmallMotorGain = 1.0
            }
        };
    }

    /// <summary>
    /// 2. PS5 / PlayStation (DualSense -> DS4/XInput) preset:
    /// PlayStation layout (Cross/Circle/Square/Triangle, L1/R1, L2/R2, Touchpad, PS button), Gyro -> Right Stick while L2 held, adapted vibration.
    /// </summary>
    public static ControllerProfile CreatePs5DualSensePreset()
    {
        return new ControllerProfile
        {
            Id = Ps5DualSenseId,
            Name = "PS5 / PlayStation (DualSense → DS4/XInput)",
            Description = "Mapeo nativo PlayStation (✕/○/□/△, L1/R1, L2/R2, Touchpad, botón PS), giroscopio hacia stick derecho al apuntar (L2) y vibración adaptada.",
            Category = "PlayStation",
            IsBuiltInPreset = true,
            TargetControllerType = VirtualControllerType.DualShock4,
            LightbarHex = "#003791",
            HidePhysicalControllerWithHidHide = true,
            PollingRateHz = 1000,
            LeftStick = new StickConfiguration
            {
                DeadzoneMode = DeadzoneMode.Radial,
                InnerDeadzone = 0.06,
                OuterDeadzone = 0.98,
                AntiDeadzone = 0.02,
                Sensitivity = 1.0,
                CurveType = ResponseCurveType.Linear
            },
            RightStick = new StickConfiguration
            {
                DeadzoneMode = DeadzoneMode.Radial,
                InnerDeadzone = 0.06,
                OuterDeadzone = 0.98,
                AntiDeadzone = 0.02,
                Sensitivity = 1.0,
                CurveType = ResponseCurveType.Exponential,
                CurveExponent = 1.45
            },
            LeftTrigger = new TriggerConfiguration
            {
                InnerDeadzone = 0.02,
                OuterDeadzone = 0.98,
                CurveType = ResponseCurveType.Linear
            },
            RightTrigger = new TriggerConfiguration
            {
                InnerDeadzone = 0.02,
                OuterDeadzone = 0.98,
                CurveType = ResponseCurveType.Linear
            },
            Gyro = new GyroConfiguration
            {
                OutputMode = GyroOutputMode.RightStick,
                ActivationMode = GyroActivationMode.WhileL2Held,
                SensitivityX = 2.4,
                SensitivityY = 2.0,
                NoiseThresholdDegPerSec = 0.5,
                SmoothingFactor = 0.25
            },
            ForceFeedback = new ForceFeedbackConfiguration
            {
                Enabled = true,
                OverallStrength = 0.95,
                LargeMotorGain = 0.9,
                SmallMotorGain = 1.0
            }
        };
    }

    /// <summary>
    /// 3. AI Smart Profile (Auto-Calibrado) preset:
    /// Dynamic profile with auto-drift calibration, S-Curve precision sticks, Anti-Deadzone compensation, and smart Shift Layer.
    /// </summary>
    public static ControllerProfile CreateAiSmartPreset()
    {
        var profile = new ControllerProfile
        {
            Id = AiSmartProfileId,
            Name = "AI Smart Profile (Auto-Calibrado)",
            Description = "Perfil inteligente con auto-calibración de drift/ruido, curva S-Curve de alta precisión, compensación Anti-Deadzone y ajustes adaptativos por género de juego.",
            Category = "AI Smart",
            IsBuiltInPreset = true,
            TargetControllerType = VirtualControllerType.Xbox360,
            LightbarHex = "#9D4EDD",
            HidePhysicalControllerWithHidHide = true,
            PollingRateHz = 1000,
            LeftStick = new StickConfiguration
            {
                DeadzoneMode = DeadzoneMode.Hybrid,
                InnerDeadzone = 0.05,
                AxialDeadzone = 0.02,
                OuterDeadzone = 0.96,
                AntiDeadzone = 0.04,
                Sensitivity = 1.05,
                CurveType = ResponseCurveType.SCurve,
                CurveExponent = 1.65
            },
            RightStick = new StickConfiguration
            {
                DeadzoneMode = DeadzoneMode.Radial,
                InnerDeadzone = 0.04,
                OuterDeadzone = 0.97,
                AntiDeadzone = 0.05,
                Sensitivity = 1.0,
                CurveType = ResponseCurveType.SCurve,
                CurveExponent = 1.85
            },
            LeftTrigger = new TriggerConfiguration
            {
                InnerDeadzone = 0.02,
                OuterDeadzone = 0.95,
                HairTriggerMode = true,
                HairTriggerThreshold = 0.20
            },
            RightTrigger = new TriggerConfiguration
            {
                InnerDeadzone = 0.02,
                OuterDeadzone = 0.95,
                HairTriggerMode = true,
                HairTriggerThreshold = 0.18
            },
            Gyro = new GyroConfiguration
            {
                OutputMode = GyroOutputMode.MouseDelta,
                ActivationMode = GyroActivationMode.WhileL2Held,
                SensitivityX = 2.8,
                SensitivityY = 2.2,
                NoiseThresholdDegPerSec = 0.45,
                SmoothingFactor = 0.20
            },
            ForceFeedback = new ForceFeedbackConfiguration
            {
                Enabled = true,
                OverallStrength = 1.0,
                LargeMotorGain = 1.0,
                SmallMotorGain = 1.0
            }
        };

        profile.ShiftLayers.Add(new MappingLayer
        {
            Name = "AI Precision & Media Layer (Hold Back/Share)",
            ShiftModifierButton = ControllerButton.Back,
            RightStickToMouse = true,
            StickMouseSensitivity = 12.0
        });

        profile.TurboConfigs.Add(new TurboButtonConfig
        {
            Button = ControllerButton.RightShoulder,
            Enabled = false,
            FrequencyHz = 16.0,
            DutyCycle = 0.5
        });

        return profile;
    }
}

public sealed class ProfileRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _storageDirectory;

    public ProfileRepository(string? storageDirectory = null)
    {
        _storageDirectory = storageDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VortexPad",
            "Profiles");
    }

    public static string SerializeProfile(ControllerProfile profile)
        => JsonSerializer.Serialize(profile, JsonOptions);

    public static ControllerProfile? DeserializeProfile(string json)
        => JsonSerializer.Deserialize<ControllerProfile>(json, JsonOptions);

    /// <summary>
    /// Exact deep copy (same Id and name). Used to publish an immutable snapshot of the profile
    /// to the 1000 Hz loop while the UI keeps editing the original.
    /// </summary>
    public static ControllerProfile DeepCopy(ControllerProfile profile)
        => JsonSerializer.Deserialize<ControllerProfile>(
               JsonSerializer.SerializeToUtf8Bytes(profile, JsonOptions), JsonOptions)
           ?? throw new InvalidOperationException("No se pudo copiar el perfil.");

    public IReadOnlyList<ControllerProfile> LoadAllWithBuiltIns()
    {
        var profilesById = new Dictionary<string, ControllerProfile>(StringComparer.OrdinalIgnoreCase);

        foreach (var preset in BuiltInPresets.CreateAll())
        {
            profilesById[preset.Id] = preset;
        }

        if (Directory.Exists(_storageDirectory))
        {
            foreach (var file in Directory.GetFiles(_storageDirectory, "*.json"))
            {
                try
                {
                    string json = File.ReadAllText(file);
                    var loaded = DeserializeProfile(json);
                    if (loaded != null && !string.IsNullOrWhiteSpace(loaded.Id))
                    {
                        profilesById[loaded.Id] = loaded;
                    }
                }
                catch
                {
                    // Ignore malformed user files
                }
            }
        }

        return profilesById.Values.ToList();
    }

    public void SaveProfile(ControllerProfile profile)
    {
        Directory.CreateDirectory(_storageDirectory);
        string safeFile = $"{SanitizeFileName(profile.Id)}.json";
        string fullPath = Path.Combine(_storageDirectory, safeFile);
        File.WriteAllText(fullPath, SerializeProfile(profile));
    }

    public ControllerProfile CloneProfile(ControllerProfile source, string? newName = null)
    {
        string json = SerializeProfile(source);
        var copy = DeserializeProfile(json) ?? new ControllerProfile();
        copy.Id = $"custom-{Guid.NewGuid():N}"[..16];
        copy.IsBuiltInPreset = false;
        copy.Name = newName ?? $"{source.Name} (Copia)";
        copy.Category = "Custom";
        return copy;
    }

    private static string SanitizeFileName(string id)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            id = id.Replace(c, '_');
        }
        return id;
    }
}
