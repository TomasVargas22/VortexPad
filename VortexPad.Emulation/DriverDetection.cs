using System.Security.Principal;
using Microsoft.Win32;
using VortexPad.Core.Diagnostics;

namespace VortexPad.Emulation;

/// <summary>Detects whether the drivers VortexPad depends on are installed, and whether the app runs as administrator.</summary>
public static class DriverDetection
{
    /// <summary>ViGEmBus registers its service under this name.</summary>
    public static bool IsViGEmBusInstalled() => ServiceExists("ViGEmBus");

    /// <summary>HidHide: driver service or CLI present.</summary>
    public static bool IsHidHideInstalled() => ServiceExists("HidHide") || new ProcessHidHideCli().IsAvailable;

    public static bool IsElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception ex)
        {
            VortexLog.Warn("Drivers", "No se pudo comprobar si la app corre como administrador.", ex);
            return false;
        }
    }

    private static bool ServiceExists(string serviceName)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{serviceName}");
            return key != null;
        }
        catch (Exception ex)
        {
            VortexLog.Warn("Drivers", $"No se pudo comprobar el servicio {serviceName}.", ex);
            return false;
        }
    }
}
