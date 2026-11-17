using System.Buffers.Binary;
using VortexPad.Core.Hid;
using VortexPad.Core.Models;
using Xunit;

namespace VortexPad.CoreTests;

public class DualSenseReportParserTests
{
    // Builds a report with the given data placed after the header (USB: 0x01; BT: 0x31 + 1 tag byte).
    private static byte[] BuildReport(bool bluetooth, Action<byte[], int> fill)
    {
        var report = new byte[bluetooth ? 78 : 64];
        int offset = bluetooth ? 2 : 1;
        report[0] = bluetooth ? DualSenseReportParser.BluetoothInputReportId : DualSenseReportParser.UsbInputReportId;

        // Neutral state: centered sticks, released triggers, D-pad centered (hat = 8)
        report[offset + 0] = 128;
        report[offset + 1] = 128;
        report[offset + 2] = 128;
        report[offset + 3] = 128;
        report[offset + 7] = 0x08;

        fill(report, offset);
        return report;
    }

    private static void WriteInt16(byte[] buf, int index, short value) =>
        BinaryPrimitives.WriteInt16LittleEndian(buf.AsSpan(index, 2), value);

    [Fact]
    public void UsbReport_MapsSticksTriggersAndFaceButtons()
    {
        byte[] report = BuildReport(bluetooth: false, (r, o) =>
        {
            r[o + 0] = 255;          // LX full right
            r[o + 1] = 0;            // LY full up (raw 0 = up)
            r[o + 2] = 0;            // RX full left
            r[o + 3] = 255;          // RY full down
            r[o + 4] = 255;          // L2 full
            r[o + 5] = 51;           // R2 20%
            r[o + 7] = 0x08 | 0x20 | 0x80; // Cross + Triangle, D-pad centered
            r[o + 8] = 0x01 | 0x20;        // L1 + Options
            r[o + 9] = 0x01 | 0x02;        // PS + Touchpad
        });

        var state = new PhysicalInputState();
        bool ok = DualSenseReportParser.TryParseInputReport(report, DualSenseCalibration.Nominal, state, out var conn);

        Assert.True(ok);
        Assert.Equal(DualSenseConnection.Usb, conn);
        Assert.Equal(1.0, state.LX, 6);
        Assert.Equal(1.0, state.LY, 6);
        Assert.Equal(-1.0, state.RX, 6);
        Assert.Equal(-1.0, state.RY, 6);
        Assert.Equal(1.0, state.L2, 6);
        Assert.Equal(0.2, state.R2, 6);

        Assert.Equal(
            new[] { ControllerButton.South, ControllerButton.North, ControllerButton.LeftShoulder,
                    ControllerButton.Start, ControllerButton.Guide, ControllerButton.TouchpadClick }.OrderBy(b => b),
            state.PressedButtons.OrderBy(b => b));
    }

    [Fact]
    public void CenteredSticks_ProduceExactZero()
    {
        byte[] report = BuildReport(bluetooth: false, (_, _) => { });
        var state = new PhysicalInputState();

        Assert.True(DualSenseReportParser.TryParseInputReport(report, DualSenseCalibration.Nominal, state, out _));
        Assert.Equal(0.0, state.LX);
        Assert.Equal(0.0, state.LY);
        Assert.Equal(0.0, state.RX);
        Assert.Equal(0.0, state.RY);
        Assert.Empty(state.PressedButtons);
    }

    [Theory]
    [InlineData(0, new[] { ControllerButton.DPadUp })]
    [InlineData(1, new[] { ControllerButton.DPadUp, ControllerButton.DPadRight })]
    [InlineData(4, new[] { ControllerButton.DPadDown })]
    [InlineData(5, new[] { ControllerButton.DPadDown, ControllerButton.DPadLeft })]
    [InlineData(6, new[] { ControllerButton.DPadLeft })]
    [InlineData(8, new ControllerButton[0])]
    public void DPadHat_DecodesAllDirections(int hat, ControllerButton[] expected)
    {
        byte[] report = BuildReport(bluetooth: false, (r, o) => r[o + 7] = (byte)hat);
        var state = new PhysicalInputState();

        Assert.True(DualSenseReportParser.TryParseInputReport(report, DualSenseCalibration.Nominal, state, out _));
        Assert.Equal(expected.OrderBy(b => b), state.PressedButtons.OrderBy(b => b));
    }

    [Fact]
    public void BluetoothReport_UsesOffsetTwo_AndDecodesEdgePaddles()
    {
        byte[] report = BuildReport(bluetooth: true, (r, o) =>
        {
            r[1] = 0xA5;             // BT tag/sequence byte, must not be read as LX
            r[o + 0] = 255;
            r[o + 7] = 0x08 | 0x10;  // Square
            r[o + 9] = 0x40 | 0x80;  // Edge back paddles
        });

        var state = new PhysicalInputState();
        Assert.True(DualSenseReportParser.TryParseInputReport(report, DualSenseCalibration.Nominal, state, out var conn));

        Assert.Equal(DualSenseConnection.Bluetooth, conn);
        Assert.Equal(1.0, state.LX, 6);
        Assert.Contains(ControllerButton.West, state.PressedButtons);
        Assert.Contains(ControllerButton.Paddle1, state.PressedButtons);
        Assert.Contains(ControllerButton.Paddle2, state.PressedButtons);
    }

    [Fact]
    public void NominalCalibration_ConvertsImuToDegreesAndG()
    {
        byte[] report = BuildReport(bluetooth: false, (r, o) =>
        {
            WriteInt16(r, o + 15, 1638);   // pitch ≈ 100 deg/s
            WriteInt16(r, o + 17, -819);   // yaw ≈ -50 deg/s
            WriteInt16(r, o + 19, 0);      // roll
            WriteInt16(r, o + 25, 8192);   // accel Z = 1 g (resting flat)
        });

        var state = new PhysicalInputState();
        Assert.True(DualSenseReportParser.TryParseInputReport(report, DualSenseCalibration.Nominal, state, out _));

        Assert.Equal(100.0, state.GyroPitch, 1);
        Assert.Equal(-50.0, state.GyroYaw, 1);
        Assert.Equal(0.0, state.GyroRoll, 6);
        Assert.Equal(1.0, state.AccelZ, 3);
    }

    [Fact]
    public void DeviceCalibration_RemovesGyroBias()
    {
        var feature = new byte[41];
        feature[0] = DualSenseCalibration.FeatureReportId;
        // Gyro bias pitch/yaw/roll = 10, -5, 3
        WriteInt16(feature, 1, 10);
        WriteInt16(feature, 3, -5);
        WriteInt16(feature, 5, 3);
        // plus/minus per axis (pitch 7/9, yaw 11/13, roll 15/17)
        foreach (int off in new[] { 7, 11, 15 }) WriteInt16(feature, off, 8800);
        foreach (int off in new[] { 9, 13, 17 }) WriteInt16(feature, off, -8800);
        WriteInt16(feature, 19, 540); // speed plus
        WriteInt16(feature, 21, 540); // speed minus
        // Accel ±1 g
        foreach (int off in new[] { 23, 27, 31 }) WriteInt16(feature, off, 8192);
        foreach (int off in new[] { 25, 29, 33 }) WriteInt16(feature, off, -8192);

        Assert.True(DualSenseCalibration.TryParse(feature, out var cal));
        Assert.True(cal.IsFromDevice);

        // A sample equal to the bias must read 0 deg/s
        Assert.Equal(0.0, cal.GyroToDegSec(0, 10), 6);
        Assert.Equal(0.0, cal.GyroToDegSec(1, -5), 6);
        // 1 g on Z
        Assert.Equal(1.0, cal.AccelToG(2, 8192), 6);
    }

    [Fact]
    public void RejectsShortOrUnknownReports()
    {
        var state = new PhysicalInputState();
        // Reduced BT report 0x01 (10 bytes) sent before calibration is requested
        Assert.False(DualSenseReportParser.TryParseInputReport(new byte[10] { 0x01, 128, 128, 128, 128, 8, 0, 0, 0, 0 },
            DualSenseCalibration.Nominal, state, out _));
        Assert.False(DualSenseReportParser.TryParseInputReport(new byte[64] { 0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, DualSenseCalibration.Nominal, state, out _));
        Assert.False(DualSenseCalibration.TryParse(new byte[] { 0x05, 0, 0 }, out var cal));
        Assert.False(cal.IsFromDevice);
    }

    [Theory]
    [InlineData(0x054C, 0x0CE6, true)]
    [InlineData(0x054C, 0x0DF2, true)]
    [InlineData(0x054C, 0x09CC, false)] // DualShock 4
    [InlineData(0x045E, 0x0CE6, false)]
    public void IsDualSense_MatchesOnlySonyDualSensePids(int vid, int pid, bool expected)
    {
        Assert.Equal(expected, DualSenseReportParser.IsDualSense(vid, pid));
    }
}
