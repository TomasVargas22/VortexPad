using System.Buffers.Binary;
using VortexPad.Core.Models;

namespace VortexPad.Core.Hid;

/// <summary>Physical link of a native HID controller.</summary>
public enum HidConnection
{
    Unknown,
    Usb,
    Bluetooth
}

/// <summary>
/// IMU calibration shared by Sony controllers (DualSense and DualShock 4 use the same math,
/// only the feature report layout changes). Converts raw int16 samples into deg/s and g.
/// </summary>
public abstract class ImuCalibration
{
    // Nominal sensitivities used when the controller does not provide calibration:
    // ±2000 deg/s and ±4 g over the int16 range.
    protected const double NominalGyroLsbPerDegSec = 16.384;
    protected const double NominalAccelLsbPerG = 8192.0;

    private readonly double[] _gyroBias = new double[3];
    private readonly double[] _gyroScale = new double[3];   // deg/s per LSB
    private readonly double[] _accelBias = new double[3];
    private readonly double[] _accelScale = new double[3];  // g per LSB

    /// <summary>true if the values come from the controller's feature report; false for nominal ones.</summary>
    public bool IsFromDevice { get; protected init; }

    protected ImuCalibration()
    {
        for (int i = 0; i < 3; i++)
        {
            _gyroScale[i] = 1.0 / NominalGyroLsbPerDegSec;
            _accelScale[i] = 1.0 / NominalAccelLsbPerG;
        }
    }

    /// <summary>
    /// Fills the calibration from raw values (axis order: pitch, yaw, roll / X, Y, Z).
    /// Invalid axes (zero range) keep the nominal values.
    /// </summary>
    protected void SetFromRaw(
        ReadOnlySpan<short> gyroBias, ReadOnlySpan<short> gyroPlus, ReadOnlySpan<short> gyroMinus, int gyroSpeed2x,
        ReadOnlySpan<short> accelPlus, ReadOnlySpan<short> accelMinus)
    {
        for (int i = 0; i < 3; i++)
        {
            int denom = Math.Abs(gyroPlus[i] - gyroBias[i]) + Math.Abs(gyroMinus[i] - gyroBias[i]);
            if (denom > 0 && gyroSpeed2x > 0)
            {
                _gyroBias[i] = gyroBias[i];
                _gyroScale[i] = (double)gyroSpeed2x / denom;
            }

            int range2g = accelPlus[i] - accelMinus[i];
            if (range2g > 0)
            {
                _accelBias[i] = accelPlus[i] - (range2g / 2.0);
                _accelScale[i] = 2.0 / range2g;
            }
        }
    }

    public double GyroToDegSec(int axis, short raw) => (raw - _gyroBias[axis]) * _gyroScale[axis];
    public double AccelToG(int axis, short raw) => (raw - _accelBias[axis]) * _accelScale[axis];
}

/// <summary>Decoding helpers shared by the Sony report parsers.</summary>
internal static class SonyHidCommon
{
    public const int SonyVendorId = 0x054C;

    public static short ReadInt16(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadInt16LittleEndian(data.Slice(offset, 2));

    /// <summary>Sticks: 0..255, center 128, Y grows downward → the caller inverts Y for cartesian up = +1.</summary>
    public static double NormalizeStick(byte raw)
    {
        // 128 → 0.0 exactly; 0 → -1.0; 255 → +1.0
        int centered = raw - 128;
        return centered >= 0 ? centered / 127.0 : centered / 128.0;
    }

    /// <summary>
    /// Decodes the three button bytes common to DualSense and DualShock 4:
    /// b0 = hat (low nibble) + face buttons, b1 = shoulders / Share / Options / L3 / R3, b2 bit 0 = PS, bit 1 = touchpad.
    /// Clears <paramref name="buttons"/> first.
    /// </summary>
    public static void DecodeButtons(byte b0, byte b1, byte b2, HashSet<ControllerButton> buttons)
    {
        buttons.Clear();

        AddDPad(b0 & 0x0F, buttons);
        if ((b0 & 0x10) != 0) buttons.Add(ControllerButton.West);   // Square
        if ((b0 & 0x20) != 0) buttons.Add(ControllerButton.South);  // Cross
        if ((b0 & 0x40) != 0) buttons.Add(ControllerButton.East);   // Circle
        if ((b0 & 0x80) != 0) buttons.Add(ControllerButton.North);  // Triangle

        if ((b1 & 0x01) != 0) buttons.Add(ControllerButton.LeftShoulder);  // L1
        if ((b1 & 0x02) != 0) buttons.Add(ControllerButton.RightShoulder); // R1
        // 0x04 / 0x08 = digital L2 / R2 (already covered by the analog value)
        if ((b1 & 0x10) != 0) buttons.Add(ControllerButton.Back);   // Share / Create
        if ((b1 & 0x20) != 0) buttons.Add(ControllerButton.Start);  // Options
        if ((b1 & 0x40) != 0) buttons.Add(ControllerButton.LeftThumb);  // L3
        if ((b1 & 0x80) != 0) buttons.Add(ControllerButton.RightThumb); // R3

        if ((b2 & 0x01) != 0) buttons.Add(ControllerButton.Guide);         // PS
        if ((b2 & 0x02) != 0) buttons.Add(ControllerButton.TouchpadClick);
    }

    private static void AddDPad(int hat, HashSet<ControllerButton> buttons)
    {
        // 0 = N, 1 = NE, 2 = E, 3 = SE, 4 = S, 5 = SW, 6 = W, 7 = NW, 8 = centered
        switch (hat)
        {
            case 0: buttons.Add(ControllerButton.DPadUp); break;
            case 1: buttons.Add(ControllerButton.DPadUp); buttons.Add(ControllerButton.DPadRight); break;
            case 2: buttons.Add(ControllerButton.DPadRight); break;
            case 3: buttons.Add(ControllerButton.DPadDown); buttons.Add(ControllerButton.DPadRight); break;
            case 4: buttons.Add(ControllerButton.DPadDown); break;
            case 5: buttons.Add(ControllerButton.DPadDown); buttons.Add(ControllerButton.DPadLeft); break;
            case 6: buttons.Add(ControllerButton.DPadLeft); break;
            case 7: buttons.Add(ControllerButton.DPadUp); buttons.Add(ControllerButton.DPadLeft); break;
        }
    }
}
