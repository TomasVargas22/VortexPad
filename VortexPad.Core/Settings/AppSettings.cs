using System.Text.Json;
using VortexPad.Core.Diagnostics;

namespace VortexPad.Core.Settings;

/// <summary>Application settings (not tied to a profile), persisted in <c>%LOCALAPPDATA%\VortexPad\settings.json</c>.</summary>
public sealed class AppSettings
{
    /// <summary>Last profile used; it is selected again on startup.</summary>
    public string? LastProfileId { get; set; }

    /// <summary>Last physical controller used (UniqueId); it is selected again on startup and when reconnected.</summary>
    public string? LastDeviceId { get; set; }

    /// <summary>Switch profile automatically based on the game in the foreground.</summary>
    public bool AutoSwitchEnabled { get; set; } = true;

    /// <summary>Start VortexPad when signing in to Windows (minimized to the tray).</summary>
    public bool StartWithWindows { get; set; }

    /// <summary>Minimizing the window hides it in the system tray.</summary>
    public bool MinimizeToTray { get; set; } = true;

    /// <summary>Start emulation automatically when the app opens.</summary>
    public bool StartEmulationOnLaunch { get; set; } = true;

    public AppSettings Clone() => (AppSettings)MemberwiseClone();
}

/// <summary>Reads and writes <see cref="AppSettings"/>. A missing or corrupt file yields default settings.</summary>
public sealed class AppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public AppSettingsStore(string? filePath = null)
    {
        FilePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VortexPad", "settings.json");
    }

    public string FilePath { get; }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new AppSettings();
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            VortexLog.Warn("Ajustes", "settings.json dañado; se usan los ajustes por defecto.", ex);
            return new AppSettings();
        }
    }

    /// <summary>Writes atomically (temp file + replace) so a crash mid-write cannot leave a truncated file.</summary>
    public void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            string temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(temp, FilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            VortexLog.Error("Ajustes", "No se pudieron guardar los ajustes de la app.", ex);
        }
    }
}
