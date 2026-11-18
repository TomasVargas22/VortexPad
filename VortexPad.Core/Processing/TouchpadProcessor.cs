using VortexPad.Core.Models;
using VortexPad.Core.Profiles;

namespace VortexPad.Core.Processing;

/// <summary>Result of processing the touchpad in one frame.</summary>
public readonly record struct TouchpadFrame(
    double MouseDx,
    double MouseDy,
    double Wheel,
    bool MouseLeft,
    bool MouseRight,
    ControllerButton ZoneButton,
    bool ConsumeTouchpadClick);

/// <summary>
/// Turns the DualSense touchpad into mouse and scroll, and splits the click into zones.
/// - One finger: relative movement (no jump when the finger is put down: the first frame of each contact does not move).
/// - Two fingers: vertical scroll with the average height.
/// - Click: left half / right half → mouse buttons or the profile's buttons.
/// No allocations: only scalar state.
/// </summary>
public sealed class TouchpadProcessor
{
    // Pixels for a full touchpad width with sensitivity 1.0
    private const double PixelsPerWidth = 1000.0;
    // Touchpad aspect ratio (1920×1080): Y moves proportionally to X
    private const double HeightToWidth = 1080.0 / 1920.0;
    // Wheel units (120 = one notch) for a full height with sensitivity 1.0 → 10 notches
    private const double WheelPerHeight = 1200.0;

    private bool _tracking;
    private byte _trackedId;
    private double _lastX, _lastY;

    private bool _scrolling;
    private double _lastScrollY;

    public void Reset()
    {
        _tracking = false;
        _scrolling = false;
    }

    public TouchpadFrame Process(PhysicalInputState input, TouchpadConfiguration? config)
    {
        if (config == null)
        {
            Reset();
            return default;
        }

        bool mouseMode = config.Mode == TouchpadMode.Mouse;
        double dx = 0, dy = 0, wheel = 0;

        if (mouseMode)
        {
            var t1 = input.Touch1;
            var t2 = input.Touch2;

            if (t1.Active && t2.Active && config.TwoFingerScroll)
            {
                double avgY = (t1.Y + t2.Y) * 0.5;
                if (_scrolling)
                {
                    // Fingers up (Y decreasing) → positive wheel (scroll up), as on a laptop touchpad
                    double sign = config.InvertScroll ? 1.0 : -1.0;
                    wheel = sign * (avgY - _lastScrollY) * WheelPerHeight * Math.Clamp(config.ScrollSensitivity, 0.1, 10.0);
                }
                _scrolling = true;
                _lastScrollY = avgY;
                _tracking = false;
            }
            else if (t1.Active || t2.Active)
            {
                var finger = t1.Active ? t1 : t2;
                if (_tracking && finger.Id == _trackedId)
                {
                    double sens = Math.Clamp(config.MouseSensitivity, 0.1, 10.0);
                    dx = (finger.X - _lastX) * PixelsPerWidth * sens;
                    dy = (finger.Y - _lastY) * PixelsPerWidth * HeightToWidth * sens;
                }
                _tracking = true;
                _trackedId = finger.Id;
                _lastX = finger.X;
                _lastY = finger.Y;
                _scrolling = false;
            }
            else
            {
                Reset();
            }
        }
        else
        {
            Reset();
        }

        // Click: zone according to the position of the first finger on the touchpad
        bool mouseLeft = false, mouseRight = false, consume = false;
        ControllerButton zoneButton = ControllerButton.None;

        if (input.IsPressed(ControllerButton.TouchpadClick))
        {
            double x = input.Touch1.Active ? input.Touch1.X : input.Touch2.Active ? input.Touch2.X : 0.5;
            bool leftHalf = x < 0.5;

            if (mouseMode && config.ClickAsMouseButtons)
            {
                mouseLeft = leftHalf;
                mouseRight = !leftHalf;
                consume = true;
            }
            else if (config.SplitClickZones)
            {
                zoneButton = leftHalf ? config.LeftZoneButton : config.RightZoneButton;
                consume = true;
            }
        }

        return new TouchpadFrame(dx, dy, wheel, mouseLeft, mouseRight, zoneButton, consume);
    }
}
