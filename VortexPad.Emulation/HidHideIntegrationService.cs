using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using VortexPad.Core.Diagnostics;

namespace VortexPad.Emulation;

/// <summary>
/// Integration with Nefarius HidHide: hides the physical controller while emulation is running so games
/// do not see double input, and restores it when emulation stops, the app closes, or on the next launch
/// after an unexpected shutdown.
///
/// Only undoes what VortexPad itself changed:
/// - unhides only the devices it added (not the ones the user already had hidden);
/// - turns off global cloaking only if it was off before.
/// </summary>
public sealed class HidHideIntegrationService : IDisposable
{
    private const int CliTimeoutMs = 3000;

    private static readonly Regex QuotedValue = new("\"([^\"]+)\"", RegexOptions.Compiled);

    private static readonly string SessionStatePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VortexPad",
        "hidhide-session.json");

    private readonly string? _cliPath;
    private readonly object _sync = new();
    private readonly List<string> _hiddenByUs = new();
    private bool _cloakEnabledByUs;

    public bool IsHidHideInstalled => _cliPath != null;
    public bool IsDeviceCurrentlyCloaked { get; private set; }
    public string StatusText { get; private set; }

    public HidHideIntegrationService()
    {
        _cliPath = FindCli();
        StatusText = _cliPath != null
            ? "HidHide detectado (Auto-Cloak listo)"
            : "HidHide no instalado (Opcional: instala Nefarius.HidHide para ocultar mando físico)";
    }

    private static string? FindCli()
    {
        var roots = new[]
        {
            Environment.GetEnvironmentVariable("ProgramW6432"),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            @"C:\Program Files"
        };

        foreach (string? root in roots.Where(r => !string.IsNullOrWhiteSpace(r)).Distinct())
        {
            foreach (string sub in new[] { @"x64\HidHideCLI.exe", "HidHideCLI.exe" })
            {
                string candidate = Path.Combine(root!, "Nefarius Software Solutions", "HidHide", sub);
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }

    /// <summary>
    /// If the previous session ended without restoring (crash, process killed), undoes its changes.
    /// Call once at startup, before hiding anything.
    /// </summary>
    public void RecoverPreviousSession()
    {
        lock (_sync)
        {
            if (_cliPath == null || !File.Exists(SessionStatePath)) return;

            try
            {
                var state = JsonSerializer.Deserialize<SessionState>(File.ReadAllText(SessionStatePath));
                if (state != null)
                {
                    _hiddenByUs.AddRange(state.HiddenByUs ?? new List<string>());
                    _cloakEnabledByUs = state.CloakEnabledByUs;
                    IsDeviceCurrentlyCloaked = true;
                }
            }
            catch (Exception ex)
            {
                // Corrupt file: nothing to recover
                VortexLog.Warn("HidHide", "El archivo de sesión de HidHide estaba dañado; no se pudo recuperar.", ex);
            }

            string recovered = string.Join(", ", _hiddenByUs);
            RestoreCore();
            StatusText = "HidHide: restaurado el mando que quedó oculto en la sesión anterior";
            VortexLog.Warn("HidHide", $"La sesión anterior terminó con el mando oculto; restaurado ({recovered}).");
        }
    }

    /// <summary>
    /// Hides the given HID instance IDs (HID\VID_...\...) and enables cloaking.
    /// If something was already hidden by VortexPad, restores it first (device change).
    /// </summary>
    public bool HidePhysicalDevices(IReadOnlyCollection<string> instanceIds)
    {
        lock (_sync)
        {
            if (_cliPath == null) return false;

            RestoreCore();

            if (instanceIds.Count == 0)
            {
                StatusText = "HidHide: el dispositivo seleccionado no tiene interfaz HID que ocultar";
                return false;
            }

            try
            {
                // Whitelist VortexPad itself so it can keep reading the controller once hidden
                string? exePath = Environment.ProcessPath;
                if (!string.IsNullOrWhiteSpace(exePath) && File.Exists(exePath))
                {
                    var reg = RunCli($"--app-reg \"{exePath}\"");
                    if (reg.ExitCode != 0)
                    {
                        StatusText = DescribeFailure("registrar VortexPad en la lista blanca", reg);
                        VortexLog.Error("HidHide", StatusText);
                        return false;
                    }
                }

                var alreadyHidden = ParseQuotedList(RunCli("--dev-list").Output);

                foreach (string id in instanceIds)
                {
                    if (alreadyHidden.Contains(id)) continue; // the user already had it hidden: leave it alone

                    var result = RunCli($"--dev-hide \"{id}\"");
                    if (result.ExitCode != 0)
                    {
                        StatusText = DescribeFailure($"ocultar {id}", result);
                        VortexLog.Error("HidHide", StatusText);
                        RestoreCore();
                        return false;
                    }
                    _hiddenByUs.Add(id);
                }

                bool cloakWasOn = RunCli("--cloak-state").Output.Contains("--cloak-on", StringComparison.OrdinalIgnoreCase);
                if (!cloakWasOn)
                {
                    var cloak = RunCli("--cloak-on");
                    if (cloak.ExitCode != 0)
                    {
                        StatusText = DescribeFailure("activar el ocultamiento", cloak);
                        VortexLog.Error("HidHide", StatusText);
                        RestoreCore();
                        return false;
                    }
                    _cloakEnabledByUs = true;
                }

                SaveSessionState();
                IsDeviceCurrentlyCloaked = true;
                StatusText = instanceIds.Count == 1
                    ? $"Mando físico oculto para evitar doble input ({instanceIds.First()})"
                    : $"Mando físico oculto para evitar doble input ({instanceIds.Count} interfaces HID)";
                return true;
            }
            catch (Exception ex)
            {
                StatusText = $"No se pudo activar HidHide: {ex.Message}";
                VortexLog.Error("HidHide", "Excepción al activar HidHide.", ex);
                RestoreCore();
                return false;
            }
        }
    }

    public void RestorePhysicalDevice()
    {
        lock (_sync)
        {
            if (_cliPath == null || !IsDeviceCurrentlyCloaked) return;
            RestoreCore();
            StatusText = "HidHide: Mando físico visible (restaurado)";
        }
    }

    private void RestoreCore()
    {
        if (_cliPath != null)
        {
            foreach (string id in _hiddenByUs)
            {
                try
                {
                    var r = RunCli($"--dev-unhide \"{id}\"");
                    if (r.ExitCode != 0) VortexLog.Error("HidHide", $"No se pudo volver a mostrar {id}: {r.Output.Trim()}");
                }
                catch (Exception ex)
                {
                    VortexLog.Error("HidHide", $"Excepción al volver a mostrar {id}.", ex);
                }
            }

            if (_cloakEnabledByUs)
            {
                try { RunCli("--cloak-off"); }
                catch (Exception ex) { VortexLog.Error("HidHide", "Excepción al desactivar el ocultamiento.", ex); }
            }
        }

        _hiddenByUs.Clear();
        _cloakEnabledByUs = false;
        IsDeviceCurrentlyCloaked = false;
        DeleteSessionState();
    }

    private static HashSet<string> ParseQuotedList(string output)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in QuotedValue.Matches(output))
        {
            set.Add(m.Groups[1].Value.Trim());
        }
        return set;
    }

    private static string DescribeFailure(string action, CliResult result)
    {
        string detail = string.IsNullOrWhiteSpace(result.Output) ? $"código {result.ExitCode}" : result.Output.Trim();
        return $"HidHide no pudo {action}: {detail}. ¿Ejecutaste VortexPad como administrador?";
    }

    private readonly record struct CliResult(int ExitCode, string Output);

    private CliResult RunCli(string arguments)
    {
        if (_cliPath == null) return new CliResult(-1, string.Empty);

        using var proc = Process.Start(new ProcessStartInfo
        {
            FileName = _cliPath,
            Arguments = arguments,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        });

        if (proc == null) return new CliResult(-1, string.Empty);

        var stdout = proc.StandardOutput.ReadToEndAsync();
        var stderr = proc.StandardError.ReadToEndAsync();

        if (!proc.WaitForExit(CliTimeoutMs))
        {
            try { proc.Kill(); } catch { }
            return new CliResult(-1, "tiempo de espera agotado");
        }

        return new CliResult(proc.ExitCode, stdout.Result + stderr.Result);
    }

    private sealed class SessionState
    {
        public List<string>? HiddenByUs { get; set; }
        public bool CloakEnabledByUs { get; set; }
    }

    private void SaveSessionState()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SessionStatePath)!);
            File.WriteAllText(SessionStatePath, JsonSerializer.Serialize(new SessionState
            {
                HiddenByUs = _hiddenByUs.ToList(),
                CloakEnabledByUs = _cloakEnabledByUs
            }));
        }
        catch (Exception ex)
        {
            // Without the file, crash recovery is lost, but hiding still works
            VortexLog.Warn("HidHide", "No se pudo guardar el estado de sesión (sin recuperación tras cierre inesperado).", ex);
        }
    }

    private static void DeleteSessionState()
    {
        try
        {
            if (File.Exists(SessionStatePath)) File.Delete(SessionStatePath);
        }
        catch { }
    }

    public void Dispose()
    {
        RestorePhysicalDevice();
    }
}
