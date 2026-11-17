using VortexPad.Core.Hid;
using Xunit;

namespace VortexPad.CoreTests;

public class HidDevicePathsTests
{
    [Theory]
    // DualSense over USB
    [InlineData(@"\\?\hid#vid_054c&pid_0ce6&mi_03#7&2a5e1b6&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}",
                @"HID\VID_054C&PID_0CE6&MI_03\7&2A5E1B6&0&0000")]
    // DualSense over Bluetooth (GUID in the middle of the hardware ID)
    [InlineData(@"\\?\hid#{00001124-0000-1000-8000-00805f9b34fb}_vid&0002054c_pid&0ce6#9&1a2b3c4d&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}",
                @"HID\{00001124-0000-1000-8000-00805F9B34FB}_VID&0002054C_PID&0CE6\9&1A2B3C4D&0&0000")]
    // Already an instance ID
    [InlineData(@"hid\vid_045e&pid_028e&ig_00\8&abc&0&0000", @"HID\VID_045E&PID_028E&IG_00\8&ABC&0&0000")]
    public void DevicePathToInstanceId_ConvertsRealPaths(string devicePath, string expected)
    {
        Assert.Equal(expected, HidDevicePaths.DevicePathToInstanceId(devicePath));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(@"\\?\usb#vid_054c&pid_0ce6#5&1234#{a5dcbf10-6530-11d2-901f-00c04fb951ed}")] // not HID
    [InlineData(@"\\?\hid#vid_054c&pid_0ce6#{4d1e55b2-f16f-11cf-88cb-001111000030}")]         // missing instance
    public void DevicePathToInstanceId_RejectsInvalidPaths(string? devicePath)
    {
        Assert.Null(HidDevicePaths.DevicePathToInstanceId(devicePath));
    }
}

public class VirtualDeviceSignaturesTests
{
    [Theory]
    [InlineData(@"Nefarius\ViGEmBus\Gen1", true)]
    [InlineData(@"ROOT\VIGEMBUS\0000", true)]
    [InlineData(@"root\vigembus", true)]
    [InlineData(@"USB\VID_054C&PID_05C4&REV_0100", false)]   // the ViGEm child itself looks like a real DS4
    [InlineData(@"HID\VID_2DC8&PID_3106\7&ABC&0&0000", false)] // 8BitDo
    [InlineData(@"ROOT\SYSTEM\0001", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void IsViGEmBusIdentifier_OnlyMatchesTheBus(string? id, bool expected)
    {
        Assert.Equal(expected, VirtualDeviceSignatures.IsViGEmBusIdentifier(id));
    }
}
