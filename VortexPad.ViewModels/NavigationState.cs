using CommunityToolkit.Mvvm.ComponentModel;

namespace VortexPad.ViewModels;

/// <summary>Sections of the side navigation rail, in display order.</summary>
public enum AppSection
{
    LivePanel,
    Profiles,
    Sticks,
    Buttons,
    Gyro,
    Feedback,
    Macros,
    DualSense,
    Settings
}

/// <summary>
/// Active section of the main window and rail behavior (shortcuts Ctrl+1…5, collapsed rail below 1280 px,
/// DualSense section only when that controller is connected).
/// </summary>
public sealed class NavigationState : ObservableObject
{
    /// <summary>Below this window width the rail collapses to icons (unless the user pinned it).</summary>
    public const double CompactWidth = 1280;

    private AppSection _current = AppSection.LivePanel;
    private AppSection _lastEditorSection = AppSection.Sticks;
    private bool _dualSenseAvailable;

    public AppSection Current
    {
        get => _current;
        private set => SetProperty(ref _current, value);
    }

    /// <summary>Last editor section opened (target of Ctrl+3).</summary>
    public AppSection LastEditorSection
    {
        get => _lastEditorSection;
        private set => SetProperty(ref _lastEditorSection, value);
    }

    /// <summary>The selected physical controller is a DualSense: its section shows in the rail.</summary>
    public bool DualSenseAvailable
    {
        get => _dualSenseAvailable;
        set
        {
            if (SetProperty(ref _dualSenseAvailable, value)) OnPropertyChanged(nameof(DualSenseVisible));
        }
    }

    /// <summary>
    /// The DualSense entry is shown when the controller is connected, and also while that section is open
    /// (so it does not vanish under the user if the controller disconnects).
    /// </summary>
    public bool DualSenseVisible => _dualSenseAvailable || _current == AppSection.DualSense;

    public static bool IsEditorSection(AppSection section) =>
        section is AppSection.Sticks or AppSection.Buttons or AppSection.Gyro or AppSection.Feedback or AppSection.DualSense;

    public void Navigate(AppSection section)
    {
        if (section == AppSection.DualSense && !DualSenseVisible) return;
        if (IsEditorSection(section)) LastEditorSection = section;
        Current = section;
        OnPropertyChanged(nameof(DualSenseVisible));
    }

    /// <summary>Ctrl+1 Panel · Ctrl+2 Perfiles · Ctrl+3 last editor section · Ctrl+4 Macros · Ctrl+5 Ajustes.</summary>
    public AppSection? SectionForShortcut(int digit) => digit switch
    {
        1 => AppSection.LivePanel,
        2 => AppSection.Profiles,
        3 => LastEditorSection == AppSection.DualSense && !DualSenseVisible ? AppSection.Sticks : LastEditorSection,
        4 => AppSection.Macros,
        5 => AppSection.Settings,
        _ => null
    };

    /// <summary>The user's choice (Ctrl+B) wins; without one, the rail collapses below <see cref="CompactWidth"/>.</summary>
    public static bool ShouldCollapseRail(double windowWidth, bool? userPreference) =>
        userPreference ?? windowWidth < CompactWidth;

    /// <summary>Below <see cref="CompactWidth"/> the status chips are grouped into one summary chip.</summary>
    public static bool IsCompact(double windowWidth) => windowWidth < CompactWidth;
}
