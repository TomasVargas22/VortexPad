using System.Text.RegularExpressions;
using VortexPad.Emulation;

namespace VortexPad.WindowsTests;

/// <summary>
/// Simulates the HidHide driver state (hidden devices, global cloaking, allowed apps) and answers with the same
/// output format that <see cref="HidHideIntegrationService"/> expects from the real CLI.
/// </summary>
internal sealed class FakeHidHideCli : IHidHideCli
{
    private static readonly Regex Quoted = new("\"([^\"]+)\"");

    public bool IsAvailable { get; set; } = true;
    public HashSet<string> Hidden { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Apps { get; } = new(StringComparer.OrdinalIgnoreCase);
    public bool CloakOn { get; set; }
    public List<string> Calls { get; } = new();

    /// <summary>If it returns a result for a command, that is used instead of simulating it (to inject failures).</summary>
    public Func<string, HidHideCliResult?>? Override { get; set; }

    public HidHideCliResult Run(string arguments)
    {
        Calls.Add(arguments);
        if (Override?.Invoke(arguments) is { } forced) return forced;

        string? value = Quoted.Match(arguments) is { Success: true } m ? m.Groups[1].Value : null;
        string command = arguments.Split(' ', 2)[0];

        switch (command)
        {
            case "--app-reg":
                Apps.Add(value!);
                return Ok();
            case "--dev-list":
                return Ok(string.Join(Environment.NewLine, Hidden.Select(h => $"--dev-hide \"{h}\"")));
            case "--dev-hide":
                Hidden.Add(value!);
                return Ok();
            case "--dev-unhide":
                Hidden.Remove(value!);
                return Ok();
            case "--cloak-state":
                return Ok(CloakOn ? "--cloak-on" : "--cloak-off");
            case "--cloak-on":
                CloakOn = true;
                return Ok();
            case "--cloak-off":
                CloakOn = false;
                return Ok();
            default:
                return new HidHideCliResult(1, $"comando desconocido: {arguments}");
        }
    }

    private static HidHideCliResult Ok(string output = "") => new(0, output);

    public int CountCalls(string prefix) => Calls.Count(c => c.StartsWith(prefix, StringComparison.Ordinal));
}
