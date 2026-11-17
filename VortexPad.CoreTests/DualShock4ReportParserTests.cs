using System.Buffers.Binary;
using VortexPad.Core.Hid;
using VortexPad.Core.Models;
using Xunit;

namespace VortexPad.CoreTests;

public class DualShock4ReportParserTests
{
    // Builds a report with the given data placed after the header (USB: 0x01; BT: 0x11 + 2 header bytes).
    private static byte[] BuildReport(bool bluetooth, Action<byte[], int> fill)
    {
        var report = new byte[bluetooth ? 78 : 64];
        int offset = bluetooth ? 3 : 1;
        report[0] = bluetooth ? DualShock4ReportParser.BluetoothInputReportId : DualShock4ReportParser.UsbInputReportId;

        // Neutral state: centered sticks, released triggers, D-pad centered (hat = 8)
        report[offset + 0] = 128;
        report[offset + 1] = 128;
        report[offset + 2] = 128;
        report[offset + 3] = 128;
        report[offset + 4] = 0x08;

        fill(report, offset);
        return report;
    }

    private static void WriteInt16(byte[] buf, int index, short value) =>
        BinaryPrimitives.WriteInt16LittleEndian(buf.AsSpan(index, 2), value);

    [Fact]
    public void UsbReport_MapsSticksTriggersAndButtons()
    {
        byte[] report = BuildReport(bluetooth: false, (r, o) =>
        {
            r[o + 0] = 255;                 // LX full right
            r[o + 1] = 0;                   // LY full up (raw 0 = up)
            r[o + 2] = 0;                   // RX full left
            r[o + 3] = 255;                 // RY full down
            r[o + 4] = 0x08 | 0x20 | 0x80;  // Cross + Triangle, D-pad centered
            r[o + 5] = 0x01 | 0x10 | 0x80;  // L1 + Share + R3
            r[o + 6] = 0x01 | 0x02;         // PS + Touchpad
            r[o + 7] = 255;                 // L2 full
            r[o + 8] = 51;                  // R2 20%
        });

        var state = new PhysicalInputState();
        bool ok = DualShock4ReportParser.TryParseInputReport(report, DualShock4Calibration.Nominal, state, out var conn);

        Assert.True(ok);
        Assert.Equal(HidConnection.Usb, conn);
        Assert.Equal(1.0, state.LX, 6);
        Assert.Equal(1.0, state.LY, 6);
        Assert.Equal(-1.0, state.RX, 6);
        Assert.Equal(-1.0, state.RY, 6);
        Assert.Equal(1.0, state.L2, 6);
        Assert.Equal(0.2, state.R2, 6);

        Assert.Equal(
            new[] { ControllerButton.South, ControllerButton.North, ControllerButton.LeftShoulder, ControllerButton.Back,
                    ControllerButton.RightThumb, ControllerButton.Guide, ControllerButton.TouchpadClick }.OrderBy(b => b),
            state.PressedButtons.OrderBy(b => b));
    }

    [Fact]
    public void CenteredSticks_ProduceExactZero_AndFrameCounterIsNotAButton()
    {
        byte[] report = BuildReport(bluetooth: false, (r, o) => r[o + 6] = 0xFC); // counter bits only
        var state = new PhysicalInputState();

        Assert.True(DualShock4ReportParser.TryParseInputReport(report, DualShock4Calibration.Nominal, state, out _));
        Assert.Equal(0.0, state.LX);
        Assert.Equal(0.0, state.LY);
        Assert.Equal(0.0, state.RX);
        Assert.Equal(0.0, state.RY);
        Assert.Empty(state.PressedButtons);
    }

    [Theory]
    [InlineData(0, new[] { ControllerButton.DPadUp })]
    [InlineData(3, new[] { ControllerButton.DPadDown, ControllerButton.DPadRight })]
    [InlineData(6, new[] { ControllerButton.DPadLeft })]
    [InlineData(7, new[] { ControllerButton.DPadUp, ControllerButton.DPadLeft })]
    [InlineData(8, new ControllerButton[0])]
    public void DPadHat_DecodesDirections(int hat, ControllerButton[] expected)
    {
        byte[] report = BuildReport(bluetooth: false, (r, o) => r[o + 4] = (byte)hat);
        var state = new PhysicalInputState();

        Assert.True(DualShock4ReportParser.TryParseInputReport(report, DualShock4Calibration.Nominal, state, out _));
        Assert.Equal(expected.OrderBy(b => b), state.PressedButtons.OrderBy(b => b));
    }

    [Fact]
    public void BluetoothReport_UsesOffsetThree()
    {
        byte[] report = BuildReport(bluetooth: true, (r, o) =>
        {
            r[1] = 0xC0;             // BT header bytes, must not be read as sticks
            r[2] = 0x00;
            r[o + 0] = 255;          // LX full right
            r[o + 4] = 0x08 | 0x10;  // Square
            r[o + 5] = 0x02 | 0x20;  // R1 + Options
            r[o + 8] = 255;          // R2 full
            WriteInt16(r, o + 14, 1638); // yaw ≈ 100 deg/s
        });

        var state = new PhysicalInputState();
        Assert.True(DualShock4ReportParser.TryParseInputReport(report, DualShock4Calibration.Nominal, state, out var conn));

        Assert.Equal(HidConnection.Bluetooth, conn);
        Assert.Equal(1.0, state.LX, 6);
        Assert.Equal(0.0, state.LY);
        Assert.Equal(1.0, state.R2, 6);
        Assert.Equal(100.0, state.GyroYaw, 1);
        Assert.Equal(
            new[] { ControllerButton.West, ControllerButton.RightShoulder, ControllerButton.Start }.OrderBy(b => b),
            state.PressedButtons.OrderBy(b => b));
    }

    [Fact]
    public void NominalCalibration_ConvertsImuToDegreesAndG()
    {
        byte[] report = BuildReport(bluetooth: false, (r, o) =>
        {
            WriteInt16(r, o + 12, 1638);   // pitch ≈ 100 deg/s
            WriteInt16(r, o + 14, -819);   // yaw ≈ -50 deg/s
            WriteInt16(r, o + 16, 0);      // roll
            WriteInt16(r, o + 18, 0);      // accel X
            WriteInt16(r, o + 20, 8192);   // accel Y = 1 g (DS4 resting flat)
            WriteInt16(r, o + 22, 0);      // accel Z
        });

        var state = new PhysicalInputState();
        Assert.True(DualShock4ReportParser.TryParseInputReport(report, DualShock4Calibration.Nominal, state, out _));

        Assert.Equal(100.0, state.GyroPitch, 1);
        Assert.Equal(-50.0, state.GyroYaw, 1);
        Assert.Equal(0.0, state.GyroRoll, 6);
        Assert.Equal(1.0, state.AccelY, 3);
        Assert.Equal(0.0, state.AccelX, 6);
    }

    // Per-axis plus/minus values differ so a wrong layout would be detected.
    private static readonly short[] GyroPlus = { 8800, 4400, 2200 };
    private static readonly short[] GyroMinus = { -8800, -4400, -2200 };

    private static byte[] BuildCalibration(byte reportId, bool interleaved, int length)
    {
        var feature = new byte[length];
        feature[0] = reportId;
        // Gyro bias pitch/yaw/roll = 10, -5, 3
        WriteInt16(feature, 1, 10);
        WriteInt16(feature, 3, -5);
        WriteInt16(feature, 5, 3);
        for (int i = 0; i < 3; i++)
        {
            if (interleaved)
            {
                WriteInt16(feature, 7 + (i * 4), GyroPlus[i]);
                WriteInt16(feature, 9 + (i * 4), GyroMinus[i]);
            }
            else
            {
                WriteInt16(feature, 7 + (i * 2), GyroPlus[i]);
                WriteInt16(feature, 13 + (i * 2), GyroMinus[i]);
            }
        }
        WriteInt16(feature, 19, 540); // speed plus
        WriteInt16(feature, 21, 540); // speed minus
        // Accel ±1 g with a +100 offset on X
        WriteInt16(feature, 23, 8292);
        WriteInt16(feature, 25, -8092);
        foreach (int off in new[] { 27, 31 }) WriteInt16(feature, off, 8192);
        foreach (int off in new[] { 29, 33 }) WriteInt16(feature, off, -8192);
        return feature;
    }

    private static void AssertCalibrationMatches(DualShock4Calibration cal)
    {
        Assert.True(cal.IsFromDevice);

        // A sample equal to the bias must read 0 deg/s
        Assert.Equal(0.0, cal.GyroToDegSec(0, 10), 6);
        Assert.Equal(0.0, cal.GyroToDegSec(1, -5), 6);
        Assert.Equal(0.0, cal.GyroToDegSec(2, 3), 6);

        // speed2x = 1080 over |plus - bias| + |minus - bias| → ≈ 540 deg/s at the plus reference on every axis
        // (a wrong plus/minus layout would give values far outside this range)
        for (int axis = 0; axis < 3; axis++)
        {
            Assert.InRange(cal.GyroToDegSec(axis, GyroPlus[axis]), 535.0, 545.0);
            Assert.InRange(cal.GyroToDegSec(axis, GyroMinus[axis]), -545.0, -535.0);
        }

        // Accelerometer bias removed: X midpoint (100) = 0 g, Y plus reference = 1 g
        Assert.Equal(0.0, cal.AccelToG(0, 100), 6);
        Assert.Equal(1.0, cal.AccelToG(1, 8192), 6);
    }

    [Fact]
    public void UsbCalibration_0x02_InterleavedLayout()
    {
        byte[] feature = BuildCalibration(DualShock4Calibration.UsbFeatureReportId, interleaved: true, length: 37);
        Assert.True(DualShock4Calibration.TryParse(feature, out var cal));
        AssertCalibrationMatches(cal);
    }

    [Fact]
    public void BluetoothCalibration_0x05_GroupedLayout()
    {
        byte[] feature = BuildCalibration(DualShock4Calibration.BluetoothFeatureReportId, interleaved: false, length: 41);
        Assert.True(DualShock4Calibration.TryParse(feature, out var cal));
        AssertCalibrationMatches(cal);
    }

    [Fact]
    public void AdapterCalibration_0x02_WithGroupedLayout_IsDetectedBySigns()
    {
        // The wireless adapter answers 0x02 but with the Bluetooth (grouped) layout
        byte[] feature = BuildCalibration(DualShock4Calibration.UsbFeatureReportId, interleaved: false, length: 37);
        Assert.True(DualShock4Calibration.TryParse(feature, out var cal));
        AssertCalibrationMatches(cal);
    }

    [Fact]
    public void CalibrationWithZeroRange_FallsBackToNominalPerAxis()
    {
        var feature = new byte[37];
        feature[0] = DualShock4Calibration.UsbFeatureReportId; // all zeros: clone without calibration

        Assert.True(DualShock4Calibration.TryParse(feature, out var cal));
        Assert.Equal(100.0, cal.GyroToDegSec(0, 1638), 1);
        Assert.Equal(1.0, cal.AccelToG(1, 8192), 3);
    }

    [Fact]
    public void RejectsShortOrUnknownReports()
    {
        var state = new PhysicalInputState();
        // Reduced BT report 0x01 (10 bytes) sent before calibration is requested
        Assert.False(DualShock4ReportParser.TryParseInputReport(new byte[10] { 0x01, 128, 128, 128, 128, 8, 0, 0, 0, 0 },
            DualShock4Calibration.Nominal, state, out _));
        // BT 0x11 too short to contain the IMU
        Assert.False(DualShock4ReportParser.TryParseInputReport(new byte[20] { 0x11, 0xC0, 0, 128, 128, 128, 128, 8, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 },
            DualShock4Calibration.Nominal, state, out _));
        // Unknown report ID
        var unknown = new byte[64];
        unknown[0] = 0x31;
        Assert.False(DualShock4ReportParser.TryParseInputReport(unknown, DualShock4Calibration.Nominal, state, out _));

        Assert.False(DualShock4Calibration.TryParse(new byte[] { 0x02, 0, 0 }, out var cal));
        Assert.False(cal.IsFromDevice);
        Assert.False(DualShock4Calibration.TryParse(new byte[37], out _)); // report ID 0x00
    }

    [Theory]
    [InlineData(0x054C, 0x05C4, true)]  // DS4 v1
    [InlineData(0x054C, 0x09CC, true)]  // DS4 v2
    [InlineData(0x054C, 0x0BA0, true)]  // Sony wireless adapter
    [InlineData(0x054C, 0x0CE6, false)] // DualSense
    [InlineData(0x045E, 0x09CC, false)]
    public void IsDualShock4_MatchesOnlySonyDs4Pids(int vid, int pid, bool expected)
    {
        Assert.Equal(expected, DualShock4ReportParser.IsDualShock4(vid, pid));
    }
}
