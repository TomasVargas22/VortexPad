namespace VortexPad.Core.Hid;

/// <summary>
/// Conversions between Windows HID device paths (what HidSharp/CreateFile use)
/// and device instance IDs (what HidHide expects in --dev-hide).
/// </summary>
public static class HidDevicePaths
{
    /// <summary>
    /// Converts a device interface path to its instance ID.
    /// <code>
    /// \\?\hid#vid_054c&amp;pid_0ce6&amp;mi_03#7&amp;2a5e1b6&amp;0&amp;0000#{4d1e55b2-f16f-11cf-88cb-001111000030}
    ///   → HID\VID_054C&amp;PID_0CE6&amp;MI_03\7&amp;2A5E1B6&amp;0&amp;0000
    /// </code>
    /// Paths that are already instance IDs (HID\...) are returned normalized.
    /// Returns null if the path does not look like a HID device.
    /// </summary>
    public static string? DevicePathToInstanceId(string? devicePath)
    {
        if (string.IsNullOrWhiteSpace(devicePath)) return null;
        string p = devicePath.Trim();

        // Already an instance ID
        if (p.StartsWith(@"HID\", StringComparison.OrdinalIgnoreCase))
        {
            return p.ToUpperInvariant();
        }

        // Prefix \\?\ or \\.\
        if (p.StartsWith(@"\\?\", StringComparison.Ordinal) || p.StartsWith(@"\\.\", StringComparison.Ordinal))
        {
            p = p[4..];
        }

        // Trailing interface class GUID: #{xxxxxxxx-...}
        int guidStart = p.LastIndexOf("#{", StringComparison.Ordinal);
        if (guidStart > 0 && p.EndsWith('}'))
        {
            p = p[..guidStart];
        }

        if (!p.StartsWith("hid#", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // Exactly 3 segments: enumerator \ hardware ID \ instance
        string[] parts = p.Split('#');
        if (parts.Length != 3 || parts.Any(string.IsNullOrEmpty))
        {
            return null;
        }

        return string.Join('\\', parts).ToUpperInvariant();
    }
}
