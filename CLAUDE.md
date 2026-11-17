# VortexPad

App de escritorio para Windows en **.NET 8 (C# / WPF)** que reemplaza a XOutput: convierte mandos físicos (DirectInput / HID / DualSense / DualShock 4 / genéricos) en mandos virtuales (Xbox 360 y DualShock 4 vía ViGEmBus, o teclado y ratón vía `SendInput`), con polling de 1000 Hz, curvas de respuesta, giroscopio, macros, HidHide y calibración asistida.

La hoja de ruta por fases está en [docs/PROMPT-FASES.md](docs/PROMPT-FASES.md).

## Comandos

- Compilar: `dotnet build .\VortexPad.sln`
- Tests: `dotnet test .\VortexPad.sln`
- Ejecutar: `dotnet run --project .\VortexPad.App\VortexPad.App.csproj -c Release`

## Arquitectura (`VortexPad.sln`)

| Proyecto | TFM | Responsabilidad |
|---|---|---|
| `VortexPad.Core` | net8.0 | Modelos (`Models/ControllerState.cs`), bucle de 1000 Hz (`Engine/HighPrecisionPollingLoop`), `Processing/SignalProcessor` + `GyroProcessor`, `Automation/MacroAndTurboEngine.ProcessFrame`, perfiles/presets + `ProfileRepository` (en `Profiles/BuiltInPresets.cs`), parsers HID puros en `Hid/` |
| `VortexPad.AI` | net8.0 | `SmartMapperAndCalibrator`: identificación VID:PID, calibración de drift 3σ, tuning por género |
| `VortexPad.Input` | net8.0-windows | `DirectInputAndHidProvider` (Vortice.DirectInput + HidSharp), `SonyHidReaderBase` (hilo propio con snapshot y reconexión) con `DualSenseHidReader` y `DualShock4HidReader`, `ForegroundGameWatcher` |
| `VortexPad.Emulation` | net8.0-windows | `ViGEmVirtualControllerService`, `NativeKeyboardMouseInjector`, `HidHideIntegrationService` |
| `VortexPad.App` | net8.0-windows | WPF `MainWindow` (code-behind, sin MVVM todavía) + icono de bandeja |
| `VortexPad.CoreTests` | net8.0 | xUnit; referencia solo Core y AI |

Flujo por tick: `OnHighPrecisionTick` → `PollCurrentState` (snapshot, no bloquea) → `ProcessFrame` → `ViGEm.SubmitState` + `KmInjector.ApplyOutput`.

## Reglas de trabajo

1. **Idioma**: responde en español. Comentarios y textos de UI en español; identificadores en inglés.
2. **Git**:
   - Antes de tocar código: `git status` y crea una rama desde `main` actualizado (`feature/<nombre>` o `fix/<nombre>`). Si depende de una rama no fusionada, créala desde esa y avisa.
   - **No hagas commit ni push** salvo que el usuario lo pida explícitamente: el usuario los hace. Al terminar, dale los comandos `git add` / `git commit` / `git push` en bloques separados, con **mensaje de commit en español** (verbo en infinitivo: "Agregar…", "Corregir…", "Optimizar…").
3. Lee los archivos implicados antes de cambiarlos; prefiere cambios acotados a reescrituras.
4. **Lógica pura → Core** (testeable desde `CoreTests`, net8.0). Lo que dependa de Win32/HidSharp/ViGEm va en Input/Emulation, detrás de una interfaz si hay que testearlo.
5. **Bucle de 1000 Hz**: nada bloqueante (I/O, procesos, locks largos) en `OnHighPrecisionTick` ni en `ProcessFrame`. La lectura de hardware va en su propio hilo con snapshot.
6. **Verificación**: `dotnet build` sin advertencias y `dotnet test` en verde; agrega tests para la lógica nueva y di claramente qué quedó sin probar con hardware real.
7. **Cierre**: resumen corto (con enlaces a archivos), qué debe probar el usuario a mano, comandos de git y la siguiente fase recomendada.

## Pendiente de validar con hardware real

- Signos de giro del giroscopio del DualSense (`InvertYaw` / `InvertPitch`).
- Formato de salida de `HidHideCLI --cloak-state` y `--dev-list` (se asume `--cloak-on`/`--cloak-off` y rutas entre comillas).
- Instance IDs de HidHide con DualSense por Bluetooth; mandos Xbox por XInput pueden seguir visibles aunque se oculte su interfaz HID.
- DualShock 4 por HID: layout de calibración del adaptador inalámbrico 0x0BA0 (se detecta por signos plus/minus), signos del giroscopio y que BT pase al reporte 0x11 tras leer el feature 0x05.
