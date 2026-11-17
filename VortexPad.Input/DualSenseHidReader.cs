using HidSharp;
using VortexPad.Core.Hid;
using VortexPad.Core.Models;

namespace VortexPad.Input;

/// <summary>
/// Reads a DualSense / DualSense Edge over native HID (USB or Bluetooth) on its own thread.
/// See <see cref="SonyHidReaderBase{TCalibration}"/> for threading and reconnection.
/// </summary>
public sealed class DualSenseHidReader : SonyHidReaderBase<DualSenseCalibration>
{
    public DualSenseHidReader(string devicePath) : base(devicePath)
    {
    }

    protected override string DeviceLabel => "DualSense";

    protected override DualSenseCalibration NominalCalibration => DualSenseCalibration.Nominal;

    // Feature 0x05 provides the IMU calibration and, over Bluetooth,
    // also switches the controller to the full 0x31 report (with gyro).
    protected override DualSenseCalibration ReadCalibration(HidDevice device, HidStream stream)
    {
        byte[]? feature = TryGetFeature(device, stream,
            DualSenseCalibration.FeatureReportId, DualSenseCalibration.FeatureReportMinLength);
        return feature != null && DualSenseCalibration.TryParse(feature, out var cal) ? cal : DualSenseCalibration.Nominal;
    }

    protected override int BuildOutputReport(
        in SonyOutputState state, HidConnection connection, ref byte sequence, bool firstAfterConnect, Span<byte> buffer)
    {
        int length = DualSenseOutputReport.Build(state, connection, sequence, firstAfterConnect, buffer);
        sequence = (byte)((sequence + 1) & 0x0F);
        return length;
    }

    protected override bool TryParseReport(
        ReadOnlySpan<byte> report, DualSenseCalibration calibration, PhysicalInputState state, out HidConnection connection) =>
        DualSenseReportParser.TryParseInputReport(report, calibration, state, out connection);
}
