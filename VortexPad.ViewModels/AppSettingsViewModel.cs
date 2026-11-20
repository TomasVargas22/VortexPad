using VortexPad.Core.Settings;

namespace VortexPad.ViewModels;

/// <summary>
/// Application settings panel. Every change is saved to disk right away and fires
/// <see cref="SettingChanged"/> so the window applies the side effects (Windows startup, auto-switch…).
/// </summary>
public sealed class AppSettingsViewModel : EditableViewModel
{
    private readonly AppSettingsStore _store;

    public AppSettingsViewModel(AppSettingsStore store) : base(null)
    {
        _store = store;
        Settings = store.Load();
    }

    /// <summary>Current settings (the window reads LastProfileId / LastDeviceId at startup).</summary>
    public AppSettings Settings { get; }

    /// <summary>Fired after saving a change. The argument is the property name.</summary>
    public event Action<string>? SettingChanged;

    protected override void NotifyEdited(string propertyName)
    {
        _store.Save(Settings);
        SettingChanged?.Invoke(propertyName);
    }

    public bool AutoSwitchEnabled
    {
        get => Settings.AutoSwitchEnabled;
        set => Edit(Settings.AutoSwitchEnabled, value, v => Settings.AutoSwitchEnabled = v);
    }

    public bool StartWithWindows
    {
        get => Settings.StartWithWindows;
        set => Edit(Settings.StartWithWindows, value, v => Settings.StartWithWindows = v);
    }

    public bool MinimizeToTray
    {
        get => Settings.MinimizeToTray;
        set => Edit(Settings.MinimizeToTray, value, v => Settings.MinimizeToTray = v);
    }

    public bool StartEmulationOnLaunch
    {
        get => Settings.StartEmulationOnLaunch;
        set => Edit(Settings.StartEmulationOnLaunch, value, v => Settings.StartEmulationOnLaunch = v);
    }

    /// <summary>Rail collapsed/expanded chosen with Ctrl+B (null = automatic by width).</summary>
    public bool? NavRailCollapsed
    {
        get => Settings.NavRailCollapsed;
        set => Edit(Settings.NavRailCollapsed, value, v => Settings.NavRailCollapsed = v);
    }

    /// <summary>Remembers the last profile used (saved, without firing <see cref="SettingChanged"/>).</summary>
    public void RememberProfile(string? profileId)
    {
        if (Settings.LastProfileId == profileId) return;
        Settings.LastProfileId = profileId;
        _store.Save(Settings);
    }

    /// <summary>Remembers the last physical controller used (saved, without firing <see cref="SettingChanged"/>).</summary>
    public void RememberDevice(string? deviceId)
    {
        if (Settings.LastDeviceId == deviceId) return;
        Settings.LastDeviceId = deviceId;
        _store.Save(Settings);
    }
}
