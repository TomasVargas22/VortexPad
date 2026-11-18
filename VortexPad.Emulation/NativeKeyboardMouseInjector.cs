using System.Runtime.InteropServices;
using VortexPad.Core.Models;

namespace VortexPad.Emulation;

public sealed class NativeKeyboardMouseInjector : IDisposable
{
    private const uint INPUT_MOUSE = 0;
    private const uint INPUT_KEYBOARD = 1;
    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_WHEEL = 0x0800;
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

    /// <summary>Already-translated event, used only by tests in place of SendInput.</summary>
    internal readonly record struct InjectedInput(
        bool IsMouse, int Dx, int Dy, ushort VirtualKey, bool KeyUp, uint MouseFlags = 0, int WheelData = 0);

    private readonly Action<InjectedInput[]>? _testSink;

    public NativeKeyboardMouseInjector()
    {
    }

    /// <summary>Tests: captures what would be sent instead of calling SendInput.</summary>
    internal NativeKeyboardMouseInjector(Action<InjectedInput[]> testSink)
    {
        _testSink = testSink;
    }

    private void Send(INPUT[] inputs, int count)
    {
        if (_testSink != null)
        {
            var captured = new InjectedInput[count];
            for (int i = 0; i < count; i++)
            {
                var input = inputs[i];
                captured[i] = input.type == INPUT_MOUSE
                    ? new InjectedInput(true, input.U.mi.dx, input.U.mi.dy, 0, false,
                        input.U.mi.dwFlags & ~MOUSEEVENTF_MOVE, unchecked((int)input.U.mi.mouseData))
                    : new InjectedInput(false, 0, 0, input.U.ki.wVk, (input.U.ki.dwFlags & KEYEVENTF_KEYUP) != 0);
            }
            _testSink(captured);
            return;
        }

        _ = SendInput((uint)count, inputs, InputSize);
    }

    private readonly HashSet<ushort> _currentlyPressedKeys = new();
    private double _residualMouseX;
    private double _residualMouseY;
    private double _residualWheel;
    private bool _leftButtonDown;
    private bool _rightButtonDown;

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

        // 1b. Wheel (units of 1/120 notch; Windows accepts fractions of a notch for smooth scrolling)
        double totalWheel = state.MouseWheelDelta + _residualWheel;
        int wheel = (int)Math.Truncate(totalWheel);
        _residualWheel = totalWheel - wheel;
        if (wheel != 0)
        {
            AddInput(CreateMouseInput(MOUSEEVENTF_WHEEL, unchecked((uint)wheel)));
        }

        // 1c. Mouse buttons (touchpad as mouse): only the changes
        if (state.MouseLeftButton != _leftButtonDown)
        {
            _leftButtonDown = state.MouseLeftButton;
            AddInput(CreateMouseInput(_leftButtonDown ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_LEFTUP, 0));
        }
        if (state.MouseRightButton != _rightButtonDown)
        {
            _rightButtonDown = state.MouseRightButton;
            AddInput(CreateMouseInput(_rightButtonDown ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_RIGHTUP, 0));
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
            Send(_inputBuffer, _inputCount);
        }
    }

    /// <summary>Releases every pressed key and mouse button (on stopping emulation or closing).</summary>
    public void ReleaseAllKeys()
    {
        var inputs = _currentlyPressedKeys
            .Select(vk => CreateKeyInput(vk, keyUp: true))
            .ToList();
        _currentlyPressedKeys.Clear();

        if (_leftButtonDown) inputs.Add(CreateMouseInput(MOUSEEVENTF_LEFTUP, 0));
        if (_rightButtonDown) inputs.Add(CreateMouseInput(MOUSEEVENTF_RIGHTUP, 0));
        _leftButtonDown = _rightButtonDown = false;
        _residualWheel = 0;

        if (inputs.Count > 0)
        {
            Send(inputs.ToArray(), inputs.Count);
        }
    }

    private static INPUT CreateMouseInput(uint flags, uint mouseData) => new()
    {
        type = INPUT_MOUSE,
        U = new InputUnion { mi = new MOUSEINPUT { dwFlags = flags, mouseData = mouseData } }
    };

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
