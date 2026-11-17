using System.Diagnostics;

namespace VortexPad.Emulation;

/// <summary>
/// Manages automatic integration with Nefarius HidHide to cloak the physical DirectInput/HID controller
/// while emulation is active and automatically uncloak it when stopped or on application exit.
/// </summary>
public sealed class HidHideIntegrationService : IDisposable
{
    private static readonly string[] CandidateCliPaths =
    {
        @"C:\Program Files\Nefarius Software Solutions\HidHide\x64\HidHideCLI.exe",
        @"C:\Program Files\Nefarius Software Solutions\HidHide\HidHideCLI.exe"
    };

    private readonly string? _cliPath;
    private string? _currentlyHiddenDevicePath;

    public bool IsHidHideInstalled => _cliPath != null;
    public bool IsDeviceCurrentlyCloaked { get; private set; }
    public string StatusText { get; private set; }

    public HidHideIntegrationService()
    {
        _cliPath = CandidateCliPaths.FirstOrDefault(File.Exists);
        StatusText = _cliPath != null
            ? "HidHide detectado (Auto-Cloak listo)"
            : "HidHide no instalado (Opcional: instala Nefarius.HidHide para ocultar mando físico)";
    }

    public bool HidePhysicalDevice(string? deviceInstancePath)
    {
        if (_cliPath == null || string.IsNullOrWhiteSpace(deviceInstancePath))
        {
            return false;
        }

        try
        {
            string? exePath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(exePath) && File.Exists(exePath))
            {
                RunCli($"--app-reg \"{exePath}\"");
            }

            RunCli($"--dev-hide \"{deviceInstancePath}\"");
            RunCli("--cloak-on");

            _currentlyHiddenDevicePath = deviceInstancePath;
            IsDeviceCurrentlyCloaked = true;
            StatusText = $"Mando físico oculto para evitar doble input ({deviceInstancePath})";
            return true;
        }
        catch (Exception ex)
        {
            StatusText = $"No se pudo activar HidHide: {ex.Message}";
            return false;
        }
    }

    public void RestorePhysicalDevice()
    {
        if (_cliPath == null || !IsDeviceCurrentlyCloaked)
        {
            return;
        }

        try
        {
            RunCli("--cloak-off");
            if (!string.IsNullOrWhiteSpace(_currentlyHiddenDevicePath))
            {
                RunCli($"--dev-unhide \"{_currentlyHiddenDevicePath}\"");
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
        finally
        {
            _currentlyHiddenDevicePath = null;
            IsDeviceCurrentlyCloaked = false;
            StatusText = "HidHide: Mando físico visible (restaurado)";
        }
    }

    private void RunCli(string arguments)
    {
        if (_cliPath == null) return;
        using var proc = Process.Start(new ProcessStartInfo
        {
            FileName = _cliPath,
            Arguments = arguments,
            CreateNoWindow = true,
            UseShellExecute = false
        });
        proc?.WaitForExit(1500);
    }

    public void Dispose()
    {
        RestorePhysicalDevice();
    }
}
