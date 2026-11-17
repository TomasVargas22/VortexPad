using VortexPad.Core.Models;
using VortexPad.Core.Profiles;

namespace VortexPad.ViewModels;

/// <summary>Option lists (Spanish labels) for the editor's ComboBoxes.</summary>
public static class EditorOptions
{
    public static IReadOnlyList<EnumOption<VirtualControllerType>> TargetControllers { get; } = new[]
    {
        new EnumOption<VirtualControllerType>(VirtualControllerType.Xbox360, "Xbox 360 (XInput)"),
        new EnumOption<VirtualControllerType>(VirtualControllerType.DualShock4, "Sony DualShock 4 (PS4 / PS5)"),
        new EnumOption<VirtualControllerType>(VirtualControllerType.KeyboardMouse, "Teclado y ratón nativo"),
        new EnumOption<VirtualControllerType>(VirtualControllerType.HybridXboxKeyboard, "Híbrido Xbox + teclado")
    };

    public static IReadOnlyList<EnumOption<ResponseCurveType>> CurveTypes { get; } = new[]
    {
        new EnumOption<ResponseCurveType>(ResponseCurveType.Linear, "Lineal (1:1)"),
        new EnumOption<ResponseCurveType>(ResponseCurveType.Exponential, "Exponencial (precisión al centro)"),
        new EnumOption<ResponseCurveType>(ResponseCurveType.SCurve, "S-Curve (precisión + giro rápido)"),
        new EnumOption<ResponseCurveType>(ResponseCurveType.CustomBezier, "Bézier personalizada")
    };

    public static IReadOnlyList<EnumOption<DeadzoneMode>> DeadzoneModes { get; } = new[]
    {
        new EnumOption<DeadzoneMode>(DeadzoneMode.Radial, "Radial (circular)"),
        new EnumOption<DeadzoneMode>(DeadzoneMode.Axial, "Axial (por eje)"),
        new EnumOption<DeadzoneMode>(DeadzoneMode.Hybrid, "Híbrida (axial + radial)")
    };

    public static IReadOnlyList<EnumOption<GyroOutputMode>> GyroOutputModes { get; } = new[]
    {
        new EnumOption<GyroOutputMode>(GyroOutputMode.Disabled, "Desactivado"),
        new EnumOption<GyroOutputMode>(GyroOutputMode.RightStick, "Stick derecho"),
        new EnumOption<GyroOutputMode>(GyroOutputMode.MouseDelta, "Ratón")
    };

    public static IReadOnlyList<EnumOption<GyroActivationMode>> GyroActivationModes { get; } = new[]
    {
        new EnumOption<GyroActivationMode>(GyroActivationMode.AlwaysOn, "Siempre activo"),
        new EnumOption<GyroActivationMode>(GyroActivationMode.WhileL2Held, "Mientras se mantiene L2"),
        new EnumOption<GyroActivationMode>(GyroActivationMode.WhileButtonHeld, "Mientras se mantiene un botón"),
        new EnumOption<GyroActivationMode>(GyroActivationMode.ToggleButton, "Alternar con un botón")
    };

    public static IReadOnlyList<EnumOption<MacroStepType>> MacroStepTypes { get; } = new[]
    {
        new EnumOption<MacroStepType>(MacroStepType.TapButton, "Pulsar y soltar botón"),
        new EnumOption<MacroStepType>(MacroStepType.PressButton, "Mantener botón"),
        new EnumOption<MacroStepType>(MacroStepType.ReleaseButton, "Soltar botón"),
        new EnumOption<MacroStepType>(MacroStepType.PressVirtualKey, "Mantener tecla"),
        new EnumOption<MacroStepType>(MacroStepType.ReleaseVirtualKey, "Soltar tecla"),
        new EnumOption<MacroStepType>(MacroStepType.WaitMilliseconds, "Esperar")
    };

    public static IReadOnlyList<EnumOption<ControllerButton>> Buttons { get; } = new[]
    {
        new EnumOption<ControllerButton>(ControllerButton.None, "(ninguno)"),
        new EnumOption<ControllerButton>(ControllerButton.South, "A / ✕"),
        new EnumOption<ControllerButton>(ControllerButton.East, "B / ○"),
        new EnumOption<ControllerButton>(ControllerButton.West, "X / □"),
        new EnumOption<ControllerButton>(ControllerButton.North, "Y / △"),
        new EnumOption<ControllerButton>(ControllerButton.LeftShoulder, "LB / L1"),
        new EnumOption<ControllerButton>(ControllerButton.RightShoulder, "RB / R1"),
        new EnumOption<ControllerButton>(ControllerButton.Back, "View / Share"),
        new EnumOption<ControllerButton>(ControllerButton.Start, "Menu / Options"),
        new EnumOption<ControllerButton>(ControllerButton.Guide, "Guía / PS"),
        new EnumOption<ControllerButton>(ControllerButton.LeftThumb, "L3"),
        new EnumOption<ControllerButton>(ControllerButton.RightThumb, "R3"),
        new EnumOption<ControllerButton>(ControllerButton.TouchpadClick, "Touchpad"),
        new EnumOption<ControllerButton>(ControllerButton.DPadUp, "D-Pad ↑"),
        new EnumOption<ControllerButton>(ControllerButton.DPadDown, "D-Pad ↓"),
        new EnumOption<ControllerButton>(ControllerButton.DPadLeft, "D-Pad ←"),
        new EnumOption<ControllerButton>(ControllerButton.DPadRight, "D-Pad →"),
        new EnumOption<ControllerButton>(ControllerButton.Paddle1, "Paleta 1"),
        new EnumOption<ControllerButton>(ControllerButton.Paddle2, "Paleta 2")
    };

    /// <summary>Real buttons only (no "(ninguno)"), for things that require one.</summary>
    public static IReadOnlyList<EnumOption<ControllerButton>> RealButtons { get; } =
        Buttons.Where(b => b.Value != ControllerButton.None).ToArray();

    public static IReadOnlyList<EnumOption<int>> PollingRates { get; } = new[]
    {
        new EnumOption<int>(125, "125 Hz"),
        new EnumOption<int>(250, "250 Hz"),
        new EnumOption<int>(500, "500 Hz"),
        new EnumOption<int>(1000, "1000 Hz"),
        new EnumOption<int>(2000, "2000 Hz")
    };
}

/// <summary>Windows virtual keys (VK_*) usable with SendInput, with readable names.</summary>
public static class VirtualKeyCatalog
{
    public static IReadOnlyList<EnumOption<ushort>> Keys { get; } = Build();

    private static EnumOption<ushort>[] Build()
    {
        var list = new List<EnumOption<ushort>>
        {
            new(0x20, "Espacio"), new(0x0D, "Enter"), new(0x1B, "Esc"), new(0x09, "Tab"),
            new(0x08, "Retroceso"), new(0x10, "Shift"), new(0x11, "Ctrl"), new(0x12, "Alt"),
            new(0x14, "Bloq Mayús"), new(0x26, "Flecha ↑"), new(0x28, "Flecha ↓"),
            new(0x25, "Flecha ←"), new(0x27, "Flecha →"), new(0x2D, "Insert"), new(0x2E, "Supr"),
            new(0x24, "Inicio"), new(0x23, "Fin"), new(0x21, "Re Pág"), new(0x22, "Av Pág")
        };

        for (char c = 'A'; c <= 'Z'; c++) list.Add(new((ushort)c, c.ToString()));
        for (char c = '0'; c <= '9'; c++) list.Add(new((ushort)c, c.ToString()));
        for (int f = 1; f <= 12; f++) list.Add(new((ushort)(0x70 + f - 1), $"F{f}"));

        return list.ToArray();
    }

    public static string NameOf(ushort vk) =>
        Keys.FirstOrDefault(k => k.Value == vk)?.Label ?? $"VK 0x{vk:X2}";
}
