using HidSharp;
using VortexPad.Core.Hid;
using VortexPad.Core.Models;

namespace VortexPad.Input;

/// <summary>
/// Reads a DualShock 4 (v1, v2 or through the Sony wireless adapter) over native HID on its own thread.
/// See <see cref="SonyHidReaderBase{TCalibration}"/> for threading and reconnection.
/// </summary>
public sealed class DualShock4HidReader : SonyHidReaderBase<DualShock4Calibration>
{
    private readonly bool _likelyBluetooth;

    /// <param name="devicePath">HID device path of the gamepad interface.</param>
    /// <param name="likelyBluetooth">
    /// Hint for the first calibration request: over Bluetooth feature 0x05 is the one that enables the full 0x11 report.
    /// </param>
    public DualShock4HidReader(string devicePath, bool likelyBluetooth) : base(devicePath)
    {
        _likelyBluetooth = likelyBluetooth;
    }

    protected override string DeviceLabel => "DualShock 4";

    protected override DualShock4Calibration NominalCalibration => DualShock4Calibration.Nominal;

    protected override DualShock4Calibration ReadCalibration(HidDevice device, HidStream stream)
    {
        // USB / adapter answer 0x02; Bluetooth answers 0x05. Try the likely one first, then the other.
        byte first = _likelyBluetooth ? DualShock4Calibration.BluetoothFeatureReportId : DualShock4Calibration.UsbFeatureReportId;
        byte second = _likelyBluetooth ? DualShock4Calibration.UsbFeatureReportId : DualShock4Calibration.BluetoothFeatureReportId;

        foreach (byte id in new[] { first, second })
        {
            byte[]? feature = TryGetFeature(device, stream, id, DualShock4Calibration.FeatureReportMinLength);
            if (feature != null && DualShock4Calibration.TryParse(feature, out var cal))
            {
                return cal;
            }
        }

        return DualShock4Calibration.Nominal;
    }

    protected override bool TryParseReport(
        ReadOnlySpan<byte> report, DualShock4Calibration calibration, PhysicalInputState state, out HidConnection connection) =>
        DualShock4ReportParser.TryParseInputReport(report, calibration, state, out connection);
}
