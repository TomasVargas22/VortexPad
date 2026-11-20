# VortexPad

App de escritorio para Windows en **.NET 8 (C# / WPF)** que reemplaza a XOutput: convierte mandos físicos (DirectInput / HID / DualSense / DualShock 4 / genéricos) en mandos virtuales (Xbox 360 y DualShock 4 vía ViGEmBus, o teclado y ratón vía `SendInput`), con polling de 1000 Hz, curvas de respuesta, giroscopio, macros, HidHide y calibración asistida.

La hoja de ruta por fases está en [docs/PROMPT-FASES.md](docs/PROMPT-FASES.md). El rediseño de la interfaz (R1–R6) está en [docs/PROMPT-REDISENO.md](docs/PROMPT-REDISENO.md) y [docs/rediseno/](docs/rediseno/): usa los tokens y estilos de `Themes/` para cualquier UI nueva, nunca colores literales ni emoji.

## Comandos

- Compilar: `dotnet build .\VortexPad.sln`
- Tests: `dotnet test .\VortexPad.sln` (CoreTests + WindowsTests)
- CI: `.github/workflows/ci.yml` (windows-latest) compila en Release con `-warnaserror` y corre los tests en cada push y PR a `main`
- Ejecutar: `dotnet run --project .\VortexPad.App\VortexPad.App.csproj -c Release`
- Publicar exe (self-contained, single-file, win-x64): `dotnet publish .\VortexPad.App\VortexPad.App.csproj -c Release -r win-x64 -o publish`
- Instalador local (requiere Inno Setup 6): `iscc /DAppVersion=0.1.0 /DPublishDir=..\publish installer\VortexPad.iss` → `dist\`
- Release: `git tag vX.Y.Z` + `git push origin vX.Y.Z` → `.github/workflows/release.yml` compila, prueba, publica el exe, arma zip portable + instalador + `SHA256SUMS.txt` y crea la release en GitHub (prerelease si la versión lleva `-`). También se puede lanzar a mano (Run workflow) para probar sin publicar.
- Icono: `tools/generate-icon.ps1` regenera `VortexPad.App/Assets/vortexpad.ico`. La app arranca sin administrador (`app.manifest` asInvoker) y solo ofrece elevarse cuando HidHide lo necesita (`DependencyNotices`).

## Arquitectura (`VortexPad.sln`)

| Proyecto | TFM | Responsabilidad |
|---|---|---|
| `VortexPad.Core` | net8.0 | Modelos (`Models/ControllerState.cs`), bucle de 1000 Hz (`Engine/HighPrecisionPollingLoop`), `Processing/SignalProcessor` + `GyroProcessor`, `Automation/MacroAndTurboEngine.ProcessFrame`, perfiles/presets + `ProfileRepository` (en `Profiles/BuiltInPresets.cs`), parsers HID puros en `Hid/`, log `Diagnostics/VortexLog` (rotativo, `ErrorThrottled` para rutas calientes), ajustes `Settings/AppSettings`, `Devices/DeviceSelectionPolicy` |
| `VortexPad.AI` | net8.0 | `SmartMapperAndCalibrator`: identificación VID:PID, calibración de drift 3σ, tuning por género |
| `VortexPad.Input` | net8.0-windows | `DirectInputAndHidProvider` (Vortice.DirectInput + HidSharp), `SonyHidReaderBase` (hilo propio con snapshot y reconexión) con `DualSenseHidReader` y `DualShock4HidReader`, `HidDeviceWatcher` (hot-plug), `ForegroundGameWatcher` |
| `VortexPad.Emulation` | net8.0-windows | `ViGEmVirtualControllerService`, `NativeKeyboardMouseInjector` (con sumidero de test interno), `HidHideIntegrationService` (sobre `IHidHideCli`) |
| `VortexPad.ViewModels` | net8.0 | MVVM (CommunityToolkit.Mvvm): `ProfileEditorViewModel` y sus secciones (sticks, gatillos, gyro, vibración, capas, turbos, macros). Editan el perfil en sitio, validan rangos y avisan con `ProfileEdited`. Shell: `NavigationState` (secciones, Ctrl+1…5, riel colapsado), `StatusItem` (chips) y `Autosaver<T>` (autoguardado con debounce de 800 ms) |
| `VortexPad.App` | net8.0-windows | WPF `MainWindow`: riel de navegación + barra de estado con chips + secciones (code-behind para motor, dispositivos y panel en vivo). Editor en `Views/Editor*View`, `MacrosView`, `DualSenseView` (MVVM) y `ResponseCurveControl`. Tema en `Themes/Tokens.xaml` + `Themes/Controls.xaml` (único sitio con colores literales). Icono de bandeja |
| `VortexPad.CoreTests` | net8.0 | xUnit; referencia Core, AI y ViewModels |
| `VortexPad.WindowsTests` | net8.0-windows | xUnit para Input/Emulation con fakes (`FakeHidHideCli` simula el driver); accede a internos vía `InternalsVisibleTo` |

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
- Vibración/LED (`Core/Hid/SonyOutputReports.cs`): DualSense usa a la vez `COMPATIBLE_VIBRATION` (flag0) y `COMPATIBLE_VIBRATION2` (flag2) para cubrir firmware viejo y nuevo; DS4 BT usa `0xC4` (HID + CRC + 4 ms). Comprobar que vibra y cambia de color por USB y BT, y que la longitud de escritura (`GetMaxOutputReportLength`) es aceptada por Windows.
- Hot-plug: desconectar y reconectar el mando con la emulación activa (USB y BT, DirectInput y HID nativo). Al iniciar ViGEm se dispara un refresco de dispositivos; el DS4 virtual de ViGEm (054C:05C4) aparece como mando físico y no debe seleccionarse.
- Mandos virtuales de ViGEm: se filtran por el árbol de dispositivos (`VirtualDeviceDetector`); verificado con el Xbox 360 virtual, falta el DS4 virtual.
- DualSense extras (Fase 8): coordenadas y bits del touchpad (offset 32 de los datos), bits de Mute/Fn del Edge (b2 0x04/0x10/0x20) y bytes de los gatillos adaptativos (`AdaptiveTriggerEncoder`: modos 0x05/0x21/0x25/0x26, offsets 10 y 21 del bloque común, flags 0x04/0x08). Probar por USB y BT que el gatillo opone resistencia y que el touchpad mueve el ratón y hace scroll.

## Logs y ajustes

- Log: `%LOCALAPPDATA%\VortexPad\Logs\vortexpad.log` (rota a 1 MB, 5 archivos). Usa `VortexLog`; nunca `catch {}` silencioso salvo en limpieza (Dispose).
- Ajustes de la app: `%LOCALAPPDATA%\VortexPad\settings.json` (`AppSettingsViewModel`).
