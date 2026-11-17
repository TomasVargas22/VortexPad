using System.Globalization;
using VortexPad.Core.Models;
using VortexPad.Core.Processing;
using VortexPad.Core.Profiles;

namespace VortexPad.Core.Automation;

public sealed class MacroAndTurboEngine
{
    // Windows Virtual Key codes for WASD
    public const ushort VK_W = 0x57;
    public const ushort VK_A = 0x41;
    public const ushort VK_S = 0x53;
    public const ushort VK_D = 0x44;

    private readonly Dictionary<ControllerButton, double> _turboPhaseSeconds = new();
    private readonly Dictionary<string, ActiveMacroExecution> _activeMacros = new();
    private readonly HashSet<string> _prevMacroTriggers = new();
    private readonly GyroProcessor _gyroProcessor = new();

    private sealed class ActiveMacroExecution
    {
        public required MacroDefinition Definition { get; init; }
        public int CurrentStepIndex { get; set; }
        public double StepElapsedMs { get; set; }
        public HashSet<ControllerButton> HeldButtons { get; } = new();
        public HashSet<ushort> HeldKeys { get; } = new();
    }

    public void Reset()
    {
        _turboPhaseSeconds.Clear();
        _activeMacros.Clear();
        _prevMacroTriggers.Clear();
        _gyroProcessor.Reset();
    }

    /// <summary>
    /// Evaluates whether a turbo-enabled button should be pressed at the current tick.
    /// </summary>
    public bool EvaluateTurbo(ControllerButton button, bool physicallyHeld, TurboButtonConfig config, double deltaSeconds)
    {
        if (!physicallyHeld)
        {
            _turboPhaseSeconds.Remove(button);
            return false;
        }

        if (!config.Enabled || config.FrequencyHz <= 0.1)
        {
            return true;
        }

        double freq = Math.Clamp(config.FrequencyHz, 0.5, 100.0);
        double period = 1.0 / freq;
        double duty = Math.Clamp(config.DutyCycle, 0.1, 0.9);

        if (!_turboPhaseSeconds.TryGetValue(button, out double elapsed))
        {
            _turboPhaseSeconds[button] = 0.0;
            return true; // Immediate press on initial edge
        }

        elapsed = (elapsed + Math.Max(0.0, deltaSeconds)) % period;
        _turboPhaseSeconds[button] = elapsed;
        return elapsed < (period * duty);
    }

    /// <summary>
    /// Full real-time pipeline transforming PhysicalInputState + ControllerProfile into VirtualOutputState.
    /// </summary>
    public VirtualOutputState ProcessFrame(
        PhysicalInputState physical,
        ControllerProfile profile,
        double deltaSeconds)
    {
        var output = new VirtualOutputState
        {
            TargetType = profile.TargetControllerType
        };

        ParseLightbarColor(profile.LightbarHex, out byte r, out byte g, out byte b);
        output.LightbarR = r;
        output.LightbarG = g;
        output.LightbarB = b;

        // 1. Resolve active mapping layer (Shift Layer if modifier button is held, otherwise Base Layer)
        MappingLayer activeLayer = ResolveActiveLayer(physical, profile);

        // 2. Process analog sticks & triggers
        var (lx, ly) = SignalProcessor.ProcessStick2D(physical.LX, physical.LY, profile.LeftStick);
        var (rx, ry) = SignalProcessor.ProcessStick2D(physical.RX, physical.RY, profile.RightStick);
        double l2 = SignalProcessor.ProcessTrigger(physical.L2, profile.LeftTrigger);
        double r2 = SignalProcessor.ProcessTrigger(physical.R2, profile.RightTrigger);

        // 3. Process Gyroscope
        var (gyroX, gyroY, gyroActive) = _gyroProcessor.Process(physical, profile.Gyro);
        if (gyroActive)
        {
            if (profile.Gyro.OutputMode == GyroOutputMode.RightStick)
            {
                rx = Math.Clamp(rx + gyroX, -1.0, 1.0);
                ry = Math.Clamp(ry + gyroY, -1.0, 1.0);
            }
            else if (profile.Gyro.OutputMode == GyroOutputMode.MouseDelta)
            {
                output.MouseDeltaX += gyroX;
                output.MouseDeltaY += gyroY;
            }
        }

        // 4. Stick-to-WASD and Stick-to-Mouse conversion when configured on active layer
        if (activeLayer.LeftStickToWasd)
        {
            const double threshold = 0.35;
            if (ly > threshold) output.ActiveVirtualKeys.Add(VK_W);
            if (ly < -threshold) output.ActiveVirtualKeys.Add(VK_S);
            if (lx < -threshold) output.ActiveVirtualKeys.Add(VK_A);
            if (lx > threshold) output.ActiveVirtualKeys.Add(VK_D);
        }
        else
        {
            output.LX = lx;
            output.LY = ly;
        }

        if (activeLayer.RightStickToMouse)
        {
            output.MouseDeltaX += rx * activeLayer.StickMouseSensitivity;
            output.MouseDeltaY += -ry * activeLayer.StickMouseSensitivity;
        }
        else
        {
            output.RX = rx;
            output.RY = ry;
        }

        output.L2 = l2;
        output.R2 = r2;

        // 5. Process buttons with Shift Remapping + Turbo
        var turboByButton = profile.TurboConfigs.ToDictionary(t => t.Button, t => t);

        foreach (ControllerButton rawBtn in Enum.GetValues<ControllerButton>())
        {
            if (rawBtn == ControllerButton.None) continue;

            // Skip shift modifier button itself if it's currently activating a shift layer
            if (activeLayer.ShiftModifierButton.HasValue && rawBtn == activeLayer.ShiftModifierButton.Value)
            {
                continue;
            }

            bool isHeld = physical.IsPressed(rawBtn);
            if (turboByButton.TryGetValue(rawBtn, out var turboCfg))
            {
                isHeld = EvaluateTurbo(rawBtn, isHeld, turboCfg, deltaSeconds);
            }

            if (!isHeld) continue;

            // Check keyboard binding on active layer
            if (activeLayer.ButtonToKeyboardKey.TryGetValue(rawBtn, out ushort vkCode) && vkCode != 0)
            {
                output.ActiveVirtualKeys.Add(vkCode);
                continue;
            }

            // Remap or default 1:1
            ControllerButton mappedBtn = activeLayer.ButtonRemaps.TryGetValue(rawBtn, out var targetBtn)
                ? targetBtn
                : rawBtn;

            if (mappedBtn != ControllerButton.None)
            {
                output.ActiveButtons.Add(mappedBtn);
            }
        }

        // 6. Advance and apply Macros
        UpdateMacros(physical, profile, deltaSeconds, output);

        return output;
    }

    private static MappingLayer ResolveActiveLayer(PhysicalInputState physical, ControllerProfile profile)
    {
        foreach (var shiftLayer in profile.ShiftLayers)
        {
            if (shiftLayer.ShiftModifierButton.HasValue &&
                physical.IsPressed(shiftLayer.ShiftModifierButton.Value))
            {
                return shiftLayer;
            }
        }
        return profile.BaseLayer;
    }

    private void UpdateMacros(
        PhysicalInputState physical,
        ControllerProfile profile,
        double deltaSeconds,
        VirtualOutputState output)
    {
        double deltaMs = Math.Max(0.0, deltaSeconds * 1000.0);

        foreach (var macro in profile.Macros)
        {
            if (macro.Steps.Count == 0 || macro.TriggerButton == ControllerButton.None)
                continue;

            bool triggerHeld = physical.IsPressed(macro.TriggerButton);
            bool wasHeld = _prevMacroTriggers.Contains(macro.Id);

            if (triggerHeld && !wasHeld)
            {
                _prevMacroTriggers.Add(macro.Id);
                if (!_activeMacros.ContainsKey(macro.Id))
                {
                    _activeMacros[macro.Id] = new ActiveMacroExecution
                    {
                        Definition = macro,
                        CurrentStepIndex = 0,
                        StepElapsedMs = 0
                    };
                }
            }
            else if (!triggerHeld && wasHeld)
            {
                _prevMacroTriggers.Remove(macro.Id);
                if (macro.RepeatWhileHeld)
                {
                    _activeMacros.Remove(macro.Id);
                }
            }
        }

        var finishedMacroIds = new List<string>();

        foreach (var (macroId, exec) in _activeMacros)
        {
            double remainingMs = deltaMs;
            while (exec.CurrentStepIndex < exec.Definition.Steps.Count)
            {
                var step = exec.Definition.Steps[exec.CurrentStepIndex];
                if (exec.StepElapsedMs == 0.0)
                {
                    ApplyStepStart(step, exec);
                }

                double stepDuration = Math.Max(1.0, step.DurationMs);
                double needed = stepDuration - exec.StepElapsedMs;

                if (remainingMs >= needed)
                {
                    remainingMs -= needed;
                    ApplyStepEnd(step, exec);
                    exec.CurrentStepIndex++;
                    exec.StepElapsedMs = 0.0;
                }
                else
                {
                    exec.StepElapsedMs += remainingMs;
                    break;
                }
            }

            foreach (var btn in exec.HeldButtons)
            {
                output.ActiveButtons.Add(btn);
            }
            foreach (var vk in exec.HeldKeys)
            {
                output.ActiveVirtualKeys.Add(vk);
            }

            if (exec.CurrentStepIndex >= exec.Definition.Steps.Count)
            {
                if (exec.Definition.RepeatWhileHeld && physical.IsPressed(exec.Definition.TriggerButton))
                {
                    exec.CurrentStepIndex = 0;
                    exec.StepElapsedMs = 0.0;
                    exec.HeldButtons.Clear();
                    exec.HeldKeys.Clear();
                }
                else
                {
                    finishedMacroIds.Add(macroId);
                }
            }
        }

        foreach (var id in finishedMacroIds)
        {
            _activeMacros.Remove(id);
        }
    }

    private static void ApplyStepStart(MacroStep step, ActiveMacroExecution exec)
    {
        switch (step.StepType)
        {
            case MacroStepType.PressButton:
            case MacroStepType.TapButton:
                if (step.Button != ControllerButton.None)
                    exec.HeldButtons.Add(step.Button);
                break;
            case MacroStepType.ReleaseButton:
                exec.HeldButtons.Remove(step.Button);
                break;
            case MacroStepType.PressVirtualKey:
                if (step.VirtualKeyCode != 0)
                    exec.HeldKeys.Add(step.VirtualKeyCode);
                break;
            case MacroStepType.ReleaseVirtualKey:
                exec.HeldKeys.Remove(step.VirtualKeyCode);
                break;
        }
    }

    private static void ApplyStepEnd(MacroStep step, ActiveMacroExecution exec)
    {
        if (step.StepType == MacroStepType.TapButton)
        {
            exec.HeldButtons.Remove(step.Button);
        }
    }

    private static void ParseLightbarColor(string hex, out byte r, out byte g, out byte b)
    {
        r = 0;
        g = 229;
        b = 255;

        if (string.IsNullOrWhiteSpace(hex)) return;
        string clean = hex.Trim().TrimStart('#');
        if (clean.Length == 6 &&
            byte.TryParse(clean.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte pr) &&
            byte.TryParse(clean.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte pg) &&
            byte.TryParse(clean.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte pb))
        {
            r = pr;
            g = pg;
            b = pb;
        }
    }
}
