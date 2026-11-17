namespace VortexPad.Core.Hid;

/// <summary>
/// Identifies virtual controllers created by emulation drivers (ViGEmBus), which Windows lists as
/// one more physical controller. If VortexPad picked one as input, it would read its own output (feedback loop).
/// </summary>
public static class VirtualDeviceSignatures
{
    // ViGEmBus registers its bus with hardware IDs like "Nefarius\ViGEmBus\Gen1" (also "Root\ViGEmBus" in old versions)
    private static readonly string[] ViGEmMarkers = { "VIGEMBUS", "NEFARIUS\\VIGEM" };

    /// <summary>
    /// true if a device ID or hardware ID (of the device or one of its parents) belongs to ViGEmBus.
    /// </summary>
    public static bool IsViGEmBusIdentifier(string? identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier)) return false;
        foreach (string marker in ViGEmMarkers)
        {
            if (identifier.Contains(marker, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}
