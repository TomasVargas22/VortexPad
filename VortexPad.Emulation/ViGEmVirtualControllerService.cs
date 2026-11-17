#pragma warning disable CS0618
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.DualShock4;
using Nefarius.ViGEm.Client.Targets.Xbox360;
using VortexPad.Core.Models;

namespace VortexPad.Emulation;

public sealed class ViGEmVirtualControllerService : IDisposable
{
    private ViGEmClient? _client;
    private IXbox360Controller? _xbox360;
    private IDualShock4Controller? _ds4;
    private VirtualControllerType? _activeType;
    private readonly object _sync = new();

    public bool IsDriverAvailable { get; private set; }
    public bool IsControllerConnected { get; private set; }
    public string StatusMessage { get; private set; } = "Desconectado";
    public ForceFeedbackState CurrentFeedback { get; private set; } = new();

    public event Action<ForceFeedbackState>? FeedbackReceived;

    public ViGEmVirtualControllerService()
    {
        // Lazy initialize when StartVirtualController is called so UI constructor never blocks on driver PnP
    }

    public bool TryInitializeClient()
    {
        lock (_sync)
        {
            if (_client != null) return true;
            try
            {
                _client = new ViGEmClient();
                IsDriverAvailable = true;
                StatusMessage = "ViGEmBus v1.22 listo";
                return true;
            }
            catch (Exception ex)
            {
                IsDriverAvailable = false;
                StatusMessage = $"ViGEmBus no disponible ({ex.GetType().Name})";
                return false;
            }
        }
    }

    public bool StartVirtualController(VirtualControllerType targetType)
    {
        lock (_sync)
        {
            StopVirtualController();

            if (targetType == VirtualControllerType.KeyboardMouse)
            {
                _activeType = targetType;
                IsControllerConnected = true;
                StatusMessage = "Modo Teclado/Ratón Nativo Activo";
                return true;
            }

            if (!TryInitializeClient() || _client == null)
            {
                StatusMessage = "Ejecutando en modo vista previa (sin ViGEmBus)";
                _activeType = targetType;
                return false;
            }

            try
            {
                if (targetType == VirtualControllerType.DualShock4)
                {
                    _ds4 = _client.CreateDualShock4Controller();
                    _ds4.AutoSubmitReport = false;
                    _ds4.FeedbackReceived += OnDs4FeedbackReceived;
                    _ds4.Connect();
                    _activeType = VirtualControllerType.DualShock4;
                    IsControllerConnected = true;
                    StatusMessage = "DualShock 4 Virtual Conectado (ViGEmBus)";
                }
                else
                {
                    _xbox360 = _client.CreateXbox360Controller();
                    _xbox360.AutoSubmitReport = false;
                    _xbox360.FeedbackReceived += OnXbox360FeedbackReceived;
                    _xbox360.Connect();
                    _activeType = VirtualControllerType.Xbox360;
                    IsControllerConnected = true;
                    StatusMessage = "Xbox 360 Virtual Conectado (ViGEmBus)";
                }
                return true;
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error al conectar mando virtual: {ex.Message}";
                StopVirtualController();
                return false;
            }
        }
    }

    public void StopVirtualController()
    {
        lock (_sync)
        {
            if (_xbox360 != null)
            {
                try
                {
                    _xbox360.FeedbackReceived -= OnXbox360FeedbackReceived;
                    _xbox360.Disconnect();
                }
                catch { }
                _xbox360 = null;
            }

            if (_ds4 != null)
            {
                try
                {
                    _ds4.FeedbackReceived -= OnDs4FeedbackReceived;
                    _ds4.Disconnect();
                }
                catch { }
                _ds4 = null;
            }

            IsControllerConnected = false;
            _activeType = null;
            CurrentFeedback = new ForceFeedbackState();
        }
    }

    public void SubmitState(VirtualOutputState state)
    {
        lock (_sync)
        {
            if (!IsControllerConnected) return;

            if (_xbox360 != null && (_activeType == VirtualControllerType.Xbox360 || _activeType == VirtualControllerType.HybridXboxKeyboard))
            {
                SubmitXbox360(state, _xbox360);
            }
            else if (_ds4 != null && _activeType == VirtualControllerType.DualShock4)
            {
                SubmitDualShock4(state, _ds4);
            }
        }
    }

    private static void SubmitXbox360(VirtualOutputState s, IXbox360Controller pad)
    {
        pad.SetButtonState(Xbox360Button.A, s.IsButtonActive(ControllerButton.South));
        pad.SetButtonState(Xbox360Button.B, s.IsButtonActive(ControllerButton.East));
        pad.SetButtonState(Xbox360Button.X, s.IsButtonActive(ControllerButton.West));
        pad.SetButtonState(Xbox360Button.Y, s.IsButtonActive(ControllerButton.North));
        pad.SetButtonState(Xbox360Button.LeftShoulder, s.IsButtonActive(ControllerButton.LeftShoulder));
        pad.SetButtonState(Xbox360Button.RightShoulder, s.IsButtonActive(ControllerButton.RightShoulder));
        pad.SetButtonState(Xbox360Button.Back, s.IsButtonActive(ControllerButton.Back));
        pad.SetButtonState(Xbox360Button.Start, s.IsButtonActive(ControllerButton.Start));
        pad.SetButtonState(Xbox360Button.Guide, s.IsButtonActive(ControllerButton.Guide));
        pad.SetButtonState(Xbox360Button.LeftThumb, s.IsButtonActive(ControllerButton.LeftThumb));
        pad.SetButtonState(Xbox360Button.RightThumb, s.IsButtonActive(ControllerButton.RightThumb));
        pad.SetButtonState(Xbox360Button.Up, s.IsButtonActive(ControllerButton.DPadUp));
        pad.SetButtonState(Xbox360Button.Down, s.IsButtonActive(ControllerButton.DPadDown));
        pad.SetButtonState(Xbox360Button.Left, s.IsButtonActive(ControllerButton.DPadLeft));
        pad.SetButtonState(Xbox360Button.Right, s.IsButtonActive(ControllerButton.DPadRight));

        pad.SetAxisValue(Xbox360Axis.LeftThumbX, ToSignedShort(s.LX));
        pad.SetAxisValue(Xbox360Axis.LeftThumbY, ToSignedShort(s.LY));
        pad.SetAxisValue(Xbox360Axis.RightThumbX, ToSignedShort(s.RX));
        pad.SetAxisValue(Xbox360Axis.RightThumbY, ToSignedShort(s.RY));

        pad.SetSliderValue(Xbox360Slider.LeftTrigger, ToByteTrigger(s.L2));
        pad.SetSliderValue(Xbox360Slider.RightTrigger, ToByteTrigger(s.R2));

        pad.SubmitReport();
    }

    private static void SubmitDualShock4(VirtualOutputState s, IDualShock4Controller pad)
    {
        pad.SetButtonState(DualShock4Button.Cross, s.IsButtonActive(ControllerButton.South));
        pad.SetButtonState(DualShock4Button.Circle, s.IsButtonActive(ControllerButton.East));
        pad.SetButtonState(DualShock4Button.Square, s.IsButtonActive(ControllerButton.West));
        pad.SetButtonState(DualShock4Button.Triangle, s.IsButtonActive(ControllerButton.North));
        pad.SetButtonState(DualShock4Button.ShoulderLeft, s.IsButtonActive(ControllerButton.LeftShoulder));
        pad.SetButtonState(DualShock4Button.ShoulderRight, s.IsButtonActive(ControllerButton.RightShoulder));
        pad.SetButtonState(DualShock4Button.Share, s.IsButtonActive(ControllerButton.Back));
        pad.SetButtonState(DualShock4Button.Options, s.IsButtonActive(ControllerButton.Start));
        pad.SetButtonState(DualShock4Button.ThumbLeft, s.IsButtonActive(ControllerButton.LeftThumb));
        pad.SetButtonState(DualShock4Button.ThumbRight, s.IsButtonActive(ControllerButton.RightThumb));
        pad.SetButtonState(DualShock4SpecialButton.Ps, s.IsButtonActive(ControllerButton.Guide));
        pad.SetButtonState(DualShock4SpecialButton.Touchpad, s.IsButtonActive(ControllerButton.TouchpadClick));

        // D-Pad direction
        pad.SetDPadDirection(ResolveDs4DPad(
            s.IsButtonActive(ControllerButton.DPadUp),
            s.IsButtonActive(ControllerButton.DPadDown),
            s.IsButtonActive(ControllerButton.DPadLeft),
            s.IsButtonActive(ControllerButton.DPadRight)));

        // Note: DS4 Y-axis in ViGEm uses 0=up, 255=down, 128=center
        pad.SetAxisValue(DualShock4Axis.LeftThumbX, ToByteAxis(s.LX));
        pad.SetAxisValue(DualShock4Axis.LeftThumbY, ToByteAxis(-s.LY));
        pad.SetAxisValue(DualShock4Axis.RightThumbX, ToByteAxis(s.RX));
        pad.SetAxisValue(DualShock4Axis.RightThumbY, ToByteAxis(-s.RY));

        pad.SetSliderValue(DualShock4Slider.LeftTrigger, ToByteTrigger(s.L2));
        pad.SetSliderValue(DualShock4Slider.RightTrigger, ToByteTrigger(s.R2));

        pad.SubmitReport();
    }

    private static DualShock4DPadDirection ResolveDs4DPad(bool up, bool down, bool left, bool right)
    {
        if (up && left) return DualShock4DPadDirection.Northwest;
        if (up && right) return DualShock4DPadDirection.Northeast;
        if (down && left) return DualShock4DPadDirection.Southwest;
        if (down && right) return DualShock4DPadDirection.Southeast;
        if (up) return DualShock4DPadDirection.North;
        if (down) return DualShock4DPadDirection.South;
        if (left) return DualShock4DPadDirection.West;
        if (right) return DualShock4DPadDirection.East;
        return DualShock4DPadDirection.None;
    }

    private static short ToSignedShort(double normalized)
    {
        double clamped = Math.Clamp(normalized, -1.0, 1.0);
        return clamped >= 0
            ? (short)Math.Round(clamped * short.MaxValue)
            : (short)Math.Round(-clamped * short.MinValue);
    }

    private static byte ToByteAxis(double normalized)
    {
        double clamped = Math.Clamp(normalized, -1.0, 1.0);
        return (byte)Math.Clamp(Math.Round(((clamped + 1.0) * 0.5) * 255.0), 0, 255);
    }

    private static byte ToByteTrigger(double normalized)
    {
        return (byte)Math.Clamp(Math.Round(Math.Clamp(normalized, 0.0, 1.0) * 255.0), 0, 255);
    }

    private void OnXbox360FeedbackReceived(object sender, Xbox360FeedbackReceivedEventArgs e)
    {
        var state = new ForceFeedbackState
        {
            LargeMotor = e.LargeMotor / 255.0,
            SmallMotor = e.SmallMotor / 255.0
        };
        CurrentFeedback = state;
        FeedbackReceived?.Invoke(state);
    }

    private void OnDs4FeedbackReceived(object sender, DualShock4FeedbackReceivedEventArgs e)
    {
        var state = new ForceFeedbackState
        {
            LargeMotor = e.LargeMotor / 255.0,
            SmallMotor = e.SmallMotor / 255.0,
            LedR = e.LightbarColor.Red,
            LedG = e.LightbarColor.Green,
            LedB = e.LightbarColor.Blue,
            HasLightbar = true
        };
        CurrentFeedback = state;
        FeedbackReceived?.Invoke(state);
    }

    public void Dispose()
    {
        lock (_sync)
        {
            StopVirtualController();
            try { _client?.Dispose(); } catch { }
            _client = null;
        }
    }
}
