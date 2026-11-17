using System.Runtime.InteropServices;
using VortexPad.Core.Models;

namespace VortexPad.Emulation;

public sealed class NativeKeyboardMouseInjector : IDisposable
{
    private const uint INPUT_MOUSE = 0;
    private const uint INPUT_KEYBOARD = 1;
    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    private readonly HashSet<ushort> _currentlyPressedKeys = new();
    private double _residualMouseX;
    private double _residualMouseY;

    public bool Enabled { get; set; } = true;

    public void ApplyOutput(VirtualOutputState state)
    {
        if (!Enabled) return;

        var inputs = new List<INPUT>();

        // 1. Sub-pixel mouse movement accumulation
        double totalX = state.MouseDeltaX + _residualMouseX;
        double totalY = state.MouseDeltaY + _residualMouseY;
        int moveX = (int)Math.Truncate(totalX);
        int moveY = (int)Math.Truncate(totalY);
        _residualMouseX = totalX - moveX;
        _residualMouseY = totalY - moveY;

        if (moveX != 0 || moveY != 0)
        {
            inputs.Add(new INPUT
            {
                type = INPUT_MOUSE,
                U = new InputUnion
                {
                    mi = new MOUSEINPUT
                    {
                        dx = moveX,
                        dy = moveY,
                        dwFlags = MOUSEEVENTF_MOVE
                    }
                }
            });
        }

        // 2. Virtual key state diffing
        foreach (ushort vk in state.ActiveVirtualKeys)
        {
            if (_currentlyPressedKeys.Add(vk))
            {
                inputs.Add(CreateKeyInput(vk, keyUp: false));
            }
        }

        var keysToRelease = _currentlyPressedKeys
            .Where(k => !state.ActiveVirtualKeys.Contains(k))
            .ToList();

        foreach (ushort vk in keysToRelease)
        {
            _currentlyPressedKeys.Remove(vk);
            inputs.Add(CreateKeyInput(vk, keyUp: true));
        }

        if (inputs.Count > 0)
        {
            INPUT[] arr = inputs.ToArray();
            _ = SendInput((uint)arr.Length, arr, Marshal.SizeOf<INPUT>());
        }
    }

    public void ReleaseAllKeys()
    {
        if (_currentlyPressedKeys.Count == 0) return;

        var inputs = _currentlyPressedKeys
            .Select(vk => CreateKeyInput(vk, keyUp: true))
            .ToArray();
        _currentlyPressedKeys.Clear();

        if (inputs.Length > 0)
        {
            _ = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        }
    }

    private static INPUT CreateKeyInput(ushort vk, bool keyUp)
    {
        return new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = vk,
                    dwFlags = keyUp ? KEYEVENTF_KEYUP : 0
                }
            }
        };
    }

    public void Dispose()
    {
        ReleaseAllKeys();
    }
}
