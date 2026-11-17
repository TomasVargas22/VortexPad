using System.Buffers.Binary;
using VortexPad.Core.Models;

namespace VortexPad.Core.Hid;

/// <summary>
/// IMU calibration of a DualSense, read from feature report 0x05.
/// Converts raw int16 IMU samples into deg/s (gyro) and g (accelerometer).
/// </summary>
public sealed class DualSenseCalibration
{
    public const byte FeatureReportId = 0x05;
    public const int FeatureReportMinLength = 35;

    // Nominal sensitivities used when the controller does not provide calibration:
    // ±2000 deg/s and ±4 g over the int16 range.
    private const double NominalGyroLsbPerDegSec = 16.384;
    private const double NominalAccelLsbPerG = 8192.0;

    private readonly double[] _gyroBias = new double[3];
    private readonly double[] _gyroScale = new double[3];   // deg/s per LSB
    private readonly double[] _accelBias = new double[3];
    private readonly double[] _accelScale = new double[3];  // g per LSB

    /// <summary>true if the values come from the controller's feature report; false for nominal ones.</summary>
    public bool IsFromDevice { get; private init; }

    public static DualSenseCalibration Nominal { get; } = CreateNominal();

    private static DualSenseCalibration CreateNominal()
    {
        var cal = new DualSenseCalibration { IsFromDevice = false };
        for (int i = 0; i < 3; i++)
        {
            cal._gyroScale[i] = 1.0 / NominalGyroLsbPerDegSec;
            cal._accelScale[i] = 1.0 / NominalAccelLsbPerG;
        }
        return cal;
    }

    /// <summary>
    /// Parses feature report 0x05 (byte 0 = report ID). Same layout for USB and Bluetooth.
    /// Invalid axes (zero range) fall back to nominal values.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<byte> report, out DualSenseCalibration calibration)
    {
        calibration = Nominal;
        if (report.Length < FeatureReportMinLength || report[0] != FeatureReportId)
        {
            return false;
        }

        // Gyro order in the report: pitch, yaw, roll (matches input report axes 0, 1, 2)
        short[] bias = { S(report, 1), S(report, 3), S(report, 5) };
        short[] plus = { S(report, 7), S(report, 11), S(report, 15) };
        short[] minus = { S(report, 9), S(report, 13), S(report, 17) };
        int speed2x = S(report, 19) + S(report, 21);

        // Accelerometer: X, Y, Z
        short[] accPlus = { S(report, 23), S(report, 27), S(report, 31) };
        short[] accMinus = { S(report, 25), S(report, 29), S(report, 33) };

        var cal = new DualSenseCalibration { IsFromDevice = true };
        for (int i = 0; i < 3; i++)
        {
            int denom = Math.Abs(plus[i] - bias[i]) + Math.Abs(minus[i] - bias[i]);
            if (denom > 0 && speed2x > 0)
            {
                cal._gyroBias[i] = bias[i];
                cal._gyroScale[i] = (double)speed2x / denom;
            }
            else
            {
                cal._gyroBias[i] = 0;
                cal._gyroScale[i] = 1.0 / NominalGyroLsbPerDegSec;
            }

            int range2g = accPlus[i] - accMinus[i];
            if (range2g > 0)
            {
                cal._accelBias[i] = accPlus[i] - (range2g / 2.0);
                cal._accelScale[i] = 2.0 / range2g;
            }
            else
            {
                cal._accelBias[i] = 0;
                cal._accelScale[i] = 1.0 / NominalAccelLsbPerG;
            }
        }

        calibration = cal;
        return true;
    }

    private static short S(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadInt16LittleEndian(data.Slice(offset, 2));

    public double GyroToDegSec(int axis, short raw) => (raw - _gyroBias[axis]) * _gyroScale[axis];
    public double AccelToG(int axis, short raw) => (raw - _accelBias[axis]) * _accelScale[axis];
}

public enum DualSenseConnection
{
    Unknown,
    Usb,
    Bluetooth
}

/// <summary>
/// Parses DualSense / DualSense Edge HID input reports into <see cref="PhysicalInputState"/>.
/// USB: report 0x01 (64 bytes, data at offset 1).
/// Bluetooth: report 0x31 (78 bytes, data at offset 2). BT only sends 0x31 after feature report 0x05 has been read.
/// </summary>
public static class DualSenseReportParser
{
    public const int SonyVendorId = 0x054C;
    public const int DualSenseProductId = 0x0CE6;
    public const int DualSenseEdgeProductId = 0x0DF2;

    public const byte UsbInputReportId = 0x01;
    public const byte BluetoothInputReportId = 0x31;

    // Offsets relative to the start of the report data (after report ID / BT header)
    private const int OffLX = 0, OffLY = 1, OffRX = 2, OffRY = 3, OffL2 = 4, OffR2 = 5;
    private const int OffButtons0 = 7, OffButtons1 = 8, OffButtons2 = 9;
    private const int OffGyro = 15, OffAccel = 21;
    private const int MinDataLength = OffAccel + 6; // up to and including the accelerometer

    public static bool IsDualSense(int vendorId, int productId) =>
        vendorId == SonyVendorId && productId is DualSenseProductId or DualSenseEdgeProductId;

    /// <summary>
    /// Parses a full input report (byte 0 = report ID). Returns false for reports in an unknown format
    /// or that are too short, e.g. the reduced BT 0x01 report sent before calibration is requested.
    /// Only overwrites the axes, buttons and IMU of <paramref name="state"/>.
    /// </summary>
    public static bool TryParseInputReport(
        ReadOnlySpan<byte> report,
        DualSenseCalibration calibration,
        PhysicalInputState state,
        out DualSenseConnection connection)
    {
        connection = DualSenseConnection.Unknown;
        if (report.Length < 2) return false;

        int offset;
        if (report[0] == UsbInputReportId && report.Length >= 1 + MinDataLength)
        {
            offset = 1;
            connection = DualSenseConnection.Usb;
        }
        else if (report[0] == BluetoothInputReportId && report.Length >= 2 + MinDataLength)
        {
            offset = 2;
            connection = DualSenseConnection.Bluetooth;
        }
        else
        {
            return false;
        }

        ReadOnlySpan<byte> d = report[offset..];

        // Sticks: 0..255, center 128, Y grows downward → invert for cartesian up = +1
        state.LX = NormalizeStick(d[OffLX]);
        state.LY = -NormalizeStick(d[OffLY]);
        state.RX = NormalizeStick(d[OffRX]);
        state.RY = -NormalizeStick(d[OffRY]);
        state.L2 = d[OffL2] / 255.0;
        state.R2 = d[OffR2] / 255.0;

        var buttons = state.PressedButtons;
        buttons.Clear();

        byte b0 = d[OffButtons0];
        byte b1 = d[OffButtons1];
        byte b2 = d[OffButtons2];

        AddDPad(b0 & 0x0F, buttons);
        if ((b0 & 0x10) != 0) buttons.Add(ControllerButton.West);   // Square
        if ((b0 & 0x20) != 0) buttons.Add(ControllerButton.South);  // Cross
        if ((b0 & 0x40) != 0) buttons.Add(ControllerButton.East);   // Circle
        if ((b0 & 0x80) != 0) buttons.Add(ControllerButton.North);  // Triangle

        if ((b1 & 0x01) != 0) buttons.Add(ControllerButton.LeftShoulder);  // L1
        if ((b1 & 0x02) != 0) buttons.Add(ControllerButton.RightShoulder); // R1
        // 0x04 / 0x08 = digital L2 / R2 (already covered by the analog value)
        if ((b1 & 0x10) != 0) buttons.Add(ControllerButton.Back);   // Create
        if ((b1 & 0x20) != 0) buttons.Add(ControllerButton.Start);  // Options
        if ((b1 & 0x40) != 0) buttons.Add(ControllerButton.LeftThumb);  // L3
        if ((b1 & 0x80) != 0) buttons.Add(ControllerButton.RightThumb); // R3

        if ((b2 & 0x01) != 0) buttons.Add(ControllerButton.Guide);         // PS
        if ((b2 & 0x02) != 0) buttons.Add(ControllerButton.TouchpadClick);
        // 0x04 = Mute (no equivalent in ControllerButton)
        // DualSense Edge: 0x40 / 0x80 = left / right back paddle
        if ((b2 & 0x40) != 0) buttons.Add(ControllerButton.Paddle1);
        if ((b2 & 0x80) != 0) buttons.Add(ControllerButton.Paddle2);

        // IMU: gyro (pitch, yaw, roll) and accelerometer (X, Y, Z), little-endian int16
        state.GyroPitch = calibration.GyroToDegSec(0, ReadInt16(d, OffGyro));
        state.GyroYaw = calibration.GyroToDegSec(1, ReadInt16(d, OffGyro + 2));
        state.GyroRoll = calibration.GyroToDegSec(2, ReadInt16(d, OffGyro + 4));
        state.AccelX = calibration.AccelToG(0, ReadInt16(d, OffAccel));
        state.AccelY = calibration.AccelToG(1, ReadInt16(d, OffAccel + 2));
        state.AccelZ = calibration.AccelToG(2, ReadInt16(d, OffAccel + 4));

        return true;
    }

    private static short ReadInt16(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadInt16LittleEndian(data.Slice(offset, 2));

    private static double NormalizeStick(byte raw)
    {
        // 128 → 0.0 exactly; 0 → -1.0; 255 → +1.0
        int centered = raw - 128;
        return centered >= 0 ? centered / 127.0 : centered / 128.0;
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
