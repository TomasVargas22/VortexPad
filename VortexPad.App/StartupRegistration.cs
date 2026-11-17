using Microsoft.Win32;
using VortexPad.Core.Diagnostics;

namespace VortexPad.App;

/// <summary>
/// "Start with Windows" for the current user only (HKCU\...\Run, no administrator needed).
/// The entry launches VortexPad with <c>--minimized</c> so it opens straight into the tray.
/// </summary>
public static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "VortexPad";

    public static bool Apply(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (enabled)
            {
                string? exe = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(exe)) return false;
                key.SetValue(ValueName, $"\"{exe}\" --minimized");
                VortexLog.Info("Inicio", "VortexPad se iniciará con Windows.");
            }
            else if (key.GetValue(ValueName) != null)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                VortexLog.Info("Inicio", "VortexPad ya no se iniciará con Windows.");
            }
            return true;
        }
        catch (Exception ex)
        {
            VortexLog.Error("Inicio", "No se pudo cambiar el inicio con Windows.", ex);
            return false;
        }
    }
}
