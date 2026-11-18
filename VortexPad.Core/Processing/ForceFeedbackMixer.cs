using System.Globalization;
using VortexPad.Core.Hid;
using VortexPad.Core.Models;
using VortexPad.Core.Profiles;

namespace VortexPad.Core.Processing;

/// <summary>
/// Combines the vibration received from the virtual controller (the game) with the profile settings
/// to produce what is sent to the physical controller.
/// </summary>
public static class ForceFeedbackMixer
{
    /// <param name="feedback">Latest vibration/LED from ViGEm, or null if the game has sent nothing yet.</param>
    /// <param name="config">Gains of the active profile.</param>
    /// <param name="profileLightbarHex">Profile color, used when the game does not control the lightbar.</param>
    /// <param name="leftTrigger">Profile adaptive effect for L2 (DualSense).</param>
    /// <param name="rightTrigger">Profile adaptive effect for R2 (DualSense).</param>
    public static SonyOutputState Compute(
        ForceFeedbackState? feedback,
        ForceFeedbackConfiguration config,
        string? profileLightbarHex,
        AdaptiveTriggerEffect leftTrigger = default,
        AdaptiveTriggerEffect rightTrigger = default)
    {
        byte large = 0, small = 0;
        if (feedback != null && config.Enabled)
        {
            double overall = Math.Clamp(config.OverallStrength, 0.0, 2.0);
            large = ToByte(feedback.LargeMotor * overall * Math.Clamp(config.LargeMotorGain, 0.0, 2.0));
            small = ToByte(feedback.SmallMotor * overall * Math.Clamp(config.SmallMotorGain, 0.0, 2.0));
        }

        byte r, g, b;
        if (feedback is { HasLightbar: true })
        {
            r = feedback.LedR;
            g = feedback.LedG;
            b = feedback.LedB;
        }
        else
        {
            ParseHexColor(profileLightbarHex, out r, out g, out b);
        }

        return new SonyOutputState(large, small, r, g, b,
            LeftTrigger: leftTrigger, RightTrigger: rightTrigger);
    }

    private static byte ToByte(double normalized) =>
        (byte)Math.Round(Math.Clamp(normalized, 0.0, 1.0) * 255.0);

    /// <summary>"#RRGGBB" or "RRGGBB". Invalid color → VortexPad cyan (#00E5FF).</summary>
    public static void ParseHexColor(string? hex, out byte r, out byte g, out byte b)
    {
        r = 0x00;
        g = 0xE5;
        b = 0xFF;

        if (string.IsNullOrWhiteSpace(hex)) return;
        ReadOnlySpan<char> clean = hex.AsSpan().Trim().TrimStart('#');
        if (clean.Length == 6 &&
            byte.TryParse(clean[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte pr) &&
            byte.TryParse(clean[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte pg) &&
            byte.TryParse(clean[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte pb))
        {
            r = pr;
            g = pg;
            b = pb;
        }
    }
}
