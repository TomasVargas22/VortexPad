using System.Text.Json;
using VortexPad.Core.Automation;
using VortexPad.Core.Hid;
using VortexPad.Core.Models;
using VortexPad.Core.Processing;
using VortexPad.Core.Profiles;
using VortexPad.ViewModels;
using Xunit;

namespace VortexPad.CoreTests;

public class DualSenseTouchAndButtonsParsingTests
{
    private static byte[] Report(bool bt, Action<byte[], int> fill)
    {
        var r = new byte[bt ? 78 : 64];
        int o = bt ? 2 : 1;
        r[0] = bt ? DualSenseReportParser.BluetoothInputReportId : DualSenseReportParser.UsbInputReportId;
        r[o] = r[o + 1] = r[o + 2] = r[o + 3] = 128;
        r[o + 7] = 0x08;
        // No fingers: bit 7 set on both points
        r[o + 32] = 0x80;
        r[o + 36] = 0x80;
        fill(r, o);
        return r;
    }

    private static void WriteTouch(byte[] r, int at, byte id, int x, int y)
    {
        r[at] = (byte)(id & 0x7F);              // bit 7 = 0 → touching
        r[at + 1] = (byte)(x & 0xFF);
        r[at + 2] = (byte)(((x >> 8) & 0x0F) | ((y & 0x0F) << 4));
        r[at + 3] = (byte)(y >> 4);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Touchpad_DecodesTwoFingers_OnUsbAndBluetooth(bool bt)
    {
        var report = Report(bt, (r, o) =>
        {
            WriteTouch(r, o + 32, id: 5, x: 1919, y: 0);    // top-right corner
            WriteTouch(r, o + 36, id: 6, x: 960, y: 1079);  // bottom center
        });

        var s = new PhysicalInputState();
        Assert.True(DualSenseReportParser.TryParseInputReport(report, DualSenseCalibration.Nominal, s, out _));

        Assert.True(s.Touch1.Active);
        Assert.Equal(5, s.Touch1.Id);
        Assert.Equal(1.0, s.Touch1.X, 3);
        Assert.Equal(0.0, s.Touch1.Y, 3);
        Assert.True(s.Touch2.Active);
        Assert.Equal(0.5, s.Touch2.X, 2);
        Assert.Equal(1.0, s.Touch2.Y, 3);
    }

    [Fact]
    public void Touchpad_WithoutFingers_IsInactive()
    {
        var s = new PhysicalInputState { Touch1 = new TouchPoint(true, 1, 0.3, 0.3) };
        Assert.True(DualSenseReportParser.TryParseInputReport(Report(false, (_, _) => { }), DualSenseCalibration.Nominal, s, out _));
        Assert.False(s.Touch1.Active);
        Assert.False(s.Touch2.Active);
    }

    [Fact]
    public void MuteAndEdgeFnButtons_AreDecoded()
    {
        var report = Report(false, (r, o) => r[o + 9] = 0x04 | 0x10 | 0x20);
        var s = new PhysicalInputState();
        DualSenseReportParser.TryParseInputReport(report, DualSenseCalibration.Nominal, s, out _);

        Assert.Contains(ControllerButton.Mute, s.PressedButtons);
        Assert.Contains(ControllerButton.FnLeft, s.PressedButtons);
        Assert.Contains(ControllerButton.FnRight, s.PressedButtons);
        Assert.DoesNotContain(ControllerButton.Paddle1, s.PressedButtons);
    }
}

public class AdaptiveTriggerTests
{
    private static byte[] Encode(AdaptiveTriggerEffect e)
    {
        var b = new byte[AdaptiveTriggerEncoder.EffectLength];
        AdaptiveTriggerEncoder.Encode(e, b);
        return b;
    }

    [Fact]
    public void Off_IsMode0x05_WithZeroParams()
    {
        Assert.Equal(new byte[] { 0x05, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, Encode(AdaptiveTriggerEffect.Off));
    }

    [Fact]
    public void Resistance_MarksZonesFromStart_WithStrengthPerZone()
    {
        // From position 7 at strength 8 (value 7 = 0b111 in zones 7, 8, 9)
        var b = Encode(new AdaptiveTriggerEffect(AdaptiveTriggerMode.Resistance, StartPosition: 7, Strength: 8));

        Assert.Equal(0x21, b[0]);
        ushort active = (ushort)(b[1] | (b[2] << 8));
        Assert.Equal(0b11_1000_0000, active);
        uint zones = (uint)(b[3] | (b[4] << 8) | (b[5] << 16) | (b[6] << 24));
        Assert.Equal((7u << 21) | (7u << 24) | (7u << 27), zones);
    }

    [Fact]
    public void Weapon_EncodesStartAndEndBits_AndStrength()
    {
        var b = Encode(new AdaptiveTriggerEffect(AdaptiveTriggerMode.Weapon, StartPosition: 2, EndPosition: 6, Strength: 5));
        Assert.Equal(0x25, b[0]);
        Assert.Equal((1 << 2) | (1 << 6), b[1] | (b[2] << 8));
        Assert.Equal(4, b[3]);
        Assert.All(b.Skip(4), x => Assert.Equal(0, x));
    }

    [Fact]
    public void Vibration_PutsFrequencyAtByte9()
    {
        var b = Encode(new AdaptiveTriggerEffect(AdaptiveTriggerMode.Vibration, StartPosition: 0, Strength: 1, FrequencyHz: 40));
        Assert.Equal(0x26, b[0]);
        Assert.Equal(0x3FF, b[1] | (b[2] << 8)); // all 10 zones
        Assert.Equal(0, b[3]);                   // strength 1 → 0 in every zone
        Assert.Equal(40, b[9]);
    }

    [Fact]
    public void FromConfig_ClampsToFirmwareRanges()
    {
        var weapon = AdaptiveTriggerEffect.FromConfig(new AdaptiveTriggerConfiguration
        {
            Mode = AdaptiveTriggerMode.Weapon, StartPosition = 0, EndPosition = 1, Strength = 50
        });
        Assert.Equal(2, weapon.StartPosition);   // minimum 2
        Assert.Equal(3, weapon.EndPosition);     // greater than the start
        Assert.Equal(8, weapon.Strength);

        var vib = AdaptiveTriggerEffect.FromConfig(new AdaptiveTriggerConfiguration
        {
            Mode = AdaptiveTriggerMode.Vibration, StartPosition = 99, FrequencyHz = 0
        });
        Assert.Equal(9, vib.StartPosition);
        Assert.Equal(1, vib.FrequencyHz);

        Assert.Equal(AdaptiveTriggerEffect.Off, AdaptiveTriggerEffect.FromConfig(null));
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 3)]
    public void DualSenseOutput_PlacesTriggerEffects(bool bt, int commonStart)
    {
        var state = new SonyOutputState(0, 0, 0, 0, 0,
            LeftTrigger: new AdaptiveTriggerEffect(AdaptiveTriggerMode.Weapon, 2, 6, 5),
            RightTrigger: new AdaptiveTriggerEffect(AdaptiveTriggerMode.Resistance, 0, Strength: 3));
        var buffer = new byte[78];

        DualSenseOutputReport.Build(state, bt ? HidConnection.Bluetooth : HidConnection.Usb, 0, false, buffer);

        Assert.Equal(0x0C, buffer[commonStart] & 0x0C);    // both trigger flags
        Assert.Equal(0x21, buffer[commonStart + 10]);      // right trigger at 10
        Assert.Equal(0x25, buffer[commonStart + 21]);      // left trigger at 21
        Assert.Equal(4, buffer[commonStart + 21 + 3]);
    }

    [Fact]
    public void Mixer_PassesTriggersThrough_AndChangesOutputEquality()
    {
        var cfg = new ForceFeedbackConfiguration();
        var a = ForceFeedbackMixer.Compute(null, cfg, "#000000");
        var b = ForceFeedbackMixer.Compute(null, cfg, "#000000",
            rightTrigger: new AdaptiveTriggerEffect(AdaptiveTriggerMode.Resistance, 3, Strength: 4));

        Assert.NotEqual(a, b); // the reader only resends when it changes: a trigger change must count
        Assert.Equal(AdaptiveTriggerMode.Resistance, b.RightTrigger.Mode);
    }
}

public class TouchpadProcessingTests
{
    private static ControllerProfile MouseProfile(Action<TouchpadConfiguration>? tweak = null)
    {
        var p = BuiltInPresets.CreatePs5DualSensePreset();
        p.Touchpad.Mode = TouchpadMode.Mouse;
        tweak?.Invoke(p.Touchpad);
        return p;
    }

    private static PhysicalInputState Finger(double x, double y, byte id = 1) => new() { Touch1 = new TouchPoint(true, id, x, y) };

    [Fact]
    public void OneFinger_MovesMouse_WithoutJumpOnContact()
    {
        var engine = new MacroAndTurboEngine();
        var profile = MouseProfile();

        var first = engine.ProcessFrame(Finger(0.5, 0.5), profile, 0.001);
        Assert.Equal(0, first.MouseDeltaX);                   // putting the finger down does not move

        var moved = engine.ProcessFrame(Finger(0.6, 0.4), profile, 0.001);
        Assert.Equal(100, moved.MouseDeltaX, 6);              // 10% of the width × 1000 px
        Assert.Equal(-0.1 * 1000 * 1080 / 1920, moved.MouseDeltaY, 6);

        var newContact = engine.ProcessFrame(Finger(0.1, 0.1, id: 2), profile, 0.001);
        Assert.Equal(0, newContact.MouseDeltaX);              // different finger: no jump
    }

    [Fact]
    public void TwoFingers_Scroll_UpWhenFingersMoveUp()
    {
        var engine = new MacroAndTurboEngine();
        var profile = MouseProfile();
        PhysicalInputState Two(double y) => new()
        {
            Touch1 = new TouchPoint(true, 1, 0.3, y),
            Touch2 = new TouchPoint(true, 2, 0.7, y)
        };

        engine.ProcessFrame(Two(0.6), profile, 0.001);
        var scrolled = engine.ProcessFrame(Two(0.5), profile, 0.001);

        Assert.Equal(120, scrolled.MouseWheelDelta, 6);       // 10% height → 1 notch up
        Assert.Equal(0, scrolled.MouseDeltaX);                 // does not move the pointer

        profile.Touchpad.InvertScroll = true;
        engine.InvalidateProfileCache();
        var inverted = engine.ProcessFrame(Two(0.4), profile, 0.001);
        Assert.Equal(-120, inverted.MouseWheelDelta, 6);
    }

    [Fact]
    public void Click_InMouseMode_IsLeftOrRightMouseButton_AndHidesTouchpadButton()
    {
        var engine = new MacroAndTurboEngine();
        var profile = MouseProfile();

        var left = Finger(0.2, 0.5);
        left.PressedButtons.Add(ControllerButton.TouchpadClick);
        var o = engine.ProcessFrame(left, profile, 0.001);
        Assert.True(o.MouseLeftButton);
        Assert.False(o.MouseRightButton);
        Assert.False(o.IsButtonActive(ControllerButton.TouchpadClick));

        var right = Finger(0.8, 0.5);
        right.PressedButtons.Add(ControllerButton.TouchpadClick);
        o = engine.ProcessFrame(right, profile, 0.001);
        Assert.True(o.MouseRightButton);
    }

    [Fact]
    public void ClickZones_WorkWithoutMouseMode()
    {
        var engine = new MacroAndTurboEngine();
        var profile = BuiltInPresets.CreatePs5DualSensePreset();
        profile.Touchpad.SplitClickZones = true;
        profile.Touchpad.LeftZoneButton = ControllerButton.Back;
        profile.Touchpad.RightZoneButton = ControllerButton.Start;

        var right = Finger(0.9, 0.5);
        right.PressedButtons.Add(ControllerButton.TouchpadClick);
        var o = engine.ProcessFrame(right, profile, 0.001);

        Assert.True(o.IsButtonActive(ControllerButton.Start));
        Assert.False(o.IsButtonActive(ControllerButton.TouchpadClick));
        Assert.Equal(0, o.MouseDeltaX); // disabled mode: does not move the mouse
    }

    [Fact]
    public void DisabledTouchpad_KeepsNormalTouchpadButton()
    {
        var engine = new MacroAndTurboEngine();
        var profile = BuiltInPresets.CreatePs5DualSensePreset();
        var input = Finger(0.5, 0.5);
        input.PressedButtons.Add(ControllerButton.TouchpadClick);

        var o = engine.ProcessFrame(input, profile, 0.001);
        Assert.True(o.IsButtonActive(ControllerButton.TouchpadClick));
        Assert.False(o.MouseLeftButton);
    }

    [Fact]
    public void TouchpadMouse_StaysAllocationFree()
    {
        var engine = new MacroAndTurboEngine();
        var profile = MouseProfile();
        var input = new PhysicalInputState();
        var output = new VirtualOutputState();

        for (int i = 0; i < 3000; i++)
        {
            input.Touch1 = new TouchPoint(true, 1, (i % 100) / 100.0, 0.5);
            input.Touch2 = i % 200 < 100 ? default : new TouchPoint(true, 2, 0.8, (i % 50) / 50.0);
            engine.ProcessFrame(input, profile, 0.001, output);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 3000; i++)
        {
            input.Touch1 = new TouchPoint(true, 1, (i % 100) / 100.0, 0.5);
            input.Touch2 = i % 200 < 100 ? default : new TouchPoint(true, 2, 0.8, (i % 50) / 50.0);
            engine.ProcessFrame(input, profile, 0.001, output);
        }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}

public class DualSenseProfileCompatibilityTests
{
    [Fact]
    public void OldProfileJson_WithoutNewSections_LoadsWithDefaults()
    {
        var old = BuiltInPresets.CreateXboxStandardPreset();
        string json = ProfileRepository.SerializeProfile(old);

        // Simulates a profile saved before Phase 8: removes the new sections
        using var doc = JsonDocument.Parse(json);
        var trimmed = doc.RootElement.EnumerateObject()
            .Where(p => p.Name is not ("touchpad" or "leftAdaptiveTrigger" or "rightAdaptiveTrigger"))
            .ToDictionary(p => p.Name, p => p.Value);
        string oldJson = JsonSerializer.Serialize(trimmed);

        var loaded = ProfileRepository.DeserializeProfile(oldJson)!;
        Assert.Equal(TouchpadMode.Disabled, loaded.Touchpad.Mode);
        Assert.Equal(AdaptiveTriggerMode.Off, loaded.RightAdaptiveTrigger.Mode);
    }

    [Fact]
    public void NewButtons_AndEffects_RoundTripByName()
    {
        var p = BuiltInPresets.CreatePs5DualSensePreset();
        p.BaseLayer.ButtonRemaps[ControllerButton.Mute] = ControllerButton.Guide;
        p.RightAdaptiveTrigger.Mode = AdaptiveTriggerMode.Weapon;
        p.Touchpad.LeftZoneButton = ControllerButton.FnLeft;

        string json = ProfileRepository.SerializeProfile(p);
        Assert.Contains("\"Mute\"", json);
        Assert.Contains("\"Weapon\"", json);

        var back = ProfileRepository.DeserializeProfile(json)!;
        Assert.Equal(ControllerButton.Guide, back.BaseLayer.ButtonRemaps[ControllerButton.Mute]);
        Assert.Equal(AdaptiveTriggerMode.Weapon, back.RightAdaptiveTrigger.Mode);
        Assert.Equal(ControllerButton.FnLeft, back.Touchpad.LeftZoneButton);
    }

    [Fact]
    public void EditorViewModels_EditAndClampDualSenseSettings()
    {
        var profile = BuiltInPresets.CreatePs5DualSensePreset();
        var vm = new ProfileEditorViewModel();
        vm.Load(profile);
        var edits = new List<string>();
        vm.ProfileEdited += edits.Add;

        vm.Touchpad.Mode = TouchpadMode.Mouse;
        Assert.True(vm.Touchpad.IsMouseMode);
        vm.Touchpad.SplitClickZones = true;
        Assert.False(vm.Touchpad.UsesZones);   // in mouse mode the mouse clicks take priority
        vm.Touchpad.ClickAsMouseButtons = false;
        Assert.True(vm.Touchpad.UsesZones);

        var r2 = vm.RightAdaptiveTrigger;
        r2.Mode = AdaptiveTriggerMode.Weapon;
        r2.StartPosition = 0;                  // weapon: minimum 2
        Assert.Equal(2, profile.RightAdaptiveTrigger.StartPosition);
        r2.StartPosition = 7;
        Assert.Equal(8, profile.RightAdaptiveTrigger.EndPosition); // pushed to start + 1
        r2.Strength = 12.4;
        Assert.Equal(8, profile.RightAdaptiveTrigger.Strength);

        Assert.Contains("Mode", edits);
        Assert.Contains("StartPosition", edits);
    }
}
