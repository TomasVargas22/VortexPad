using System.Buffers.Binary;

namespace VortexPad.Core.Hid;

/// <summary>What gets sent to the physical controller: motors, lightbar and player LEDs.</summary>
public readonly record struct SonyOutputState(
    byte LargeMotor,
    byte SmallMotor,
    byte LedR,
    byte LedG,
    byte LedB,
    byte PlayerLeds = DualSenseOutputReport.PlayerLedsOne)
{
    public static SonyOutputState Off { get; } = new(0, 0, 0, 0, 0, 0);
}

/// <summary>
/// CRC-32 (IEEE 802.3, reflected, polynomial 0xEDB88320) used by Sony Bluetooth reports.
/// The CRC covers a seed byte (0xA2 for output reports) followed by the report.
/// </summary>
public static class SonyCrc32
{
    public const byte OutputSeed = 0xA2;

    private static readonly uint[] Table = BuildTable();

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }
            table[i] = c;
        }
        return table;
    }

    public static uint Compute(byte seed, ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFFu;
        crc = Table[(crc ^ seed) & 0xFF] ^ (crc >> 8);
        foreach (byte b in data)
        {
            crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }
        return ~crc;
    }

    /// <summary>Writes the CRC of <c>report[0..^4]</c> into the last 4 bytes (little-endian).</summary>
    public static void WriteTrailer(Span<byte> report, byte seed = OutputSeed)
    {
        uint crc = Compute(seed, report[..^4]);
        BinaryPrimitives.WriteUInt32LittleEndian(report[^4..], crc);
    }
}

/// <summary>
/// DualSense / DualSense Edge output report.
/// USB: report 0x02, 63 bytes, common block at offset 1.
/// Bluetooth: report 0x31, 78 bytes, [1] = sequence (high nibble), [2] = tag 0x10, common block at offset 3, CRC-32 at the end.
/// </summary>
public static class DualSenseOutputReport
{
    public const byte UsbReportId = 0x02;
    public const byte BluetoothReportId = 0x31;
    public const int UsbLength = 63;
    public const int BluetoothLength = 78;

    /// <summary>Player 1: center LED only.</summary>
    public const byte PlayerLedsOne = 0x04;

    // valid_flag0
    private const byte Flag0CompatibleVibration = 0x01;
    private const byte Flag0HapticsSelect = 0x02;
    // valid_flag1
    private const byte Flag1LightbarControl = 0x04;
    private const byte Flag1PlayerIndicatorControl = 0x10;
    // valid_flag2
    private const byte Flag2LightbarSetupControl = 0x02;
    private const byte Flag2CompatibleVibration2 = 0x04; // newer firmware uses this one instead of Flag0CompatibleVibration
    private const byte LightbarSetupLightOut = 0x02;    // ends the default blue animation so the color can be controlled

    // Offsets inside the common block
    private const int OffFlag0 = 0, OffFlag1 = 1, OffMotorRight = 2, OffMotorLeft = 3;
    private const int OffFlag2 = 38, OffLightbarSetup = 41, OffPlayerLeds = 43;
    private const int OffLedR = 44, OffLedG = 45, OffLedB = 46;

    /// <summary>
    /// Builds the report into <paramref name="buffer"/> (zero-filled first) and returns its length.
    /// </summary>
    /// <param name="sequence">Bluetooth only: 0..15 counter, incremented by the caller on every report.</param>
    /// <param name="releaseStartupLights">Send on the first report after connecting to take control of the lightbar.</param>
    public static int Build(
        in SonyOutputState state, HidConnection connection, byte sequence, bool releaseStartupLights, Span<byte> buffer)
    {
        bool bt = connection == HidConnection.Bluetooth;
        int length = bt ? BluetoothLength : UsbLength;
        if (buffer.Length < length) throw new ArgumentException($"El buffer necesita {length} bytes.", nameof(buffer));

        Span<byte> report = buffer[..length];
        report.Clear();

        int c; // start of the common block
        if (bt)
        {
            report[0] = BluetoothReportId;
            report[1] = (byte)((sequence & 0x0F) << 4);
            report[2] = 0x10; // DS_OUTPUT_TAG
            c = 3;
        }
        else
        {
            report[0] = UsbReportId;
            c = 1;
        }

        report[c + OffFlag0] = Flag0CompatibleVibration | Flag0HapticsSelect;
        report[c + OffFlag1] = Flag1LightbarControl | Flag1PlayerIndicatorControl;
        report[c + OffFlag2] = Flag2CompatibleVibration2;
        if (releaseStartupLights)
        {
            report[c + OffFlag2] |= Flag2LightbarSetupControl;
            report[c + OffLightbarSetup] = LightbarSetupLightOut;
        }

        // DualSense: "right" motor = small (high frequency), "left" = large (low frequency)
        report[c + OffMotorRight] = state.SmallMotor;
        report[c + OffMotorLeft] = state.LargeMotor;
        report[c + OffPlayerLeds] = state.PlayerLeds;
        report[c + OffLedR] = state.LedR;
        report[c + OffLedG] = state.LedG;
        report[c + OffLedB] = state.LedB;

        if (bt)
        {
            SonyCrc32.WriteTrailer(report);
        }
        return length;
    }
}

/// <summary>
/// DualShock 4 output report.
/// USB / wireless adapter: report 0x05, 32 bytes, common block at offset 1.
/// Bluetooth: report 0x11, 78 bytes, [1] = 0xC0 (HID + CRC) | poll interval, common block at offset 3, CRC-32 at the end.
/// </summary>
public static class DualShock4OutputReport
{
    public const byte UsbReportId = 0x05;
    public const byte BluetoothReportId = 0x11;
    public const int UsbLength = 32;
    public const int BluetoothLength = 78;

    private const byte FlagMotor = 0x01;
    private const byte FlagLed = 0x02;
    private const byte BtHwControlHidCrc = 0xC0;
    private const byte BtPollIntervalMs = 4;

    // Offsets inside the common block
    private const int OffFlag0 = 0, OffMotorRight = 3, OffMotorLeft = 4, OffLedR = 5, OffLedG = 6, OffLedB = 7;

    public static int Build(in SonyOutputState state, HidConnection connection, Span<byte> buffer)
    {
        bool bt = connection == HidConnection.Bluetooth;
        int length = bt ? BluetoothLength : UsbLength;
        if (buffer.Length < length) throw new ArgumentException($"El buffer necesita {length} bytes.", nameof(buffer));

        Span<byte> report = buffer[..length];
        report.Clear();

        int c;
        if (bt)
        {
            report[0] = BluetoothReportId;
            report[1] = BtHwControlHidCrc | BtPollIntervalMs;
            report[2] = 0x00; // audio control
            c = 3;
        }
        else
        {
            report[0] = UsbReportId;
            c = 1;
        }

        report[c + OffFlag0] = FlagMotor | FlagLed;
        // DS4: "right" motor = small (high frequency), "left" = large
        report[c + OffMotorRight] = state.SmallMotor;
        report[c + OffMotorLeft] = state.LargeMotor;
        report[c + OffLedR] = state.LedR;
        report[c + OffLedG] = state.LedG;
        report[c + OffLedB] = state.LedB;

        if (bt)
        {
            SonyCrc32.WriteTrailer(report);
        }
        return length;
    }
}
