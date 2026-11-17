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

    private static readonly int InputSize = Marshal.SizeOf<INPUT>();

    // Buffers reused on every tick so the 1000 Hz loop does not allocate
    private INPUT[] _inputBuffer = new INPUT[32];
    private int _inputCount;
    private readonly List<ushort> _keysToRelease = new();

    private void AddInput(in INPUT input)
    {
        if (_inputCount == _inputBuffer.Length)
        {
            Array.Resize(ref _inputBuffer, _inputBuffer.Length * 2);
        }
        _inputBuffer[_inputCount++] = input;
    }

    public void ApplyOutput(VirtualOutputState state)
    {
        if (!Enabled) return;

        _inputCount = 0;

        // 1. Sub-pixel mouse movement accumulation
        double totalX = state.MouseDeltaX + _residualMouseX;
        double totalY = state.MouseDeltaY + _residualMouseY;
        int moveX = (int)Math.Truncate(totalX);
        int moveY = (int)Math.Truncate(totalY);
        _residualMouseX = totalX - moveX;
        _residualMouseY = totalY - moveY;

        if (moveX != 0 || moveY != 0)
        {
            AddInput(new INPUT
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
                AddInput(CreateKeyInput(vk, keyUp: false));
            }
        }

        _keysToRelease.Clear();
        foreach (ushort vk in _currentlyPressedKeys)
        {
            if (!state.ActiveVirtualKeys.Contains(vk)) _keysToRelease.Add(vk);
        }

        foreach (ushort vk in _keysToRelease)
        {
            _currentlyPressedKeys.Remove(vk);
            AddInput(CreateKeyInput(vk, keyUp: true));
        }

        if (_inputCount > 0)
        {
            // SendInput only reads the first _inputCount entries of the array
            _ = SendInput((uint)_inputCount, _inputBuffer, InputSize);
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
            _ = SendInput((uint)inputs.Length, inputs, InputSize);
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
