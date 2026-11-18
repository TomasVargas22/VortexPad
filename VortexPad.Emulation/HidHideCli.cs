using System.Diagnostics;

namespace VortexPad.Emulation;

public readonly record struct HidHideCliResult(int ExitCode, string Output);

/// <summary>
/// Runs HidHideCLI commands. Abstracted so <see cref="HidHideIntegrationService"/> can be tested without
/// HidHide installed (with a fake that simulates the driver state).
/// </summary>
public interface IHidHideCli
{
    /// <summary>true if HidHideCLI.exe was found.</summary>
    bool IsAvailable { get; }

    /// <summary>Runs one command (e.g. <c>--dev-hide "HID\..."</c>) and returns its exit code and output.</summary>
    HidHideCliResult Run(string arguments);
}

/// <summary>Real implementation: launches HidHideCLI.exe from Program Files.</summary>
public sealed class ProcessHidHideCli : IHidHideCli
{
    private const int CliTimeoutMs = 3000;
    private readonly string? _cliPath;

    public ProcessHidHideCli()
    {
        _cliPath = FindCli();
    }

    public bool IsAvailable => _cliPath != null;

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

    public HidHideCliResult Run(string arguments)
    {
        if (_cliPath == null) return new HidHideCliResult(-1, string.Empty);

        using var proc = Process.Start(new ProcessStartInfo
        {
            FileName = _cliPath,
            Arguments = arguments,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        });

        if (proc == null) return new HidHideCliResult(-1, string.Empty);

        var stdout = proc.StandardOutput.ReadToEndAsync();
        var stderr = proc.StandardError.ReadToEndAsync();

        if (!proc.WaitForExit(CliTimeoutMs))
        {
            try { proc.Kill(); } catch { /* it already ended or cannot be killed */ }
            return new HidHideCliResult(-1, "tiempo de espera agotado");
        }

        return new HidHideCliResult(proc.ExitCode, stdout.Result + stderr.Result);
    }
}
