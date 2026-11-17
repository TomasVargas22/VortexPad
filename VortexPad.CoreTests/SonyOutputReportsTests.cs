using System.Buffers.Binary;
using System.Text;
using VortexPad.Core.Hid;
using VortexPad.Core.Models;
using VortexPad.Core.Processing;
using VortexPad.Core.Profiles;
using Xunit;

namespace VortexPad.CoreTests;

public class SonyOutputReportsTests
{
    private static readonly SonyOutputState Sample = new(LargeMotor: 200, SmallMotor: 50, LedR: 0x11, LedG: 0x22, LedB: 0x33);

    [Fact]
    public void Crc32_MatchesStandardCheckValue()
    {
        // Standard CRC-32/ISO-HDLC check value: crc("123456789") = 0xCBF43926
        byte[] rest = Encoding.ASCII.GetBytes("23456789");
        Assert.Equal(0xCBF43926u, SonyCrc32.Compute((byte)'1', rest));
    }

    [Fact]
    public void DualSenseUsb_PlacesMotorsAndLightbar()
    {
        var buffer = new byte[DualSenseOutputReport.BluetoothLength];
        int len = DualSenseOutputReport.Build(Sample, HidConnection.Usb, 0, releaseStartupLights: false, buffer);

        Assert.Equal(DualSenseOutputReport.UsbLength, len);
        Assert.Equal(0x02, buffer[0]);
        Assert.Equal(0x03, buffer[1]);          // compatible vibration + haptics select
        Assert.Equal(0x14, buffer[2]);          // lightbar + player LEDs
        Assert.Equal(50, buffer[3]);            // right motor = small
        Assert.Equal(200, buffer[4]);           // left motor = large
        Assert.Equal(DualSenseOutputReport.PlayerLedsOne, buffer[1 + 43]);
        Assert.Equal(0x11, buffer[1 + 44]);
        Assert.Equal(0x22, buffer[1 + 45]);
        Assert.Equal(0x33, buffer[1 + 46]);
        Assert.Equal(0x00, buffer[1 + 41]);     // no lightbar setup unless requested
        // The data fits in the 48 bytes Windows reports for the USB output report
        Assert.True(1 + 46 < 48);
    }

    [Fact]
    public void DualSenseBluetooth_HasHeaderSequenceAndValidCrc()
    {
        var buffer = new byte[DualSenseOutputReport.BluetoothLength];
        int len = DualSenseOutputReport.Build(Sample, HidConnection.Bluetooth, sequence: 5, releaseStartupLights: true, buffer);

        Assert.Equal(78, len);
        Assert.Equal(0x31, buffer[0]);
        Assert.Equal(0x50, buffer[1]);          // sequence in the high nibble
        Assert.Equal(0x10, buffer[2]);          // tag
        Assert.Equal(50, buffer[3 + 2]);
        Assert.Equal(200, buffer[3 + 3]);
        Assert.Equal(0x02, buffer[3 + 41]);     // lightbar setup: light out
        Assert.Equal(0x06, buffer[3 + 38]);     // flag2: vibration v2 + lightbar setup

        uint expected = SonyCrc32.Compute(SonyCrc32.OutputSeed, buffer.AsSpan(0, 74));
        Assert.Equal(expected, BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(74, 4)));
    }

    [Fact]
    public void DualShock4Usb_MatchesKnownLayout()
    {
        var buffer = new byte[DualShock4OutputReport.BluetoothLength];
        int len = DualShock4OutputReport.Build(Sample, HidConnection.Usb, buffer);

        Assert.Equal(32, len);
        Assert.Equal(0x05, buffer[0]);
        Assert.Equal(0x03, buffer[1]);          // motor + LED
        Assert.Equal(50, buffer[4]);            // right = small
        Assert.Equal(200, buffer[5]);           // left = large
        Assert.Equal(0x11, buffer[6]);
        Assert.Equal(0x22, buffer[7]);
        Assert.Equal(0x33, buffer[8]);
    }

    [Fact]
    public void DualShock4Bluetooth_MatchesKnownLayoutWithCrc()
    {
        var buffer = new byte[DualShock4OutputReport.BluetoothLength];
        int len = DualShock4OutputReport.Build(Sample, HidConnection.Bluetooth, buffer);

        Assert.Equal(78, len);
        Assert.Equal(0x11, buffer[0]);
        Assert.Equal(0xC4, buffer[1]);          // HID + CRC + 4 ms poll interval
        Assert.Equal(0x03, buffer[3]);
        Assert.Equal(50, buffer[6]);
        Assert.Equal(200, buffer[7]);
        Assert.Equal(0x11, buffer[8]);
        Assert.Equal(0x22, buffer[9]);
        Assert.Equal(0x33, buffer[10]);

        uint expected = SonyCrc32.Compute(0xA2, buffer.AsSpan(0, 74));
        Assert.Equal(expected, BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(74, 4)));
    }

    [Fact]
    public void Builders_ClearPreviousBufferContents()
    {
        var buffer = Enumerable.Repeat((byte)0xEE, 78).ToArray();
        DualShock4OutputReport.Build(SonyOutputState.Off, HidConnection.Usb, buffer);
        Assert.All(buffer.Skip(2).Take(30), b => Assert.Equal(0, b));
    }

    [Fact]
    public void Mixer_AppliesProfileGainsAndClamps()
    {
        var feedback = new ForceFeedbackState { LargeMotor = 1.0, SmallMotor = 0.5 };
        var config = new ForceFeedbackConfiguration { Enabled = true, OverallStrength = 0.5, LargeMotorGain = 1.0, SmallMotorGain = 4.0 };

        var output = ForceFeedbackMixer.Compute(feedback, config, "#102030");

        Assert.Equal(128, output.LargeMotor);   // 1.0 * 0.5 * 1.0 = 0.5 → 127.5 → 128
        Assert.Equal(128, output.SmallMotor);   // 0.5 * 0.5 * 2.0 (gain 4 capped at 2) = 0.5 → 128
        Assert.Equal(0x10, output.LedR);
        Assert.Equal(0x20, output.LedG);
        Assert.Equal(0x30, output.LedB);
    }

    [Fact]
    public void Mixer_DisabledOrNoFeedback_TurnsMotorsOff_AndUsesGameLightbarWhenProvided()
    {
        var config = new ForceFeedbackConfiguration { Enabled = false };
        var game = new ForceFeedbackState { LargeMotor = 1, SmallMotor = 1, LedR = 9, LedG = 8, LedB = 7, HasLightbar = true };

        var disabled = ForceFeedbackMixer.Compute(game, config, "#FFFFFF");
        Assert.Equal(0, disabled.LargeMotor);
        Assert.Equal(0, disabled.SmallMotor);
        Assert.Equal(9, disabled.LedR); // the game controls the LED even with vibration disabled

        var none = ForceFeedbackMixer.Compute(null, new ForceFeedbackConfiguration(), "not-a-color");
        Assert.Equal(0, none.LargeMotor);
        Assert.Equal(0x00, none.LedR); // fallback color #00E5FF
        Assert.Equal(0xE5, none.LedG);
        Assert.Equal(0xFF, none.LedB);
    }
}
