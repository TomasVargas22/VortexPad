using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using VortexPad.AI;
using VortexPad.App.Views;
using VortexPad.Core.Automation;
using VortexPad.Core.Devices;
using VortexPad.Core.Diagnostics;
using VortexPad.Core.Engine;
using VortexPad.Core.Hid;
using VortexPad.Core.Models;
using VortexPad.Core.Processing;
using VortexPad.Core.Profiles;
using VortexPad.Core.Settings;
using VortexPad.Emulation;
using VortexPad.Input;
using VortexPad.ViewModels;
using Binding = System.Windows.Data.Binding;
using Brush = System.Windows.Media.Brush;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MessageBox = System.Windows.MessageBox;
using RadioButton = System.Windows.Controls.RadioButton;
using WinForms = System.Windows.Forms;

namespace VortexPad.App;

public partial class MainWindow : Window
{
    private readonly ProfileRepository _profileRepository = new();
    private readonly DirectInputAndHidProvider _inputProvider = new();
    private readonly ViGEmVirtualControllerService _vigemService = new();
    private readonly NativeKeyboardMouseInjector _kmInjector = new();
    private readonly HidHideIntegrationService _hidHideService = new();
    private readonly ForegroundGameWatcher _gameWatcher = new();
    private readonly SmartMapperAndCalibrator _aiAssistant = new();
    private readonly MacroAndTurboEngine _pipelineEngine = new();
    private readonly DriftCalibrationRecorder _driftRecorder = new(durationSeconds: 2.0);
    private CancellationTokenSource? _calibrationSamplerCts;
    private bool _calibrationUiActive;
    private readonly HighPrecisionPollingLoop _pollingLoop;
    private readonly DispatcherTimer _uiRefreshTimer;

    private List<ControllerProfile> _profiles = new();
    // _activeProfile: the copy the UI edits. _runtimeProfile: immutable snapshot that the 1000 Hz loop reads;
    // it is replaced (never mutated) on every change via PublishRuntimeProfile().
    private ControllerProfile _activeProfile;
    private volatile ControllerProfile _runtimeProfile;
    private volatile DetectedPhysicalDevice? _selectedDevice;
    private volatile PhysicalInputState _latestPhysical = new();

    // Virtual output reused frame to frame (no allocations) and published to the UI without locks
    private readonly TripleBuffer<VirtualOutputState> _virtualFrames = new(() => new VirtualOutputState());

    // Physical controller output (vibration + LED). ViGEm fires feedback on its own thread, so it reads
    // an immutable copy of the profile settings published from the UI thread.
    private sealed record FeedbackSettings(
        ForceFeedbackConfiguration Config,
        string LightbarHex,
        AdaptiveTriggerEffect LeftTrigger = default,
        AdaptiveTriggerEffect RightTrigger = default);
    private volatile FeedbackSettings _feedbackSettings = new(new ForceFeedbackConfiguration(), new ControllerProfile().LightbarHex);
    private volatile ForceFeedbackState? _lastGameFeedback;
    private bool _suppressUiEvents;
    private WinForms.NotifyIcon? _trayIcon;

    // Profile editor (MVVM): edits _activeProfile in place and notifies every change
    private readonly ProfileEditorViewModel _profileEditor = new();
    private readonly Autosaver<ControllerProfile> _autosaver;

    // App settings (settings.json) and controller hot-plug
    private readonly AppSettingsViewModel _appSettings = new(new AppSettingsStore());
    private readonly HidDeviceWatcher _deviceWatcher = new();
    private bool _refreshingDevices;
    private bool _selectedDeviceMissing;

    // External dependencies (detected at startup and with "Comprobar de nuevo")
    private bool _dependenciesDetected;
    private bool _viGEmInstalled;
    private bool _hidHideInstalled;
    private bool _isElevated;
    private readonly HashSet<string> _dismissedNotices = new();

    // Shell: navigation, status chips, transient messages
    private readonly NavigationState _navigation = new();
    private readonly StatusItem _emulationStatus = new("", "Emulación detenida");
    private readonly StatusItem _vigemStatus = new("ViGEmBus", "Comprobando…", StatusSeverity.Info);
    private readonly StatusItem _hidHideStatus = new("HidHide", "Comprobando…", StatusSeverity.Info);
    private readonly StatusItem _saveStatus = new("", "Guardado", StatusSeverity.Ok);
    private readonly StatusItem _summaryStatus = new("", "Comprobando…", StatusSeverity.Info);
    private readonly DispatcherTimer _messageTimer = new() { Interval = TimeSpan.FromSeconds(8) };
    private readonly Dictionary<RadioButton, AppSection> _navItems = new();
    private ListCollectionView? _profilesView;
    private bool _syncingProfileSelection;
    private bool _isCompact;

    // Button matrix brushes (theme resources, no literals)
    private Brush _pillOn = null!, _pillOff = null!, _pillTextOn = null!, _pillTextOff = null!;

    public MainWindow()
    {
        _suppressUiEvents = true;
        _profiles = _profileRepository.LoadAllWithBuiltIns().ToList();
        _activeProfile = _profiles.FirstOrDefault(p => p.Id == _appSettings.Settings.LastProfileId) ?? _profiles.First();
        _runtimeProfile = ProfileRepository.DeepCopy(_activeProfile);

        // Autosave (C1): 800 ms after the last edit, snapshot on the UI thread, write on the thread pool
        _autosaver = new Autosaver<ControllerProfile>(
            () => ProfileRepository.DeepCopy(_activeProfile),
            p => _profileRepository.SaveProfile(p),
            TimeSpan.FromMilliseconds(800));
        _autosaver.PropertyChanged += (_, _) => UpdateSaveStatus();

        InitializeComponent();
        InitializeShell();

        foreach (FrameworkElement editorPart in new FrameworkElement[]
                 { EditorSticks, EditorButtons, EditorGyro, EditorFeedback, MacrosEditor, EditorDualSense, PageSticks, ProfileDetail })
        {
            editorPart.DataContext = _profileEditor;
        }
        LiveCurve.SetBinding(ResponseCurveControl.StickProperty, new Binding(nameof(ProfileEditorViewModel.CurveStick)) { Source = _profileEditor });
        TxtLiveCurveTitle.SetBinding(TextBlock.TextProperty, new Binding("CurveStick.Title") { Source = _profileEditor, StringFormat = "Curva · {0}" });
        _profileEditor.ProfileEdited += OnProfileEdited;
        MacrosEditor.TestRequested += OnTestMacroRequested;

        SettingsPanel.DataContext = _appSettings;
        _appSettings.SettingChanged += OnAppSettingChanged;
        _gameWatcher.AutoSwitchEnabled = _appSettings.Settings.AutoSwitchEnabled;
        _deviceWatcher.DevicesChanged += () => Dispatcher.BeginInvoke(async () => await RefreshDevicesListAsync());
        StateChanged += OnWindowStateChanged;

        _pollingLoop = new HighPrecisionPollingLoop(OnHighPrecisionTick);
        _pollingLoop.TelemetryUpdated += OnPollingTelemetryUpdated;
        _vigemService.FeedbackReceived += OnControllerFeedbackReceived;
        _gameWatcher.ForegroundProcessChanged += OnForegroundProcessChanged;

        InitializeSystemTray();
        RefreshProfilesList();
        LoadProfileIntoUi(_activeProfile);
        UpdateEngineUi();

        _uiRefreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _uiRefreshTimer.Tick += (_, _) => UpdateLiveVisualizerUi();
        _uiRefreshTimer.Start();

        Loaded += OnMainWindowLoaded;
        Closed += OnMainWindowClosed;
    }

    // ---------------- Shell: navigation, chips, layout ----------------

    private void InitializeShell()
    {
        _pillOn = (Brush)FindResource("Brush.Accent");
        _pillOff = (Brush)FindResource("Brush.Surface2");
        _pillTextOn = (Brush)FindResource("Brush.AccentInk");
        _pillTextOff = (Brush)FindResource("Brush.Text");

        _navItems[NavLive] = AppSection.LivePanel;
        _navItems[NavProfiles] = AppSection.Profiles;
        _navItems[NavSticks] = AppSection.Sticks;
        _navItems[NavButtons] = AppSection.Buttons;
        _navItems[NavGyro] = AppSection.Gyro;
        _navItems[NavFeedback] = AppSection.Feedback;
        _navItems[NavMacros] = AppSection.Macros;
        _navItems[NavDualSense] = AppSection.DualSense;
        _navItems[NavSettings] = AppSection.Settings;

        var chips = new[] { _vigemStatus, _hidHideStatus, _saveStatus };
        ChipEmulation.Content = _emulationStatus;
        ChipsPanel.ItemsSource = chips;
        ChipsPopupList.ItemsSource = chips;
        ChipSummaryContent.Content = _summaryStatus;
        SettingsVigemChip.Content = _vigemStatus;
        SettingsHidHideChip.Content = _hidHideStatus;
        foreach (var chip in chips) chip.PropertyChanged += (_, _) => UpdateSummaryStatus();

        _messageTimer.Tick += (_, _) => HideMessage();
        _navigation.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(NavigationState.DualSenseVisible))
                NavDualSense.Visibility = _navigation.DualSenseVisible ? Visibility.Visible : Visibility.Collapsed;
        };

        TxtProfilesFolder.Text = _profileRepository.StorageDirectory;
        string version = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        TxtVersion.Text = $"VortexPad {version}";
    }

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton item && _navItems.TryGetValue(item, out var section)) Navigate(section);
    }

    private void Navigate(AppSection section)
    {
        _navigation.Navigate(section);
        var current = _navigation.Current;

        foreach (var (item, itemSection) in _navItems) item.IsChecked = itemSection == current;

        var target = SectionElement(current);
        foreach (var page in new[] { SecLive, SecProfiles, SecSticks, SecButtons, SecGyro, SecFeedback, SecMacros, SecDualSense, SecSettings })
        {
            page.Visibility = ReferenceEquals(page, target) ? Visibility.Visible : Visibility.Collapsed;
        }
        FadeIn(target);
        Dispatcher.BeginInvoke(() => AccessibleNames.Apply(this), DispatcherPriority.Loaded);
    }

    private ScrollViewer SectionElement(AppSection section) => section switch
    {
        AppSection.Profiles => SecProfiles,
        AppSection.Sticks => SecSticks,
        AppSection.Buttons => SecButtons,
        AppSection.Gyro => SecGyro,
        AppSection.Feedback => SecFeedback,
        AppSection.Macros => SecMacros,
        AppSection.DualSense => SecDualSense,
        AppSection.Settings => SecSettings,
        _ => SecLive
    };

    /// <summary>Section change: 180 ms fade + 8 px rise, only if Windows has animations enabled.</summary>
    private static void FadeIn(UIElement element)
    {
        if (!SystemParameters.ClientAreaAnimation)
        {
            element.Opacity = 1;
            return;
        }

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = new Duration(TimeSpan.FromMilliseconds(180));
        var move = new TranslateTransform(0, 8);
        element.RenderTransform = move;
        element.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
        move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(8, 0, duration) { EasingFunction = ease });
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e) => ApplyLayoutMode();

    /// <summary>Below 1280 px: collapsed rail (unless pinned with Ctrl+B), grouped chips, short labels.</summary>
    private void ApplyLayoutMode()
    {
        double width = ActualWidth;
        bool collapse = NavigationState.ShouldCollapseRail(width, _appSettings.NavRailCollapsed);
        _isCompact = NavigationState.IsCompact(width);

        Rail.Width = (double)FindResource(collapse ? "Size.NavCollapsedW" : "Size.NavW");
        Rail.Padding = collapse ? new Thickness(10, 12, 10, 12) : new Thickness(12);
        string mode = collapse ? "Collapsed" : "Expanded";
        NavItems.Tag = mode;
        NavBottom.Tag = mode;
        TxtBrand.Visibility = collapse ? Visibility.Collapsed : Visibility.Visible;
        GroupEditor.Visibility = collapse ? Visibility.Collapsed : Visibility.Visible;
        BtnToggleRail.Content = collapse ? "" : "";
        string toggleText = collapse ? "Expandir el menú" : "Contraer el menú";
        BtnToggleRail.ToolTip = $"{toggleText} · Ctrl+B";
        AutomationProperties.SetName(BtnToggleRail, toggleText);

        ApplyChipsFit();
        StatusBar.Padding = new Thickness(_isCompact ? 16 : 24, 0, _isCompact ? 16 : 24, 0);
        CmbPhysicalDevices.Width = _isCompact ? 200 : 240;
        CmbProfileQuick.Width = _isCompact ? 180 : 220;
        UpdateEngineUi();
    }

    /// <summary>
    /// Shows the chips one by one if they fit in the status bar; otherwise (always below 1280 px) groups them
    /// into the summary chip, so no chip is ever cut in half.
    /// </summary>
    private void ApplyChipsFit()
    {
        bool grouped = _isCompact;
        double available = StatusGrid.ColumnDefinitions[1].ActualWidth;
        if (!grouped && available > 0)
        {
            // A collapsed element measures 0: make it visible first so its real width is known
            ChipsPanel.Visibility = Visibility.Visible;
            var unbounded = new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity);
            ChipEmulation.Measure(unbounded);
            ChipsPanel.Measure(unbounded);
            grouped = ChipEmulation.DesiredSize.Width + ChipsPanel.DesiredSize.Width > available;
        }
        ChipsPanel.Visibility = grouped ? Visibility.Collapsed : Visibility.Visible;
        ChipSummary.Visibility = grouped ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BtnToggleRail_Click(object sender, RoutedEventArgs e) => ToggleRail();

    private void ToggleRail()
    {
        bool collapsedNow = NavigationState.ShouldCollapseRail(ActualWidth, _appSettings.NavRailCollapsed);
        _appSettings.NavRailCollapsed = !collapsedNow;
        ApplyLayoutMode();
    }

    /// <summary>F5, Ctrl+1…5, Ctrl+Tab / Ctrl+Mayús+Tab, Ctrl+B.</summary>
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

        if (e.Key == Key.F5 && Keyboard.Modifiers == ModifierKeys.None)
        {
            if (BtnToggleEngine.IsEnabled) BtnToggleEngine_Click(BtnToggleEngine, new RoutedEventArgs());
            e.Handled = true;
            return;
        }
        if (!ctrl) return;

        int digit = e.Key switch
        {
            >= Key.D1 and <= Key.D5 => e.Key - Key.D0,
            >= Key.NumPad1 and <= Key.NumPad5 => e.Key - Key.NumPad0,
            _ => 0
        };
        if (digit > 0 && _navigation.SectionForShortcut(digit) is { } section)
        {
            Navigate(section);
            e.Handled = true;
        }
        else if (e.Key == Key.Tab)
        {
            CycleProfile(shift ? -1 : 1);
            e.Handled = true;
        }
        else if (e.Key == Key.B)
        {
            ToggleRail();
            e.Handled = true;
        }
    }

    private void CycleProfile(int direction)
    {
        var ordered = _profilesView?.Cast<ControllerProfile>().ToList() ?? _profiles;
        if (ordered.Count == 0) return;
        int index = ordered.IndexOf(_activeProfile);
        int next = ((index < 0 ? 0 : index) + direction + ordered.Count) % ordered.Count;
        ActivateProfile(ordered[next]);
        ShowMessage($"Perfil activo: {_activeProfile.Name}.", StatusSeverity.Info);
    }

    private void UpdateSummaryStatus()
    {
        var worst = StatusItem.Worst(new[] { _vigemStatus, _hidHideStatus, _saveStatus });
        if (worst == null || worst.Severity <= StatusSeverity.Ok)
            _summaryStatus.Set("Todo listo", StatusSeverity.Ok, "ViGEmBus, HidHide y guardado sin problemas");
        else
            _summaryStatus.Set(worst.AccessibleText, worst.Severity, "Pulsa para ver el estado de los controladores");

        // Chip texts changed width: re-check whether they fit, once layout has updated
        Dispatcher.BeginInvoke(ApplyChipsFit, DispatcherPriority.Loaded);
    }

    private void UpdateSaveStatus()
    {
        switch (_autosaver.Status)
        {
            case SaveStatus.Pending:
            case SaveStatus.Saving:
                _saveStatus.Set("Guardando…", StatusSeverity.Info);
                break;
            case SaveStatus.Failed:
                _saveStatus.Set("No se pudo guardar", StatusSeverity.Error, _autosaver.LastError);
                VortexLog.Error("Perfiles", $"No se pudo guardar '{_activeProfile.Name}': {_autosaver.LastError}");
                break;
            default:
                _saveStatus.Set("Guardado", StatusSeverity.Ok, "Los cambios del perfil se guardan solos");
                break;
        }
    }

    private void UpdateDriverStatus()
    {
        if (!_dependenciesDetected)
        {
            _vigemStatus.Set("Comprobando…", StatusSeverity.Info);
            _hidHideStatus.Set("Comprobando…", StatusSeverity.Info);
            return;
        }

        if (!_viGEmInstalled)
            _vigemStatus.Set("No instalado", StatusSeverity.Error, "Sin ViGEmBus no se puede crear el mando virtual");
        else if (_pollingLoop.IsRunning && !_vigemService.IsControllerConnected && NeedsViGEm(_activeProfile))
            _vigemStatus.Set(_vigemService.StatusMessage, StatusSeverity.Error);
        else if (_pollingLoop.IsRunning && NeedsViGEm(_activeProfile))
            _vigemStatus.Set("Conectado", StatusSeverity.Ok, _vigemService.StatusMessage);
        else
            _vigemStatus.Set("Listo", StatusSeverity.Ok, "Instalado y listo para crear el mando virtual");

        bool wantsHide = _activeProfile.HidePhysicalControllerWithHidHide;
        if (!_hidHideInstalled)
            _hidHideStatus.Set("No instalado", wantsHide ? StatusSeverity.Warning : StatusSeverity.Neutral,
                "Opcional: sin HidHide algunos juegos ven el mando físico y el virtual a la vez");
        else if (wantsHide && !_isElevated)
            _hidHideStatus.Set("Requiere permisos", StatusSeverity.Warning, "Reinicia VortexPad como administrador para ocultar el mando");
        else if (!wantsHide)
            _hidHideStatus.Set("Sin ocultar", StatusSeverity.Neutral, "Este perfil no oculta el mando físico");
        else
            _hidHideStatus.Set(_hidHideService.IsDeviceCurrentlyCloaked ? "Mando oculto" : "Listo", StatusSeverity.Ok, _hidHideService.StatusText);

        BtnRestartAdmin.Visibility = _hidHideInstalled && wantsHide && !_isElevated ? Visibility.Visible : Visibility.Collapsed;
        UpdateEngineUi();
    }

    private static bool NeedsViGEm(ControllerProfile profile) => profile.TargetControllerType != VirtualControllerType.KeyboardMouse;

    private static string TargetLabel(VirtualControllerType type) => type switch
    {
        VirtualControllerType.DualShock4 => "DualShock 4",
        VirtualControllerType.KeyboardMouse => "Teclado y ratón",
        VirtualControllerType.HybridXboxKeyboard => "Xbox + teclado",
        _ => "Xbox 360"
    };

    /// <summary>Start/stop button and emulation chip (label, icon, enabled state with its reason).</summary>
    private void UpdateEngineUi()
    {
        bool running = _pollingLoop.IsRunning;
        TxtEngineIcon.Text = running ? "" : "";
        TxtEngineLabel.Text = running
            ? (_isCompact ? "Detener" : "Detener emulación")
            : (_isCompact ? "Iniciar" : "Iniciar emulación");

        bool blocked = !running && _dependenciesDetected && !_viGEmInstalled && NeedsViGEm(_activeProfile);
        BtnToggleEngine.IsEnabled = !blocked;
        AutomationProperties.SetName(BtnToggleEngine, running ? "Detener emulación" : "Iniciar emulación");
        BtnToggleEngine.ToolTip = blocked
            ? "Instala ViGEmBus para crear el mando virtual"
            : running ? "Detener emulación · F5" : "Iniciar emulación · F5";

        if (running)
            _emulationStatus.Set($"Emulando · {TargetLabel(_activeProfile.TargetControllerType)}", StatusSeverity.Live);
        else
            _emulationStatus.Set("Emulación detenida", StatusSeverity.Neutral);
    }

    // ---------------- Transient messages (replace the old footer) ----------------

    private void ShowMessage(string text, StatusSeverity severity)
    {
        TxtFooterStatus.Text = text;
        var (brushKey, softKey, glyph) = severity switch
        {
            StatusSeverity.Error => ("Brush.Danger", "Brush.DangerSoft", ""),
            StatusSeverity.Warning => ("Brush.Warn", "Brush.WarnSoft", ""),
            StatusSeverity.Ok => ("Brush.Ok", "Brush.OkSoft", ""),
            _ => ("Brush.Info", "Brush.InfoSoft", "")
        };
        MessageBar.Background = (Brush)FindResource(softKey);
        MessageBar.BorderBrush = (Brush)FindResource(brushKey);
        TxtMessageIcon.Foreground = (Brush)FindResource(brushKey);
        TxtMessageIcon.Text = glyph;
        AutomationProperties.SetLiveSetting(MessageBar, severity == StatusSeverity.Error
            ? System.Windows.Automation.AutomationLiveSetting.Assertive
            : System.Windows.Automation.AutomationLiveSetting.Polite);
        MessageBar.Visibility = Visibility.Visible;
        UpdateNoticesArea();
        FadeIn(MessageBar);

        // Errors and warnings stay until closed; the rest goes away by itself
        _messageTimer.Stop();
        if (severity is not (StatusSeverity.Error or StatusSeverity.Warning)) _messageTimer.Start();
    }

    private void HideMessage()
    {
        _messageTimer.Stop();
        MessageBar.Visibility = Visibility.Collapsed;
        UpdateNoticesArea();
    }

    /// <summary>The notices area takes no space when there is nothing to say.</summary>
    private void UpdateNoticesArea()
    {
        bool any = MessageBar.Visibility == Visibility.Visible || NoticesBar.Items.Count > 0;
        NoticesArea.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
    }

    private void MessageDismiss_Click(object sender, RoutedEventArgs e) => HideMessage();

    // ---------------- Startup ----------------

    private async void OnMainWindowLoaded(object sender, RoutedEventArgs e)
    {
        ApplyLayoutMode();
        AccessibleNames.Apply(this);

        // If the previous session closed unexpectedly with the controller hidden, make it visible again
        await Task.Run(_hidHideService.RecoverPreviousSession);

        await DetectDependenciesAsync();

        await RefreshDevicesListAsync();
        _deviceWatcher.Start();

        // Keeps the Run entry pointing at the current exe (in case the app was moved)
        if (_appSettings.StartWithWindows) StartupRegistration.Apply(true);

        if (_appSettings.StartEmulationOnLaunch && (_viGEmInstalled || !NeedsViGEm(_activeProfile)))
        {
            await StartEmulationEngineAsync();
        }

        if (App.StartMinimized && _trayIcon != null)
        {
            WindowState = WindowState.Minimized; // OnWindowStateChanged hides it in the tray
        }
    }

    /// <summary>Required/optional drivers and permissions. Also run by "Comprobar de nuevo" after installing one.</summary>
    private async Task DetectDependenciesAsync()
    {
        await Task.Run(() =>
        {
            _viGEmInstalled = DriverDetection.IsViGEmBusInstalled();
            _hidHideInstalled = DriverDetection.IsHidHideInstalled();
            _isElevated = DriverDetection.IsElevated();
        });
        VortexLog.Info("Drivers", $"ViGEmBus={(_viGEmInstalled ? "sí" : "no")}, HidHide={(_hidHideInstalled ? "sí" : "no")}, administrador={(_isElevated ? "sí" : "no")}.");
        _dependenciesDetected = true;
        RefreshNotices();
        UpdateDriverStatus();
    }

    // ---------------- Notices: drivers, permissions, disconnected controller ----------------

    /// <summary>Re-evaluates the notices (missing drivers, administrator needed for HidHide, controller unplugged).</summary>
    private void RefreshNotices()
    {
        var notices = new List<DependencyNotice>();

        if (_selectedDeviceMissing && _selectedDevice != null)
        {
            notices.Add(new DependencyNotice("device-disconnected", NoticeSeverity.Warning,
                $"Se desconectó {_selectedDevice.DisplayName}. El mando virtual sigue activo y en reposo; vuelve a conectarlo y la emulación continúa sola."));
        }

        if (_dependenciesDetected)
        {
            notices.AddRange(DependencyNotices.Evaluate(new DependencyState(
                ViGEmBusInstalled: _viGEmInstalled,
                HidHideInstalled: _hidHideInstalled,
                IsElevated: _isElevated,
                ProfileWantsHidHide: _activeProfile.HidePhysicalControllerWithHidHide,
                ProfileNeedsViGEm: NeedsViGEm(_activeProfile))));
        }

        NoticesBar.ItemsSource = notices.Where(n => !_dismissedNotices.Contains(n.Id)).ToList();
        UpdateNoticesArea();
    }

    private void NoticeDismiss_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: DependencyNotice notice })
        {
            _dismissedNotices.Add(notice.Id); // only for this session: it shows again on the next launch
            RefreshNotices();
        }
    }

    private async void NoticeRecheck_Click(object sender, RoutedEventArgs e)
    {
        bool hadViGEm = _viGEmInstalled, hadHidHide = _hidHideInstalled;
        await DetectDependenciesAsync();

        if (_viGEmInstalled && !hadViGEm || _hidHideInstalled && !hadHidHide)
            ShowMessage("Controlador detectado. Ya puedes iniciar la emulación.", StatusSeverity.Ok);
        else
            ShowMessage("Todavía falta el controlador. Si acabas de instalarlo y sigue sin aparecer, reinicia Windows.", StatusSeverity.Warning);
    }

    private void NoticeAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: DependencyNotice notice }) return;

        switch (notice.Action)
        {
            case NoticeAction.OpenUrl when notice.Url != null:
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(notice.Url) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    VortexLog.Warn("UI", $"No se pudo abrir {notice.Url}.", ex);
                    ShowMessage($"No se pudo abrir el navegador. Abre a mano: {notice.Url}", StatusSeverity.Warning);
                }
                break;

            case NoticeAction.RestartElevated:
                RestartAsAdministrator();
                break;
        }
    }

    private void BtnRestartAdmin_Click(object sender, RoutedEventArgs e) => RestartAsAdministrator();

    /// <summary>
    /// Relaunches VortexPad with UAC (only when HidHide needs it). If the user cancels the prompt,
    /// it keeps running without elevation.
    /// </summary>
    private void RestartAsAdministrator()
    {
        string? exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return;

        try
        {
            _autosaver.Flush();
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe)
            {
                UseShellExecute = true,
                Verb = "runas"
            });
            VortexLog.Info("App", "Reiniciando como administrador para usar HidHide.");
            System.Windows.Application.Current.Shutdown(); // Closed restores HidHide and releases everything
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            ShowMessage("Se canceló el permiso de administrador. VortexPad sigue funcionando sin ocultar el mando.", StatusSeverity.Info);
        }
        catch (Exception ex)
        {
            VortexLog.Error("App", "No se pudo reiniciar como administrador.", ex);
            ShowMessage("No se pudo reiniciar como administrador. Más detalles en el registro.", StatusSeverity.Error);
        }
    }

    // ---------------- App settings ----------------

    private void OnAppSettingChanged(string name)
    {
        switch (name)
        {
            case nameof(AppSettingsViewModel.AutoSwitchEnabled):
                _gameWatcher.AutoSwitchEnabled = _appSettings.AutoSwitchEnabled;
                break;
            case nameof(AppSettingsViewModel.StartWithWindows):
                if (!StartupRegistration.Apply(_appSettings.StartWithWindows))
                {
                    ShowMessage("No se pudo cambiar el inicio con Windows. Más detalles en el registro.", StatusSeverity.Error);
                }
                break;
        }
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized && _appSettings.MinimizeToTray && _trayIcon != null)
        {
            Hide(); // emulation keeps running; restore from the tray icon
        }
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void BtnOpenLogs_Click(object sender, RoutedEventArgs e) => OpenFolder(RotatingFileLogSink.DefaultDirectory, "registro");

    private void BtnOpenProfilesFolder_Click(object sender, RoutedEventArgs e) => OpenFolder(_profileRepository.StorageDirectory, "perfiles");

    private void OpenFolder(string path, string what)
    {
        try
        {
            System.IO.Directory.CreateDirectory(path);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            VortexLog.Warn("UI", $"No se pudo abrir la carpeta de {what}.", ex);
            ShowMessage($"No se pudo abrir la carpeta: {path}", StatusSeverity.Warning);
        }
    }

    /// <summary>
    /// Hides (or restores) the selected physical controller according to the active profile.
    /// Used when starting emulation and when the device or the setting changes while emulation is running.
    /// </summary>
    private async Task ApplyHidHideAsync()
    {
        bool hide = _activeProfile.HidePhysicalControllerWithHidHide;
        var instanceIds = _inputProvider.ResolveHidInstanceIds(_selectedDevice);

        await Task.Run(() =>
        {
            if (hide)
                _hidHideService.HidePhysicalDevices(instanceIds);
            else
                _hidHideService.RestorePhysicalDevice();
        });

        UpdateDriverStatus();
    }

    /// <summary>VortexPad icon from the exe (also works when published as a single file).</summary>
    private static System.Drawing.Icon LoadAppIcon()
    {
        try
        {
            string? exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe))
            {
                var icon = System.Drawing.Icon.ExtractAssociatedIcon(exe);
                if (icon != null) return icon;
            }
        }
        catch (Exception ex)
        {
            VortexLog.Warn("UI", "No se pudo cargar el icono de la app; se usa el genérico.", ex);
        }
        return System.Drawing.SystemIcons.Application;
    }

    private void InitializeSystemTray()
    {
        try
        {
            _trayIcon = new WinForms.NotifyIcon
            {
                Text = "VortexPad",
                Icon = LoadAppIcon(),
                Visible = true
            };

            var menu = new WinForms.ContextMenuStrip();
            menu.Items.Add("Mostrar VortexPad", null, (_, _) => Dispatcher.Invoke(ShowFromTray));
            menu.Items.Add("Iniciar o detener la emulación", null, (_, _) => Dispatcher.Invoke(() => BtnToggleEngine_Click(BtnToggleEngine, new RoutedEventArgs())));
            menu.Items.Add("-");
            foreach (var presetId in new[] { BuiltInPresets.XboxStandardId, BuiltInPresets.Ps5DualSenseId, BuiltInPresets.AiSmartProfileId })
            {
                string name = _profiles.FirstOrDefault(p => p.Id == presetId)?.Name ?? presetId;
                menu.Items.Add($"Perfil: {name}", null, (_, _) => Dispatcher.Invoke(() => SelectPresetById(presetId)));
            }
            menu.Items.Add("-");
            menu.Items.Add("Salir", null, (_, _) => Dispatcher.Invoke(Close));

            _trayIcon.ContextMenuStrip = menu;
            _trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(ShowFromTray);
        }
        catch (Exception ex)
        {
            // Restricted sessions without a tray: the app works anyway (minimize-to-tray is disabled)
            _trayIcon = null;
            VortexLog.Warn("UI", "No se pudo crear el icono de la bandeja del sistema.", ex);
        }
    }

    // ---------------- Devices ----------------

    private async Task RefreshDevicesListAsync()
    {
        var devices = await Task.Run(() => _inputProvider.DiscoverDevices());

        // Keeps the current controller (or the one saved in settings); if it was unplugged, it stays marked
        // "(desconectado)" and is picked up again by itself when it comes back.
        var current = _selectedDevice;
        var result = DeviceSelectionPolicy.Merge(
            devices,
            current,
            _appSettings.Settings.LastDeviceId,
            d => d.UniqueId,
            d => d.AsDisconnected());

        _refreshingDevices = true;
        try
        {
            CmbPhysicalDevices.DisplayMemberPath = nameof(DetectedPhysicalDevice.DisplayName);
            CmbPhysicalDevices.ItemsSource = result.Items;
            CmbPhysicalDevices.SelectedItem = result.Selected;
        }
        finally
        {
            _refreshingDevices = false;
        }

        await ApplySelectedDeviceAsync(result.Selected);
    }

    /// <summary>
    /// Activates the chosen controller. If it is the same native HID one that was already open, the reader is
    /// not recreated (it reconnects by itself). DirectInput is reopened, because its handle is no longer valid
    /// after an unplug.
    /// </summary>
    private async Task ApplySelectedDeviceAsync(DetectedPhysicalDevice? device)
    {
        var previous = _selectedDevice;
        bool sameDevice = previous != null && device != null && previous.UniqueId == device.UniqueId;
        bool wasMissing = _selectedDeviceMissing;
        _selectedDevice = device;

        if (device is { IsDisconnectedPlaceholder: true })
        {
            if (!wasMissing)
            {
                VortexLog.Warn("HotPlug", $"Se desconectó '{previous?.DisplayName}'; esperando a que vuelva.");
            }
            _selectedDeviceMissing = true;
            RefreshNotices();
            UpdateDeviceUi();
            return;
        }

        _selectedDeviceMissing = false;
        RefreshNotices();

        if (!sameDevice || device is not { IsNativeHid: true })
        {
            _inputProvider.SelectDevice(device);
        }
        PushPhysicalOutput(); // LED/vibration of the active profile on the controller

        if (device != null)
        {
            _appSettings.RememberDevice(device.UniqueId);
            var sig = _aiAssistant.IdentifyController(device.VendorId, device.ProductId, device.DisplayName);
            TxtAiDeviceSignature.Text = sig.Summary;
        }

        if (wasMissing && sameDevice)
        {
            VortexLog.Info("HotPlug", $"'{device!.DisplayName}' se reconectó.");
            ShowMessage($"{device.DisplayName} se volvió a conectar.", StatusSeverity.Ok);
        }

        UpdateDeviceUi();

        // Hide the new controller (or the reconnected one: its instance ID may change between USB and BT)
        if (_pollingLoop.IsRunning && (!sameDevice || wasMissing))
        {
            await ApplyHidHideAsync();
        }
    }

    /// <summary>Empty state of the live panel and the DualSense section, depending on the selected controller.</summary>
    private void UpdateDeviceUi()
    {
        var device = _selectedDevice;
        bool demo = ChkDemoSignal.IsChecked == true;
        LiveEmpty.Visibility = device == null && !demo ? Visibility.Visible : Visibility.Collapsed;
        _navigation.DualSenseAvailable = device is { IsNativeDualSense: true, IsDisconnectedPlaceholder: false };
    }

    private void BtnDeviceHelp_Click(object sender, RoutedEventArgs e)
    {
        ShowMessage("Comprueba el cable o el emparejamiento Bluetooth y pulsa «Buscar mandos». " +
                    "Si sigue sin aparecer, activa la señal de prueba en Ajustes › Diagnóstico para revisar el resto de la app.",
            StatusSeverity.Info);
    }

    // ---------------- Profiles ----------------

    /// <summary>Groups "Mis perfiles" / "Presets incluidos" in the profile list.</summary>
    private sealed class ProfileGroupConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is true ? "PRESETS INCLUIDOS" : "MIS PERFILES";

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    private void RefreshProfilesList()
    {
        _gameWatcher.MonitoredProfiles = _profiles;

        _profilesView = new ListCollectionView(_profiles)
        {
            Filter = o => o is ControllerProfile p && MatchesSearch(p)
        };
        _profilesView.SortDescriptions.Add(new SortDescription(nameof(ControllerProfile.IsBuiltInPreset), ListSortDirection.Ascending));
        _profilesView.SortDescriptions.Add(new SortDescription(nameof(ControllerProfile.Name), ListSortDirection.Ascending));
        _profilesView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ControllerProfile.IsBuiltInPreset), new ProfileGroupConverter()));

        var quickView = new ListCollectionView(_profiles);
        quickView.SortDescriptions.Add(new SortDescription(nameof(ControllerProfile.IsBuiltInPreset), ListSortDirection.Ascending));
        quickView.SortDescriptions.Add(new SortDescription(nameof(ControllerProfile.Name), ListSortDirection.Ascending));

        _syncingProfileSelection = true;
        try
        {
            LstProfiles.ItemsSource = _profilesView;
            CmbProfileQuick.ItemsSource = quickView;
            LstProfiles.SelectedItem = _activeProfile;
            CmbProfileQuick.SelectedItem = _activeProfile;
        }
        finally
        {
            _syncingProfileSelection = false;
        }
    }

    private bool MatchesSearch(ControllerProfile profile)
    {
        string query = TxtProfileSearch?.Text?.Trim() ?? string.Empty;
        return query.Length == 0
               || profile.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
               || profile.LinkedProcessNames.Any(n => n.Contains(query, StringComparison.CurrentCultureIgnoreCase));
    }

    private void TxtProfileSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_profilesView == null) return;
        _syncingProfileSelection = true;
        try
        {
            _profilesView.Refresh();
            LstProfiles.SelectedItem = _activeProfile;
        }
        finally
        {
            _syncingProfileSelection = false;
        }
    }

    /// <summary>
    /// Single entry point to switch the active profile (list, quick selector, Ctrl+Tab, auto-switch, tray).
    /// Saves the pending edits of the previous one first.
    /// </summary>
    private void ActivateProfile(ControllerProfile profile)
    {
        if (!ReferenceEquals(profile, _activeProfile))
        {
            _autosaver.Flush();
            LoadProfileIntoUi(profile);
            _appSettings.RememberProfile(profile.Id);
            if (_pollingLoop.IsRunning)
            {
                _vigemService.StartVirtualController(_activeProfile.TargetControllerType);
                _pollingLoop.TargetHz = _activeProfile.PollingRateHz;
            }
            UpdateDriverStatus();
        }

        _syncingProfileSelection = true;
        try
        {
            LstProfiles.SelectedItem = profile;
            CmbProfileQuick.SelectedItem = profile;
        }
        finally
        {
            _syncingProfileSelection = false;
        }
    }

    private void LoadProfileIntoUi(ControllerProfile profile)
    {
        _suppressUiEvents = true;
        try
        {
            _activeProfile = profile;
            _profileEditor.Load(profile); // the editor edits this same instance
            RefreshProfileHeader();
        }
        finally
        {
            _suppressUiEvents = false;
        }

        PublishRuntimeProfile();
        PublishFeedbackSettings();
        RefreshNotices();
        UpdateEngineUi();
    }

    /// <summary>Everything outside the editor that depends on the profile (deadzone circles, button labels…).</summary>
    private void RefreshProfileHeader()
    {
        var p = _activeProfile;
        TxtSticksSub.Text = string.Format(CultureInfo.CurrentCulture, "Zona muerta {0:F1} % · {1:F1} %",
            p.LeftStick.InnerDeadzone * 100, p.RightStick.InnerDeadzone * 100);
        UpdateDeadzoneCircles();

        bool sony = p.TargetControllerType == VirtualControllerType.DualShock4;
        LblBtnSouth.Text = sony ? "Cruz" : "A";
        LblBtnEast.Text = sony ? "Círculo" : "B";
        LblBtnWest.Text = sony ? "Cuadrado" : "X";
        LblBtnNorth.Text = sony ? "Triángulo" : "Y";
        LblBtnLB.Text = sony ? "L1" : "LB";
        LblBtnRB.Text = sony ? "R1" : "RB";
        LblBtnBack.Text = sony ? "Share" : "View";
        LblBtnStart.Text = sony ? "Options" : "Menu";
        LblBtnGuide.Text = sony ? "PS" : "Guía";
        TxtL2Label.Text = sony ? "Izquierdo · L2" : "Izquierdo · LT";
        TxtR2Label.Text = sony ? "Derecho · R2" : "Derecho · RT";
        if (p.RightTrigger.HairTriggerMode)
            TxtR2Label.Text += string.Format(CultureInfo.CurrentCulture, " · disparo rápido al {0:F0} %", p.RightTrigger.HairTriggerThreshold * 100);

        TxtDeleteProfileIcon.Text = p.IsBuiltInPreset ? "" : "";
        TxtDeleteProfileLabel.Text = p.IsBuiltInPreset ? "Restablecer" : "Borrar";
        BtnDeleteProfile.ToolTip = p.IsBuiltInPreset
            ? "Vuelve a los valores de fábrica de este preset"
            : "Borra el perfil y su archivo";
    }

    /// <summary>
    /// Any change in the editor: publishes the snapshot to the loop, schedules the autosave and applies what
    /// needs action outside the profile (virtual controller type, HidHide, polling, lists).
    /// </summary>
    private async void OnProfileEdited(string what)
    {
        if (_suppressUiEvents) return;

        PublishRuntimeProfile();
        RefreshProfileHeader();
        _autosaver.MarkDirty();

        // Vibration, LED and adaptive triggers: cheap, and the reader only resends if the result changed
        PublishFeedbackSettings();
        RefreshNotices(); // the virtual type or HidHide may have changed

        switch (what)
        {
            case nameof(ProfileEditorViewModel.Name):
                _profilesView?.Refresh();
                (CmbProfileQuick.ItemsSource as ListCollectionView)?.Refresh();
                ActivateProfile(_activeProfile); // keeps both selectors on it after re-sorting
                break;

            case nameof(ProfileEditorViewModel.TargetControllerType):
                if (_pollingLoop.IsRunning)
                {
                    var type = _activeProfile.TargetControllerType;
                    await Task.Run(() => _vigemService.StartVirtualController(type));
                }
                UpdateDriverStatus();
                break;

            case nameof(ProfileEditorViewModel.HidePhysicalControllerWithHidHide):
                if (_pollingLoop.IsRunning) await ApplyHidHideAsync();
                UpdateDriverStatus();
                break;

            case nameof(ProfileEditorViewModel.PollingRateHz):
                _pollingLoop.TargetHz = _activeProfile.PollingRateHz;
                break;
        }
    }

    /// <summary>
    /// Publishes a snapshot of the profile being edited to the 1000 Hz loop.
    /// Call after any change to <see cref="_activeProfile"/>.
    /// </summary>
    private void PublishRuntimeProfile()
    {
        _runtimeProfile = ProfileRepository.DeepCopy(_activeProfile);
    }

    /// <summary>Copies the profile's vibration gains and lightbar color, and re-sends the output to the controller.</summary>
    private void PublishFeedbackSettings()
    {
        var ff = _activeProfile.ForceFeedback;
        _feedbackSettings = new FeedbackSettings(
            new ForceFeedbackConfiguration
            {
                Enabled = ff.Enabled,
                OverallStrength = ff.OverallStrength,
                LargeMotorGain = ff.LargeMotorGain,
                SmallMotorGain = ff.SmallMotorGain
            },
            _activeProfile.LightbarHex,
            AdaptiveTriggerEffect.FromConfig(_activeProfile.LeftAdaptiveTrigger),
            AdaptiveTriggerEffect.FromConfig(_activeProfile.RightAdaptiveTrigger));
        PushPhysicalOutput();
    }

    /// <summary>
    /// Sends vibration (from the game, with the profile gains) and LED to the physical controller.
    /// It does not block: the HID reader thread writes it. Safe from any thread.
    /// </summary>
    private void PushPhysicalOutput(ForceFeedbackState? overrideFeedback = null)
    {
        var settings = _feedbackSettings;
        var output = ForceFeedbackMixer.Compute(
            overrideFeedback ?? _lastGameFeedback, settings.Config, settings.LightbarHex,
            settings.LeftTrigger, settings.RightTrigger);
        _inputProvider.SetOutput(output);
    }

    // ---------------- Engine ----------------

    private async Task StartEmulationEngineAsync()
    {
        var targetType = _activeProfile.TargetControllerType;
        int hz = _activeProfile.PollingRateHz;

        BtnToggleEngine.IsEnabled = false;
        TxtEngineLabel.Text = "Iniciando…";
        try
        {
            await Task.Run(() => _vigemService.StartVirtualController(targetType));
            await ApplyHidHideAsync();
            _pollingLoop.Start(hz);
        }
        finally
        {
            UpdateEngineUi();
            UpdateDriverStatus();
        }
    }

    private void StopEmulationEngine()
    {
        _pollingLoop.Stop();
        _vigemService.StopVirtualController();
        _kmInjector.ReleaseAllKeys();
        _hidHideService.RestorePhysicalDevice();

        // No game anymore: stop the vibration (the LED keeps the profile color)
        _lastGameFeedback = null;
        PushPhysicalOutput();

        TxtPollingHz.Text = "—";
        TxtLatencyJitter.Text = "—";
        UpdateEngineUi();
        UpdateDriverStatus();
        ShowMessage("Emulación detenida. El mando físico vuelve a estar visible.", StatusSeverity.Info);
    }

    private void OnHighPrecisionTick(double deltaSeconds)
    {
        var physical = _inputProvider.PollCurrentState(_selectedDevice);
        var profile = _runtimeProfile; // a single read per frame: the whole frame uses the same snapshot
        var virtualOut = _virtualFrames.WriteBuffer;

        _pipelineEngine.ProcessFrame(physical, profile, deltaSeconds, virtualOut);
        _driftRecorder.AddSample(physical, deltaSeconds); // returns immediately when not calibrating

        _vigemService.SubmitState(virtualOut);
        _kmInjector.ApplyOutput(virtualOut);

        _latestPhysical = physical;
        _virtualFrames.Publish();
    }

    /// <summary>UI timer (60 Hz): only the visible section is updated.</summary>
    private void UpdateLiveVisualizerUi()
    {
        UpdateCalibrationUi();

        var raw = _latestPhysical;
        var outState = _virtualFrames.ReadLatest();

        // Live point on the curve, for the stick shown in the editor
        bool left = _profileEditor.CurveStickIndex == 0;
        double rx = left ? raw.LX : raw.RX, ry = left ? raw.LY : raw.RY;
        double ox = left ? outState.LX : outState.RX, oy = left ? outState.LY : outState.RY;
        double rawMag = Math.Sqrt((rx * rx) + (ry * ry)), outMag = Math.Sqrt((ox * ox) + (oy * oy));

        if (SecSticks.Visibility == Visibility.Visible)
        {
            EditorSticks.UpdateLiveDot(rawMag, outMag);
            return;
        }
        if (SecLive.Visibility != Visibility.Visible) return;

        LiveCurve.UpdateLiveDot(rawMag, outMag);

        // Triggers
        BarL2.Value = outState.L2;
        BarR2.Value = outState.R2;
        TxtL2Value.Text = $"{outState.L2 * 100:F0} %";
        TxtR2Value.Text = $"{outState.R2 * 100:F0} %";

        // Stick radars (Canvas 170x170, center = 85, radius = 80)
        PositionDot(DotLeftRaw, 85 + (raw.LX * 80), 85 - (raw.LY * 80), 8);
        PositionDot(DotLeftOut, 85 + (outState.LX * 80), 85 - (outState.LY * 80), 12);
        TxtLeftStickCoords.Text = FormatCoords(raw.LX, raw.LY, outState.LX, outState.LY);

        PositionDot(DotRightRaw, 85 + (raw.RX * 80), 85 - (raw.RY * 80), 8);
        PositionDot(DotRightOut, 85 + (outState.RX * 80), 85 - (outState.RY * 80), 12);
        TxtRightStickCoords.Text = FormatCoords(raw.RX, raw.RY, outState.RX, outState.RY);

        // Button matrix
        SetPillState(PillBtnSouth, outState.IsButtonActive(ControllerButton.South));
        SetPillState(PillBtnEast, outState.IsButtonActive(ControllerButton.East));
        SetPillState(PillBtnWest, outState.IsButtonActive(ControllerButton.West));
        SetPillState(PillBtnNorth, outState.IsButtonActive(ControllerButton.North));
        SetPillState(PillBtnLB, outState.IsButtonActive(ControllerButton.LeftShoulder));
        SetPillState(PillBtnRB, outState.IsButtonActive(ControllerButton.RightShoulder));
        SetPillState(PillBtnBack, outState.IsButtonActive(ControllerButton.Back));
        SetPillState(PillBtnStart, outState.IsButtonActive(ControllerButton.Start));
        SetPillState(PillBtnGuide, outState.IsButtonActive(ControllerButton.Guide));
        SetPillState(PillBtnTouchpad, outState.IsButtonActive(ControllerButton.TouchpadClick));
        SetPillState(PillBtnL3, outState.IsButtonActive(ControllerButton.LeftThumb));
        SetPillState(PillBtnR3, outState.IsButtonActive(ControllerButton.RightThumb));

        // Gyro telemetry
        TxtGyroTelemetry.Text = string.Format(CultureInfo.CurrentCulture, "Giro {0:F1} °/s · ratón Δ {1:F1}, {2:F1}",
            raw.GyroYaw, outState.MouseDeltaX, outState.MouseDeltaY);
    }

    private static string FormatCoords(double rx, double ry, double ox, double oy) =>
        $"{Signed(rx)} {Signed(ry)} → {Signed(ox)} {Signed(oy)}";

    /// <summary>"+0,42" / "−0,35" with a real minus sign; values that round to zero show as "+0,00".</summary>
    private static string Signed(double value)
    {
        double rounded = Math.Round(value, 2);
        return (rounded < 0 ? "−" : "+") + Math.Abs(rounded).ToString("0.00", CultureInfo.CurrentCulture);
    }

    private static void PositionDot(UIElement dot, double centerX, double centerY, double size)
    {
        Canvas.SetLeft(dot, centerX - (size * 0.5));
        Canvas.SetTop(dot, centerY - (size * 0.5));
    }

    private void SetPillState(Border pill, bool active)
    {
        var background = active ? _pillOn : _pillOff;
        if (ReferenceEquals(pill.Background, background)) return;
        pill.Background = background;
        if (pill.Child is TextBlock tb) tb.Foreground = active ? _pillTextOn : _pillTextOff;
    }

    private void UpdateDeadzoneCircles()
    {
        double leftRadiusPx = Math.Clamp(_activeProfile.LeftStick.InnerDeadzone, 0.01, 0.8) * 160.0;
        EllipseLeftDeadzone.Width = leftRadiusPx;
        EllipseLeftDeadzone.Height = leftRadiusPx;
        Canvas.SetLeft(EllipseLeftDeadzone, 85.0 - (leftRadiusPx * 0.5));
        Canvas.SetTop(EllipseLeftDeadzone, 85.0 - (leftRadiusPx * 0.5));

        double rightRadiusPx = Math.Clamp(_activeProfile.RightStick.InnerDeadzone, 0.01, 0.8) * 160.0;
        EllipseRightDeadzone.Width = rightRadiusPx;
        EllipseRightDeadzone.Height = rightRadiusPx;
        Canvas.SetLeft(EllipseRightDeadzone, 85.0 - (rightRadiusPx * 0.5));
        Canvas.SetTop(EllipseRightDeadzone, 85.0 - (rightRadiusPx * 0.5));
    }

    private void SelectPresetById(string presetId)
    {
        var found = _profiles.FirstOrDefault(p => p.Id == presetId);
        if (found != null) ActivateProfile(found);
    }

    private async void BtnToggleEngine_Click(object sender, RoutedEventArgs e)
    {
        if (_pollingLoop.IsRunning)
            StopEmulationEngine();
        else if (BtnToggleEngine.IsEnabled)
            await StartEmulationEngineAsync();
    }

    private void ChkDemoSignal_Changed(object sender, RoutedEventArgs e)
    {
        if (_inputProvider == null || ChkDemoSignal == null) return;
        _inputProvider.EnableInteractiveDemoSignal = ChkDemoSignal.IsChecked == true;
        if (LiveEmpty != null) UpdateDeviceUi();
    }

    private async void BtnRefreshDevices_Click(object sender, RoutedEventArgs e)
    {
        await RefreshDevicesListAsync();
        ShowMessage(_selectedDevice == null ? "No se encontró ningún mando." : $"Mando seleccionado: {_selectedDevice.DisplayName}.",
            _selectedDevice == null ? StatusSeverity.Warning : StatusSeverity.Ok);
    }

    private async void CmbPhysicalDevices_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshingDevices) return; // RefreshDevicesListAsync applies the selection itself
        await ApplySelectedDeviceAsync(CmbPhysicalDevices.SelectedItem as DetectedPhysicalDevice);
    }

    private void LstProfiles_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingProfileSelection) return;
        if (LstProfiles.SelectedItem is ControllerProfile selected) ActivateProfile(selected);
    }

    private void CmbProfileQuick_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingProfileSelection) return;
        if (CmbProfileQuick.SelectedItem is ControllerProfile selected) ActivateProfile(selected);
    }

    private void BtnEditCurve_Click(object sender, RoutedEventArgs e)
    {
        _profileEditor.AnalogSelection = _profileEditor.CurveStickIndex;
        Navigate(AppSection.Sticks);
    }

    private void BtnCloneProfile_Click(object sender, RoutedEventArgs e)
    {
        _autosaver.Flush();
        var copy = _profileRepository.CloneProfile(_activeProfile);
        _profileRepository.SaveProfile(copy);
        _profiles.Add(copy);
        RefreshProfilesList();
        ActivateProfile(copy);
        Navigate(AppSection.Profiles);
        ShowMessage($"Se creó «{copy.Name}». Edítalo aquí o en las secciones del editor.", StatusSeverity.Ok);
    }

    private void BtnExportProfile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Exportar perfil de VortexPad",
            Filter = "Perfil VortexPad (*.json)|*.json",
            FileName = SafeFileName(_activeProfile.Name) + ".json"
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            ProfileRepository.ExportProfile(_activeProfile, dialog.FileName);
            ShowMessage($"Perfil «{_activeProfile.Name}» exportado a {dialog.FileName}.", StatusSeverity.Ok);
        }
        catch (Exception ex)
        {
            VortexLog.Warn("Perfiles", "No se pudo exportar el perfil.", ex);
            ShowMessage($"No se pudo exportar: {ex.Message}", StatusSeverity.Error);
        }
    }

    private void BtnImportProfile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Importar perfil de VortexPad",
            Filter = "Perfil VortexPad (*.json)|*.json|Todos los archivos (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            var imported = ProfileRepository.ImportProfile(dialog.FileName, _profiles.Select(p => p.Id));
            _profileRepository.SaveProfile(imported);
            _profiles.Add(imported);
            RefreshProfilesList();
            ActivateProfile(imported);
            ShowMessage($"Perfil «{imported.Name}» importado.", StatusSeverity.Ok);
        }
        catch (Exception ex)
        {
            VortexLog.Warn("Perfiles", "No se pudo importar el perfil.", ex);
            ShowMessage($"No se pudo importar: {ex.Message}", StatusSeverity.Error);
        }
    }

    private void BtnDeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        var profile = _activeProfile;
        bool builtIn = profile.IsBuiltInPreset;

        string question = builtIn
            ? $"¿Restablecer «{profile.Name}» a sus valores de fábrica? Se pierden tus cambios en este preset."
            : $"¿Borrar «{profile.Name}»? Se borra su archivo y no se puede deshacer.";
        string title = builtIn ? "Restablecer preset" : "Borrar perfil";
        if (MessageBox.Show(this, question, title, MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel) != MessageBoxResult.OK)
        {
            return;
        }

        _autosaver.Flush(); // nothing pending may rewrite the file after deleting it
        _profileRepository.DeleteProfile(profile.Id);

        if (builtIn)
        {
            // Swap the in-memory instance for the original preset
            var original = BuiltInPresets.CreateAll().First(p => p.Id == profile.Id);
            int index = _profiles.IndexOf(profile);
            _profiles[index] = original;
            RefreshProfilesList();
            ActivateProfile(original);
            ShowMessage($"Preset «{original.Name}» restablecido.", StatusSeverity.Ok);
        }
        else
        {
            _profiles.Remove(profile);
            RefreshProfilesList();
            ActivateProfile(_profiles.First());
            ShowMessage($"Perfil «{profile.Name}» borrado.", StatusSeverity.Ok);
        }
    }

    private static string SafeFileName(string name)
    {
        foreach (char c in System.IO.Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "perfil" : name;
    }

    // ---------------- Drift calibration ----------------

    private void BtnAiCalibrateDrift_Click(object sender, RoutedEventArgs e)
    {
        // The same button cancels an ongoing calibration
        if (_calibrationUiActive)
        {
            _driftRecorder.Cancel();
            return;
        }

        if (!_driftRecorder.Start()) return;

        _calibrationUiActive = true;
        TxtCalibrateLabel.Text = "Cancelar calibración";
        CalibrationPanel.Visibility = Visibility.Visible;
        BarCalibration.Visibility = Visibility.Visible;
        BarCalibration.Value = 0;
        TxtAiCalibrationResult.Text = $"Midiendo el reposo durante {_driftRecorder.DurationSeconds:F0} s: no toques los sticks…";
        EnsureCalibrationSampler();
    }

    /// <summary>
    /// With emulation running, the 1000 Hz loop feeds the recorder. If it is stopped,
    /// a temporary background sampler is used so you can calibrate without starting emulation.
    /// </summary>
    private void EnsureCalibrationSampler()
    {
        if (_pollingLoop.IsRunning || _calibrationSamplerCts != null) return;

        var cts = new CancellationTokenSource();
        _calibrationSamplerCts = cts;
        var device = _selectedDevice;

        Task.Run(() =>
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            double last = 0;
            while (!cts.IsCancellationRequested && _driftRecorder.IsRecording && !_pollingLoop.IsRunning)
            {
                Thread.Sleep(1);
                double now = clock.Elapsed.TotalSeconds;
                _driftRecorder.AddSample(_inputProvider.PollCurrentState(device), now - last);
                last = now;
            }

            // Lets a new sampler start if emulation stops again mid-measurement
            Interlocked.CompareExchange(ref _calibrationSamplerCts, null, cts);
        });
    }

    /// <summary>UI timer: updates the progress and applies the result when the measurement ends.</summary>
    private void UpdateCalibrationUi()
    {
        if (!_calibrationUiActive) return;

        BarCalibration.Value = _driftRecorder.Progress;
        if (_driftRecorder.IsRecording)
        {
            EnsureCalibrationSampler(); // in case emulation was stopped mid-measurement
            return;
        }

        _calibrationUiActive = false;
        _calibrationSamplerCts?.Cancel();
        _calibrationSamplerCts = null;
        TxtCalibrateLabel.Text = "Calibrar drift…";
        BarCalibration.Visibility = Visibility.Collapsed;

        string message;
        StatusSeverity severity;
        switch (_driftRecorder.Status)
        {
            case DriftCalibrationStatus.Completed:
                var result = _driftRecorder.BuildResult(_aiAssistant)!;
                _profileEditor.LeftStick.InnerDeadzone = result.RecommendedLeftDeadzone;
                _profileEditor.RightStick.InnerDeadzone = result.RecommendedRightDeadzone; // edits → publish + autosave
                message = result.DiagnosticSummary;
                severity = StatusSeverity.Ok;
                break;
            case DriftCalibrationStatus.MovementDetected:
                message = "Se movió un stick durante la medición. Suéltalos y vuelve a calibrar; el perfil no cambió.";
                severity = StatusSeverity.Warning;
                break;
            default:
                message = "Calibración cancelada. El perfil no cambió.";
                severity = StatusSeverity.Info;
                break;
        }

        TxtAiCalibrationResult.Text = message;
        ShowMessage(message, severity);
    }

    private void BtnApplyAiGenre_Click(object sender, RoutedEventArgs e)
    {
        var genre = CmbAiGenre.SelectedIndex switch
        {
            0 => GameGenrePreset.CompetitiveFps,
            1 => GameGenrePreset.RacingSim,
            2 => GameGenrePreset.FightingArcade,
            3 => GameGenrePreset.SoulslikeRpg,
            _ => GameGenrePreset.GeneralAction
        };

        string summary = _aiAssistant.ApplySmartTuning(_activeProfile, genre);
        LoadProfileIntoUi(_activeProfile);
        _autosaver.MarkDirty(); // the model was changed directly, not through the editor
        TxtAiGenreResult.Text = summary;
        ShowMessage("Ajuste automático aplicado al perfil.", StatusSeverity.Ok);
    }

    // ---------------- Test actions ----------------

    /// <summary>Runs the selected macro once through the pipeline (a copy: the UI keeps editing the original).</summary>
    private void OnTestMacroRequested(MacroViewModel macro)
    {
        if (!_pollingLoop.IsRunning)
        {
            ShowMessage("Inicia la emulación (F5) para probar la macro.", StatusSeverity.Info);
            return;
        }

        var source = macro.Model;
        var copy = new MacroDefinition
        {
            Id = "ui-test-" + source.Id,
            Name = source.Name,
            TriggerButton = ControllerButton.None // fired only from the UI
        };
        foreach (var step in source.Steps)
        {
            copy.Steps.Add(new MacroStep
            {
                StepType = step.StepType,
                Button = step.Button,
                VirtualKeyCode = step.VirtualKeyCode,
                DurationMs = step.DurationMs
            });
        }

        _pipelineEngine.EnqueueMacro(copy);
        ShowMessage($"Macro «{source.Name}» enviada al mando virtual.", StatusSeverity.Ok);
    }

    private async void BtnTestRumble_Click(object sender, RoutedEventArgs e)
    {
        var test = new ForceFeedbackState { LargeMotor = 0.85, SmallMotor = 0.65 };

        BarRumbleLarge.Value = test.LargeMotor;
        BarRumbleSmall.Value = test.SmallMotor;
        PushPhysicalOutput(test); // vibrates the real controller with the profile gains

        await Task.Delay(450);

        BarRumbleLarge.Value = 0;
        BarRumbleSmall.Value = 0;
        PushPhysicalOutput(); // back to the game's vibration (or off)

        if (_inputProvider.SupportsOutput)
            ShowMessage("Prueba de vibración enviada al mando físico.", StatusSeverity.Ok);
        else
            ShowMessage("Este mando no admite vibración desde VortexPad (solo DualSense y DualShock 4 por HID nativo).", StatusSeverity.Info);
    }

    // ---------------- Engine callbacks ----------------

    private void OnPollingTelemetryUpdated(PollingTelemetry telemetry)
    {
        Dispatcher.BeginInvoke(() =>
        {
            TxtPollingHz.Text = string.Format(CultureInfo.CurrentCulture, "{0:F1} Hz", telemetry.ActualRateHz);
            TxtLatencyJitter.Text = string.Format(CultureInfo.CurrentCulture, "{0:F1} µs · {1:F3} ms",
                telemetry.LastTickDurationMicroseconds, telemetry.AverageJitterMilliseconds);
        });
    }

    private void OnControllerFeedbackReceived(ForceFeedbackState feedback)
    {
        // ViGEm thread: forward to the physical controller right away, without going through the UI
        _lastGameFeedback = feedback;
        PushPhysicalOutput();

        Dispatcher.BeginInvoke(() =>
        {
            BarRumbleLarge.Value = feedback.LargeMotor;
            BarRumbleSmall.Value = feedback.SmallMotor;
        });
    }

    private void OnForegroundProcessChanged(string processName, ControllerProfile? matchedProfile)
    {
        Dispatcher.BeginInvoke(() =>
        {
            TxtActiveProcess.Text = matchedProfile != null
                ? $"{processName}.exe → {matchedProfile.Name}"
                : $"{processName}.exe";

            if (matchedProfile != null && matchedProfile.Id != _activeProfile.Id)
            {
                ActivateProfile(matchedProfile);
                ShowMessage($"Se activó «{matchedProfile.Name}» al abrir {processName}.exe.", StatusSeverity.Info);
            }
        });
    }

    private void OnMainWindowClosed(object? sender, EventArgs e)
    {
        _autosaver.Flush(); // C1: never lose the last edits on close
        _uiRefreshTimer.Stop();
        _messageTimer.Stop();
        _driftRecorder.Cancel();
        _calibrationSamplerCts?.Cancel();
        _gameWatcher.Dispose();
        _deviceWatcher.Dispose();
        _pollingLoop.Dispose();
        _vigemService.Dispose();
        _kmInjector.Dispose();
        _hidHideService.Dispose();
        _inputProvider.Dispose();
        if (_trayIcon != null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }
    }
}
