using System.Diagnostics;
using System.Runtime.InteropServices;
using VortexPad.Core.Profiles;

namespace VortexPad.Input;

public sealed class ForegroundGameWatcher : IDisposable
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    private readonly Timer _timer;
    private string _lastProcessName = string.Empty;

    public string CurrentForegroundProcess { get; private set; } = string.Empty;
    public event Action<string, ControllerProfile?>? ForegroundProcessChanged;

    public IReadOnlyList<ControllerProfile> MonitoredProfiles { get; set; } = Array.Empty<ControllerProfile>();
    public bool AutoSwitchEnabled { get; set; } = true;

    public ForegroundGameWatcher()
    {
        _timer = new Timer(CheckForegroundWindow, null, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(1000));
    }

    private void CheckForegroundWindow(object? state)
    {
        if (!AutoSwitchEnabled) return;

        try
        {
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return;

            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) return;

            using var proc = Process.GetProcessById((int)pid);
            string procName = proc.ProcessName;
            if (string.IsNullOrWhiteSpace(procName) ||
                string.Equals(procName, _lastProcessName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _lastProcessName = procName;
            CurrentForegroundProcess = procName;

            ControllerProfile? matched = FindMatchingProfile(procName);
            ForegroundProcessChanged?.Invoke(procName, matched);
        }
        catch
        {
            // Ignore access denied for elevated system processes
        }
    }

    public ControllerProfile? FindMatchingProfile(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return null;
        string normalized = processName.Replace(".exe", "", StringComparison.OrdinalIgnoreCase).Trim();

        foreach (var profile in MonitoredProfiles)
        {
            foreach (var linked in profile.LinkedProcessNames)
            {
                string cleanLinked = linked.Replace(".exe", "", StringComparison.OrdinalIgnoreCase).Trim();
                if (!string.IsNullOrEmpty(cleanLinked) &&
                    normalized.Equals(cleanLinked, StringComparison.OrdinalIgnoreCase))
                {
                    return profile;
                }
            }
        }
        return null;
    }

    public void Dispose()
    {
        _timer.Dispose();
    }
}
