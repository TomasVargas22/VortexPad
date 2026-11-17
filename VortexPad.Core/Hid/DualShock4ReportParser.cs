using VortexPad.Core.Models;
using static VortexPad.Core.Hid.SonyHidCommon;

namespace VortexPad.Core.Hid;

/// <summary>
/// IMU calibration of a DualShock 4.
/// USB: feature report 0x02 (37 bytes). Bluetooth: feature report 0x05 (41 bytes, with CRC).
/// Both share the bias / speed / accelerometer offsets, but the gyro plus/minus values are ordered differently:
/// USB interleaves them (pitch+, pitch-, yaw+, yaw-, roll+, roll-) and Bluetooth groups them
/// (pitch+, yaw+, roll+, pitch-, yaw-, roll-).
/// </summary>
public sealed class DualShock4Calibration : ImuCalibration
{
    public const byte UsbFeatureReportId = 0x02;
    public const byte BluetoothFeatureReportId = 0x05;
    public const int FeatureReportMinLength = 35;

    public static DualShock4Calibration Nominal { get; } = new() { IsFromDevice = false };

    /// <summary>
    /// Parses feature report 0x02 or 0x05 (byte 0 = report ID).
    /// The gyro layout is detected from the signs of the plus/minus values (plus &gt; 0, minus &lt; 0);
    /// if it is ambiguous, the report ID decides (0x02 → interleaved, 0x05 → grouped).
    /// This also covers the wireless adapter, which answers 0x02 over USB with the Bluetooth layout.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<byte> report, out DualShock4Calibration calibration)
    {
        calibration = Nominal;
        if (report.Length < FeatureReportMinLength ||
            (report[0] != UsbFeatureReportId && report[0] != BluetoothFeatureReportId))
        {
            return false;
        }

        ReadOnlySpan<short> bias = stackalloc short[] { ReadInt16(report, 1), ReadInt16(report, 3), ReadInt16(report, 5) };

        bool interleaved = DetectInterleavedLayout(report) ?? report[0] == UsbFeatureReportId;
        Span<short> plus = stackalloc short[3];
        Span<short> minus = stackalloc short[3];
        for (int i = 0; i < 3; i++)
        {
            if (interleaved)
            {
                plus[i] = ReadInt16(report, 7 + (i * 4));
                minus[i] = ReadInt16(report, 9 + (i * 4));
            }
            else
            {
                plus[i] = ReadInt16(report, 7 + (i * 2));
                minus[i] = ReadInt16(report, 13 + (i * 2));
            }
        }

        int speed2x = ReadInt16(report, 19) + ReadInt16(report, 21);

        ReadOnlySpan<short> accPlus = stackalloc short[] { ReadInt16(report, 23), ReadInt16(report, 27), ReadInt16(report, 31) };
        ReadOnlySpan<short> accMinus = stackalloc short[] { ReadInt16(report, 25), ReadInt16(report, 29), ReadInt16(report, 33) };

        var cal = new DualShock4Calibration { IsFromDevice = true };
        cal.SetFromRaw(bias, plus, minus, speed2x, accPlus, accMinus);
        calibration = cal;
        return true;
    }

    /// <summary>true = interleaved (USB), false = grouped (BT), null = cannot tell from the signs.</summary>
    private static bool? DetectInterleavedLayout(ReadOnlySpan<byte> report)
    {
        short v7 = ReadInt16(report, 7), v9 = ReadInt16(report, 9), v11 = ReadInt16(report, 11);
        short v13 = ReadInt16(report, 13), v15 = ReadInt16(report, 15), v17 = ReadInt16(report, 17);

        bool looksInterleaved = v7 > 0 && v9 < 0 && v11 > 0 && v13 < 0 && v15 > 0 && v17 < 0;
        bool looksGrouped = v7 > 0 && v9 > 0 && v11 > 0 && v13 < 0 && v15 < 0 && v17 < 0;
        if (looksInterleaved == looksGrouped) return null;
        return looksInterleaved;
    }
}

/// <summary>
/// Parses DualShock 4 HID input reports into <see cref="PhysicalInputState"/>.
/// USB / wireless adapter: report 0x01 (64 bytes, data at offset 1).
/// Bluetooth: report 0x11 (78 bytes, data at offset 3). BT only sends 0x11 after a calibration feature report has been read;
/// until then it sends a reduced 0x01 report without IMU, which is rejected.
/// </summary>
public static class DualShock4ReportParser
{
    public const int SonyVendorId = SonyHidCommon.SonyVendorId;
    public const int DualShock4V1ProductId = 0x05C4;
    public const int DualShock4V2ProductId = 0x09CC;
    public const int WirelessAdapterProductId = 0x0BA0;

    public const byte UsbInputReportId = 0x01;
    public const byte BluetoothInputReportId = 0x11;

    // Offsets relative to the start of the report data (after report ID / BT header)
    private const int OffLX = 0, OffLY = 1, OffRX = 2, OffRY = 3;
    private const int OffButtons0 = 4, OffButtons1 = 5, OffButtons2 = 6;
    private const int OffL2 = 7, OffR2 = 8;
    private const int OffGyro = 12, OffAccel = 18;
    private const int MinDataLength = OffAccel + 6; // up to and including the accelerometer

    public static bool IsDualShock4(int vendorId, int productId) =>
        vendorId == SonyVendorId &&
        productId is DualShock4V1ProductId or DualShock4V2ProductId or WirelessAdapterProductId;

    /// <summary>
    /// Parses a full input report (byte 0 = report ID). Returns false for unknown or too short reports.
    /// Only overwrites the axes, buttons and IMU of <paramref name="state"/>.
    /// </summary>
    public static bool TryParseInputReport(
        ReadOnlySpan<byte> report,
        DualShock4Calibration calibration,
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
        else if (report[0] == BluetoothInputReportId && report.Length >= 3 + MinDataLength)
        {
            offset = 3;
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

        // b2: bits 2..7 are a frame counter, only PS and touchpad click are buttons
        DecodeButtons(d[OffButtons0], d[OffButtons1], d[OffButtons2], state.PressedButtons);

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
