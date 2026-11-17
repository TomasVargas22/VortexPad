using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using VortexPad.AI;
using VortexPad.Core.Automation;
using VortexPad.Core.Engine;
using VortexPad.Core.Models;
using VortexPad.Core.Processing;
using VortexPad.Core.Profiles;
using VortexPad.Emulation;
using VortexPad.Input;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Point = System.Windows.Point;
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
    private sealed record FeedbackSettings(ForceFeedbackConfiguration Config, string LightbarHex);
    private volatile FeedbackSettings _feedbackSettings = new(new ForceFeedbackConfiguration(), "#00E5FF");
    private volatile ForceFeedbackState? _lastGameFeedback;
    private bool _suppressUiEvents;
    private WinForms.NotifyIcon? _trayIcon;

    public MainWindow()
    {
        _suppressUiEvents = true;
        _profiles = _profileRepository.LoadAllWithBuiltIns().ToList();
        _activeProfile = _profiles.First();
        _runtimeProfile = ProfileRepository.DeepCopy(_activeProfile);

        InitializeComponent();

        _pollingLoop = new HighPrecisionPollingLoop(OnHighPrecisionTick);
        _pollingLoop.TelemetryUpdated += OnPollingTelemetryUpdated;
        _vigemService.FeedbackReceived += OnControllerFeedbackReceived;
        _gameWatcher.ForegroundProcessChanged += OnForegroundProcessChanged;

        InitializeSystemTray();
        RefreshProfilesList();
        LoadProfileIntoUi(_activeProfile);
        RedrawResponseCurveCanvas();

        _uiRefreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _uiRefreshTimer.Tick += (_, _) => UpdateLiveVisualizerUi();
        _uiRefreshTimer.Start();

        Loaded += OnMainWindowLoaded;
        Closed += OnMainWindowClosed;
    }

    private async void OnMainWindowLoaded(object sender, RoutedEventArgs e)
    {
        // If the previous session closed unexpectedly with the controller hidden, make it visible again
        await Task.Run(_hidHideService.RecoverPreviousSession);
        await RefreshDevicesListAsync();
        await StartEmulationEngineAsync();
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

        TxtHidHideStatus.Text = _hidHideService.StatusText;
    }

    private void InitializeSystemTray()
    {
        try
        {
            _trayIcon = new WinForms.NotifyIcon
            {
                Text = "VortexPad — 1000Hz Controller Engine",
                Icon = System.Drawing.SystemIcons.Application,
                Visible = true
            };

            var menu = new WinForms.ContextMenuStrip();
            menu.Items.Add("Mostrar VortexPad", null, (_, _) => Dispatcher.Invoke(() =>
            {
                Show();
                WindowState = WindowState.Normal;
                Activate();
            }));
            menu.Items.Add("-");
            menu.Items.Add("Perfil: Xbox (Standard / Series)", null, (_, _) => Dispatcher.Invoke(() => SelectPresetById(BuiltInPresets.XboxStandardId)));
            menu.Items.Add("Perfil: PS5 / PlayStation (DualSense)", null, (_, _) => Dispatcher.Invoke(() => SelectPresetById(BuiltInPresets.Ps5DualSenseId)));
            menu.Items.Add("Perfil: AI Smart Profile", null, (_, _) => Dispatcher.Invoke(() => SelectPresetById(BuiltInPresets.AiSmartProfileId)));
            menu.Items.Add("-");
            menu.Items.Add("Salir", null, (_, _) => Dispatcher.Invoke(Close));

            _trayIcon.ContextMenuStrip = menu;
            _trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(() =>
            {
                Show();
                WindowState = WindowState.Normal;
                Activate();
            });
        }
        catch
        {
            // Ignore tray icon initialization issues in restricted sessions
        }
    }

    private async Task RefreshDevicesListAsync()
    {
        var devices = await Task.Run(() => _inputProvider.DiscoverDevices());
        CmbPhysicalDevices.ItemsSource = devices;
        CmbPhysicalDevices.DisplayMemberPath = nameof(DetectedPhysicalDevice.DisplayName);
        if (devices.Count > 0)
        {
            CmbPhysicalDevices.SelectedIndex = 0;
        }
    }

    private void RefreshProfilesList()
    {
        _gameWatcher.MonitoredProfiles = _profiles;
        LstProfiles.ItemsSource = null;
        LstProfiles.ItemsSource = _profiles;
        LstProfiles.SelectedItem = _activeProfile;
    }

    private void LoadProfileIntoUi(ControllerProfile profile)
    {
        _suppressUiEvents = true;
        try
        {
            _activeProfile = profile;
            TxtActiveProfileTitle.Text = $"Perfil Activo: {profile.Name}";
            TxtActiveProfileDesc.Text = profile.Description;
            TxtLinkedProcesses.Text = string.Join(", ", profile.LinkedProcessNames);
            ChkHidePhysical.IsChecked = profile.HidePhysicalControllerWithHidHide;

            CmbTargetController.SelectedIndex = profile.TargetControllerType switch
            {
                VirtualControllerType.Xbox360 => 0,
                VirtualControllerType.DualShock4 => 1,
                VirtualControllerType.KeyboardMouse => 2,
                _ => 0
            };

            CmbCurveType.SelectedIndex = profile.RightStick.CurveType switch
            {
                ResponseCurveType.Linear => 0,
                ResponseCurveType.Exponential => 1,
                ResponseCurveType.SCurve => 2,
                ResponseCurveType.CustomBezier => 3,
                _ => 0
            };

            SldInnerDeadzone.Value = profile.RightStick.InnerDeadzone;
            SldAntiDeadzone.Value = profile.RightStick.AntiDeadzone;
            SldCurveExponent.Value = profile.RightStick.CurveExponent;
            SldStickSensitivity.Value = profile.RightStick.Sensitivity;
            ChkHairTriggers.IsChecked = profile.RightTrigger.HairTriggerMode;

            CmbGyroMode.SelectedIndex = profile.Gyro.OutputMode switch
            {
                GyroOutputMode.Disabled => 0,
                GyroOutputMode.RightStick => 1,
                GyroOutputMode.MouseDelta => 2,
                _ => 0
            };
            SldGyroSens.Value = profile.Gyro.SensitivityX;

            var southTurbo = profile.TurboConfigs.FirstOrDefault(t => t.Button == ControllerButton.South);
            ChkTurboSouth.IsChecked = southTurbo?.Enabled ?? false;
            if (southTurbo != null)
            {
                SldTurboHz.Value = southTurbo.FrequencyHz;
            }

            ChkStickToWasd.IsChecked = profile.BaseLayer.LeftStickToWasd;

            UpdateSliderValueLabels();
            UpdateDeadzoneCircles();
            RedrawResponseCurveCanvas();

            try
            {
                var color = (Color)ColorConverter.ConvertFromString(profile.LightbarHex);
                BdLightbarPreview.Background = new SolidColorBrush(color);
            }
            catch
            {
                BdLightbarPreview.Background = new SolidColorBrush(Color.FromRgb(0, 229, 255));
            }
        }
        finally
        {
            _suppressUiEvents = false;
        }

        PublishRuntimeProfile();
        PublishFeedbackSettings();
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
            _activeProfile.LightbarHex);
        PushPhysicalOutput();
    }

    /// <summary>
    /// Sends vibration (from the game, with the profile gains) and LED to the physical controller.
    /// It does not block: the HID reader thread writes it. Safe from any thread.
    /// </summary>
    private void PushPhysicalOutput(ForceFeedbackState? overrideFeedback = null)
    {
        var settings = _feedbackSettings;
        var output = ForceFeedbackMixer.Compute(overrideFeedback ?? _lastGameFeedback, settings.Config, settings.LightbarHex);
        _inputProvider.SetOutput(output);
    }

    private async Task StartEmulationEngineAsync()
    {
        var targetType = _activeProfile.TargetControllerType;
        int hz = _activeProfile.PollingRateHz;

        await Task.Run(() => _vigemService.StartVirtualController(targetType));
        await ApplyHidHideAsync();
        _pollingLoop.Start(hz);

        TxtVigemStatus.Text = _vigemService.StatusMessage;
        BtnToggleEngine.Content = "⏹ Detener Emulación";
        TxtFooterStatus.Text = $"Emulación activa ({_activeProfile.Name}) a {_activeProfile.PollingRateHz} Hz.";
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

        TxtVigemStatus.Text = "Detenido";
        TxtHidHideStatus.Text = _hidHideService.StatusText;
        BtnToggleEngine.Content = "▶ Iniciar Emulación 1000Hz";
        TxtFooterStatus.Text = "Emulación detenida. Mando físico restaurado.";
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

    private void UpdateLiveVisualizerUi()
    {
        UpdateCalibrationUi();

        var raw = _latestPhysical;
        var outState = _virtualFrames.ReadLatest();

        // Triggers
        BarL2.Value = outState.L2;
        BarR2.Value = outState.R2;
        TxtL2Value.Text = $"{outState.L2 * 100:F0}%";
        TxtR2Value.Text = $"{outState.R2 * 100:F0}%";

        // Left Stick Radar (Canvas 170x170, center = 85, radius = 80)
        PositionDot(DotLeftRaw, 85 + (raw.LX * 80), 85 - (raw.LY * 80), 8);
        PositionDot(DotLeftOut, 85 + (outState.LX * 80), 85 - (outState.LY * 80), 12);
        TxtLeftStickCoords.Text = $"Raw: ({raw.LX:F2}, {raw.LY:F2}) → Out: ({outState.LX:F2}, {outState.LY:F2})";

        // Right Stick Radar
        PositionDot(DotRightRaw, 85 + (raw.RX * 80), 85 - (raw.RY * 80), 8);
        PositionDot(DotRightOut, 85 + (outState.RX * 80), 85 - (outState.RY * 80), 12);
        TxtRightStickCoords.Text = $"Raw: ({raw.RX:F2}, {raw.RY:F2}) → Out: ({outState.RX:F2}, {outState.RY:F2})";

        // Live point on Response Curve Canvas (300x150)
        double rawMag = Math.Clamp(Math.Sqrt((raw.RX * raw.RX) + (raw.RY * raw.RY)), 0.0, 1.0);
        double outMag = Math.Clamp(Math.Sqrt((outState.RX * outState.RX) + (outState.RY * outState.RY)), 0.0, 1.0);
        Canvas.SetLeft(DotCurveLive, (rawMag * 300.0) - 5.0);
        Canvas.SetTop(DotCurveLive, ((1.0 - outMag) * 150.0) - 5.0);

        // Button pills highlight
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
        TxtGyroTelemetry.Text = $"Gyro: {raw.GyroYaw:F1}°/s | Mouse Δ: ({outState.MouseDeltaX:F1}, {outState.MouseDeltaY:F1})";
    }

    private static void PositionDot(UIElement dot, double centerX, double centerY, double size)
    {
        Canvas.SetLeft(dot, centerX - (size * 0.5));
        Canvas.SetTop(dot, centerY - (size * 0.5));
    }

    private static void SetPillState(Border pill, bool active)
    {
        pill.Background = active
            ? new SolidColorBrush(Color.FromRgb(0, 229, 255))
            : new SolidColorBrush(Color.FromRgb(30, 41, 59));
        if (pill.Child is TextBlock tb)
        {
            tb.Foreground = active
                ? new SolidColorBrush(Color.FromRgb(6, 16, 24))
                : new SolidColorBrush(Color.FromRgb(241, 245, 249));
        }
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

    private void RedrawResponseCurveCanvas()
    {
        if (PolyResponseCurve == null) return;
        var pts = new PointCollection();
        var cfg = _activeProfile.RightStick;

        const int steps = 60;
        for (int i = 0; i <= steps; i++)
        {
            double xNorm = (double)i / steps;
            var (outX, _) = SignalProcessor.ProcessStick2D(xNorm, 0.0, cfg);
            double px = xNorm * 300.0;
            double py = (1.0 - Math.Clamp(outX, 0.0, 1.0)) * 150.0;
            pts.Add(new Point(px, py));
        }

        PolyResponseCurve.Points = pts;
        TxtCurveFormula.Text =
            $"Curva: {cfg.CurveType} (γ={cfg.CurveExponent:F2}) | Deadzone: {cfg.InnerDeadzone * 100:F1}% | Anti-DZ: {cfg.AntiDeadzone * 100:F1}%";
    }

    private void UpdateSliderValueLabels()
    {
        if (LblInnerDz == null) return;
        LblInnerDz.Text = $"{SldInnerDeadzone.Value * 100:F1}%";
        LblAntiDz.Text = $"{SldAntiDeadzone.Value * 100:F1}%";
        LblExponent.Text = $"{SldCurveExponent.Value:F2}";
        LblSensitivity.Text = $"{SldStickSensitivity.Value:F2}x";
        LblGyroSens.Text = $"{SldGyroSens.Value:F1}x";
        LblTurboHz.Text = $"{SldTurboHz.Value:F0} Hz";
    }

    private void SelectPresetById(string presetId)
    {
        var found = _profiles.FirstOrDefault(p => p.Id == presetId);
        if (found != null)
        {
            LstProfiles.SelectedItem = found;
        }
    }

    private void BtnPresetXbox_Click(object sender, RoutedEventArgs e) => SelectPresetById(BuiltInPresets.XboxStandardId);
    private void BtnPresetPs5_Click(object sender, RoutedEventArgs e) => SelectPresetById(BuiltInPresets.Ps5DualSenseId);
    private void BtnPresetAi_Click(object sender, RoutedEventArgs e) => SelectPresetById(BuiltInPresets.AiSmartProfileId);

    private async void BtnToggleEngine_Click(object sender, RoutedEventArgs e)
    {
        if (_pollingLoop.IsRunning)
            StopEmulationEngine();
        else
            await StartEmulationEngineAsync();
    }

    private void ChkDemoSignal_Changed(object sender, RoutedEventArgs e)
    {
        if (_inputProvider == null || ChkDemoSignal == null) return;
        _inputProvider.EnableInteractiveDemoSignal = ChkDemoSignal.IsChecked == true;
    }

    private async void BtnRefreshDevices_Click(object sender, RoutedEventArgs e)
    {
        await RefreshDevicesListAsync();
    }

    private async void CmbPhysicalDevices_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedDevice = CmbPhysicalDevices.SelectedItem as DetectedPhysicalDevice;
        _inputProvider.SelectDevice(_selectedDevice);
        PushPhysicalOutput(); // LED/vibration of the active profile on the new controller

        if (_selectedDevice != null)
        {
            var sig = _aiAssistant.IdentifyController(_selectedDevice.VendorId, _selectedDevice.ProductId, _selectedDevice.DisplayName);
            TxtAiDeviceSignature.Text = sig.Summary;
        }

        // Unhide the previous controller and hide the new one
        if (_pollingLoop.IsRunning)
        {
            await ApplyHidHideAsync();
        }
    }

    private void LstProfiles_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LstProfiles.SelectedItem is ControllerProfile selected)
        {
            LoadProfileIntoUi(selected);
            if (_pollingLoop.IsRunning)
            {
                _vigemService.StartVirtualController(_activeProfile.TargetControllerType);
                TxtVigemStatus.Text = _vigemService.StatusMessage;
            }
        }
    }

    private void BtnCloneProfile_Click(object sender, RoutedEventArgs e)
    {
        var copy = _profileRepository.CloneProfile(_activeProfile);
        _profileRepository.SaveProfile(copy);
        _profiles.Add(copy);
        RefreshProfilesList();
        LstProfiles.SelectedItem = copy;
        TxtFooterStatus.Text = $"Perfil clonado y guardado: {copy.Name}";
    }

    private void BtnSaveProfile_Click(object sender, RoutedEventArgs e)
    {
        _profileRepository.SaveProfile(_activeProfile);
        TxtFooterStatus.Text = $"Perfil '{_activeProfile.Name}' guardado en disco.";
    }

    private void CmbTargetController_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressUiEvents) return;
        _activeProfile.TargetControllerType = CmbTargetController.SelectedIndex switch
        {
            0 => VirtualControllerType.Xbox360,
            1 => VirtualControllerType.DualShock4,
            2 => VirtualControllerType.KeyboardMouse,
            _ => VirtualControllerType.Xbox360
        };
        PublishRuntimeProfile();
        if (_pollingLoop.IsRunning)
        {
            _vigemService.StartVirtualController(_activeProfile.TargetControllerType);
            TxtVigemStatus.Text = _vigemService.StatusMessage;
        }
    }

    private void TxtLinkedProcesses_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressUiEvents) return;
        _activeProfile.LinkedProcessNames = TxtLinkedProcesses.Text
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }

    private void CmbCurveType_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressUiEvents) return;
        var curve = CmbCurveType.SelectedIndex switch
        {
            0 => ResponseCurveType.Linear,
            1 => ResponseCurveType.Exponential,
            2 => ResponseCurveType.SCurve,
            3 => ResponseCurveType.CustomBezier,
            _ => ResponseCurveType.Linear
        };
        _activeProfile.LeftStick.CurveType = curve;
        _activeProfile.RightStick.CurveType = curve;
        PublishRuntimeProfile();
        RedrawResponseCurveCanvas();
    }

    private void StickSliders_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressUiEvents) return;
        _activeProfile.LeftStick.InnerDeadzone = SldInnerDeadzone.Value;
        _activeProfile.RightStick.InnerDeadzone = SldInnerDeadzone.Value;
        _activeProfile.LeftStick.AntiDeadzone = SldAntiDeadzone.Value;
        _activeProfile.RightStick.AntiDeadzone = SldAntiDeadzone.Value;
        _activeProfile.LeftStick.CurveExponent = SldCurveExponent.Value;
        _activeProfile.RightStick.CurveExponent = SldCurveExponent.Value;
        _activeProfile.LeftStick.Sensitivity = SldStickSensitivity.Value;
        _activeProfile.RightStick.Sensitivity = SldStickSensitivity.Value;
        _activeProfile.Gyro.SensitivityX = SldGyroSens.Value;
        _activeProfile.Gyro.SensitivityY = SldGyroSens.Value * 0.85;
        PublishRuntimeProfile();

        UpdateSliderValueLabels();
        UpdateDeadzoneCircles();
        RedrawResponseCurveCanvas();
    }

    private void CmbGyroMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressUiEvents) return;
        _activeProfile.Gyro.OutputMode = CmbGyroMode.SelectedIndex switch
        {
            1 => GyroOutputMode.RightStick,
            2 => GyroOutputMode.MouseDelta,
            _ => GyroOutputMode.Disabled
        };
        _activeProfile.Gyro.ActivationMode = GyroActivationMode.AlwaysOn;
        PublishRuntimeProfile();
    }

    private async void ProfileSetting_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressUiEvents || _activeProfile == null || ChkHidePhysical == null || ChkHairTriggers == null || ChkStickToWasd == null) return;

        bool hideChanged = _activeProfile.HidePhysicalControllerWithHidHide != (ChkHidePhysical.IsChecked == true);
        _activeProfile.HidePhysicalControllerWithHidHide = ChkHidePhysical.IsChecked == true;
        _activeProfile.LeftTrigger.HairTriggerMode = ChkHairTriggers.IsChecked == true;
        _activeProfile.RightTrigger.HairTriggerMode = ChkHairTriggers.IsChecked == true;
        _activeProfile.BaseLayer.LeftStickToWasd = ChkStickToWasd.IsChecked == true;
        PublishRuntimeProfile();

        if (hideChanged && _pollingLoop.IsRunning)
        {
            await ApplyHidHideAsync();
        }
    }

    private void TurboSetting_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressUiEvents || _activeProfile == null || ChkTurboSouth == null || SldTurboHz == null) return;
        UpdateSliderValueLabels();

        var existing = _activeProfile.TurboConfigs.FirstOrDefault(t => t.Button == ControllerButton.South);
        if (existing == null)
        {
            existing = new TurboButtonConfig { Button = ControllerButton.South };
            _activeProfile.TurboConfigs.Add(existing);
        }
        existing.Enabled = ChkTurboSouth.IsChecked == true;
        existing.FrequencyHz = SldTurboHz.Value;
        PublishRuntimeProfile();
    }

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
        BtnAiCalibrateDrift.Content = "✖ Cancelar calibración";
        BarCalibration.Value = 0;
        BarCalibration.Visibility = Visibility.Visible;
        TxtAiCalibrationResult.Text = $"Midiendo reposo durante {_driftRecorder.DurationSeconds:F0} s: no toques los sticks…";
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
        BtnAiCalibrateDrift.Content = "🎯 Auto-Calibrar Drift y Deadzone";
        BarCalibration.Visibility = Visibility.Collapsed;

        string message;
        switch (_driftRecorder.Status)
        {
            case DriftCalibrationStatus.Completed:
                var result = _driftRecorder.BuildResult(_aiAssistant)!;
                _activeProfile.LeftStick.InnerDeadzone = result.RecommendedLeftDeadzone;
                _activeProfile.RightStick.InnerDeadzone = result.RecommendedRightDeadzone;
                LoadProfileIntoUi(_activeProfile); // also publishes the snapshot to the loop
                message = result.DiagnosticSummary;
                break;
            case DriftCalibrationStatus.MovementDetected:
                message = "Se detectó movimiento en un stick: suéltalos y vuelve a calibrar. No se cambió el perfil.";
                break;
            default:
                message = "Calibración cancelada. No se cambió el perfil.";
                break;
        }

        TxtAiCalibrationResult.Text = message;
        TxtFooterStatus.Text = message;
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
        TxtAiCalibrationResult.Text = summary;
        TxtFooterStatus.Text = summary;
    }

    private static readonly MacroDefinition UiComboMacro = new()
    {
        Id = "ui-combo-abxy",
        Name = "Macro Combo A → B → X → Y",
        TriggerButton = ControllerButton.None, // fired only from the UI
        Steps =
        {
            new MacroStep { StepType = MacroStepType.TapButton, Button = ControllerButton.South, DurationMs = 90 },
            new MacroStep { StepType = MacroStepType.TapButton, Button = ControllerButton.East, DurationMs = 90 },
            new MacroStep { StepType = MacroStepType.TapButton, Button = ControllerButton.West, DurationMs = 90 },
            new MacroStep { StepType = MacroStepType.TapButton, Button = ControllerButton.North, DurationMs = 90 }
        }
    };

    private void BtnTriggerComboMacro_Click(object sender, RoutedEventArgs e)
    {
        if (!_pollingLoop.IsRunning)
        {
            TxtFooterStatus.Text = "Inicia la emulación para ejecutar la macro.";
            return;
        }

        // Goes through the pipeline: the 1000 Hz loop runs it frame by frame, together with the physical input
        _pipelineEngine.EnqueueMacro(UiComboMacro);
        TxtFooterStatus.Text = "Macro Combo enviada al pipeline: A → B → X → Y.";
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

        TxtFooterStatus.Text = _inputProvider.SupportsOutput
            ? "Prueba de vibración enviada al mando físico."
            : "El mando seleccionado no admite vibración (solo DualSense / DualShock 4 por HID nativo).";
    }

    private void OnPollingTelemetryUpdated(PollingTelemetry telemetry)
    {
        Dispatcher.BeginInvoke(() =>
        {
            TxtPollingHz.Text = $"{telemetry.ActualRateHz:F1} Hz";
            TxtLatencyJitter.Text = $"{telemetry.LastTickDurationMicroseconds:F1} µs / {telemetry.AverageJitterMilliseconds:F3} ms";
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
                ? $"{processName}.exe → [{matchedProfile.Name}]"
                : $"{processName}.exe";

            if (matchedProfile != null && matchedProfile.Id != _activeProfile.Id)
            {
                LstProfiles.SelectedItem = matchedProfile;
                TxtFooterStatus.Text = $"Auto-Switch activado por '{processName}.exe' → Perfil '{matchedProfile.Name}'";
            }
        });
    }

    private void OnMainWindowClosed(object? sender, EventArgs e)
    {
        _uiRefreshTimer.Stop();
        _driftRecorder.Cancel();
        _calibrationSamplerCts?.Cancel();
        _gameWatcher.Dispose();
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
