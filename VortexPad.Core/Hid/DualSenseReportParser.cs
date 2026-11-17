using VortexPad.Core.Models;
using static VortexPad.Core.Hid.SonyHidCommon;

namespace VortexPad.Core.Hid;

/// <summary>
/// IMU calibration of a DualSense, read from feature report 0x05.
/// Converts raw int16 IMU samples into deg/s (gyro) and g (accelerometer).
/// </summary>
public sealed class DualSenseCalibration : ImuCalibration
{
    public const byte FeatureReportId = 0x05;
    public const int FeatureReportMinLength = 35;

    public static DualSenseCalibration Nominal { get; } = new() { IsFromDevice = false };

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
        ReadOnlySpan<short> bias = stackalloc short[] { ReadInt16(report, 1), ReadInt16(report, 3), ReadInt16(report, 5) };
        ReadOnlySpan<short> plus = stackalloc short[] { ReadInt16(report, 7), ReadInt16(report, 11), ReadInt16(report, 15) };
        ReadOnlySpan<short> minus = stackalloc short[] { ReadInt16(report, 9), ReadInt16(report, 13), ReadInt16(report, 17) };
        int speed2x = ReadInt16(report, 19) + ReadInt16(report, 21);

        // Accelerometer: X, Y, Z
        ReadOnlySpan<short> accPlus = stackalloc short[] { ReadInt16(report, 23), ReadInt16(report, 27), ReadInt16(report, 31) };
        ReadOnlySpan<short> accMinus = stackalloc short[] { ReadInt16(report, 25), ReadInt16(report, 29), ReadInt16(report, 33) };

        var cal = new DualSenseCalibration { IsFromDevice = true };
        cal.SetFromRaw(bias, plus, minus, speed2x, accPlus, accMinus);
        calibration = cal;
        return true;
    }
}

/// <summary>
/// Parses DualSense / DualSense Edge HID input reports into <see cref="PhysicalInputState"/>.
/// USB: report 0x01 (64 bytes, data at offset 1).
/// Bluetooth: report 0x31 (78 bytes, data at offset 2). BT only sends 0x31 after feature report 0x05 has been read.
/// </summary>
public static class DualSenseReportParser
{
    public const int SonyVendorId = SonyHidCommon.SonyVendorId;
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
        out HidConnection connection)
    {
        connection = HidConnection.Unknown;
        if (report.Length < 2) return false;

        int offset;
        if (report[0] == UsbInputReportId && report.Length >= 1 + MinDataLength)
        {
            offset = 1;
            connection = HidConnection.Usb;
        }
        else if (report[0] == BluetoothInputReportId && report.Length >= 2 + MinDataLength)
        {
            offset = 2;
            connection = HidConnection.Bluetooth;
        }
        else
        {
            return false;
        }

        ReadOnlySpan<byte> d = report[offset..];

        state.LX = NormalizeStick(d[OffLX]);
        state.LY = -NormalizeStick(d[OffLY]);
        state.RX = NormalizeStick(d[OffRX]);
        state.RY = -NormalizeStick(d[OffRY]);
        state.L2 = d[OffL2] / 255.0;
        state.R2 = d[OffR2] / 255.0;

        byte b2 = d[OffButtons2];
        var buttons = state.PressedButtons;
        DecodeButtons(d[OffButtons0], d[OffButtons1], b2, buttons);
        // b2: 0x04 = Mute (no equivalent in ControllerButton)
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
}
