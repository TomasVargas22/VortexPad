using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using VortexPad.Core.Diagnostics;
using VortexPad.Core.Hid;

namespace VortexPad.Input;

/// <summary>
/// Detects whether a HID interface belongs to a virtual controller created by ViGEmBus by walking its device tree
/// (HID → ViGEm USB child → ViGEmBus bus) with CfgMgr32 and looking for the bus's hardware ID.
/// The result is cached per path: the tree does not change while the device exists.
/// </summary>
public static class VirtualDeviceDetector
{
    private const int CR_SUCCESS = 0;
    private const int CR_BUFFER_SMALL = 0x1A;
    private const uint CM_LOCATE_DEVNODE_NORMAL = 0;
    private const uint CM_DRP_HARDWAREID = 0x02;
    private const int MaxDepth = 12;

    [DllImport("CfgMgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Locate_DevNodeW(out uint pdnDevInst, string pDeviceID, uint ulFlags);

    [DllImport("CfgMgr32.dll")]
    private static extern int CM_Get_Parent(out uint pdnDevInst, uint dnDevInst, uint ulFlags);

    [DllImport("CfgMgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Device_IDW(uint dnDevInst, char[] buffer, int bufferLen, uint ulFlags);

    [DllImport("CfgMgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_DevNode_Registry_PropertyW(
        uint dnDevInst, uint ulProperty, out uint pulRegDataType, byte[]? buffer, ref uint pulLength, uint ulFlags);

    private static readonly ConcurrentDictionary<string, bool> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// true if the HID path (\\?\hid#… or HID\…) is a ViGEm virtual controller.
    /// On any query failure it returns false (better to show a virtual controller than to hide a real one).
    /// </summary>
    public static bool IsViGEmVirtual(string? hidDevicePath)
    {
        string? instanceId = HidDevicePaths.DevicePathToInstanceId(hidDevicePath);
        if (instanceId == null) return false;
        return Cache.GetOrAdd(instanceId, Query);
    }

    private static bool Query(string instanceId)
    {
        try
        {
            if (CM_Locate_DevNodeW(out uint node, instanceId, CM_LOCATE_DEVNODE_NORMAL) != CR_SUCCESS)
            {
                return false;
            }

            for (int depth = 0; depth < MaxDepth; depth++)
            {
                if (VirtualDeviceSignatures.IsViGEmBusIdentifier(GetDeviceId(node)) ||
                    GetHardwareIds(node).Any(VirtualDeviceSignatures.IsViGEmBusIdentifier))
                {
                    return true;
                }

                if (CM_Get_Parent(out uint parent, node, 0) != CR_SUCCESS) break;
                node = parent;
            }
            return false;
        }
        catch (Exception ex)
        {
            VortexLog.Warn("HID", $"No se pudo comprobar si '{instanceId}' es un mando virtual.", ex);
            return false;
        }
    }

    private static string? GetDeviceId(uint node)
    {
        var buffer = new char[400];
        return CM_Get_Device_IDW(node, buffer, buffer.Length, 0) == CR_SUCCESS
            ? new string(buffer).TrimEnd('\0')
            : null;
    }

    private static string[] GetHardwareIds(uint node)
    {
        uint length = 0;
        int cr = CM_Get_DevNode_Registry_PropertyW(node, CM_DRP_HARDWAREID, out _, null, ref length, 0);
        if (cr != CR_BUFFER_SMALL && cr != CR_SUCCESS || length == 0) return Array.Empty<string>();

        var buffer = new byte[length];
        if (CM_Get_DevNode_Registry_PropertyW(node, CM_DRP_HARDWAREID, out _, buffer, ref length, 0) != CR_SUCCESS)
        {
            return Array.Empty<string>();
        }

        // REG_MULTI_SZ: strings separated by \0, ending in \0\0
        return Encoding.Unicode.GetString(buffer, 0, (int)length)
            .Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }
}
