# Hoja de ruta — Fases de VortexPad

Las reglas de trabajo y el contexto de la arquitectura están en [CLAUDE.md](../CLAUDE.md), que Claude Code carga solo en cada sesión. Para empezar una fase basta con escribir en una sesión nueva:

> Trabaja la **Fase N** de `docs/PROMPT-FASES.md`.

**Ya hecho:** lectura HID nativa del DualSense/Edge (USB 0x01, BT 0x31, IMU calibrada con feature 0x05) y corrección de HidHide (instance IDs reales, sin `--cloak-off` global, recuperación tras crash).

## Fases

Orden recomendado; las marcadas con ⚡ son independientes y pueden ir en paralelo en ramas distintas.

### Fase 1 — DualShock 4 nativo por HID
- Parser puro `DualShock4ReportParser` en `Core/Hid` (USB report 0x01, BT 0x11 con offset 3; IMU y calibración desde el feature report 0x02 por USB y 0x05 por BT).
- Reader en Input reutilizando el patrón de `DualSenseHidReader` (extraer una base común si tiene sentido).
- Que la enumeración prefiera HID nativo para DS4 (PIDs 0x05C4, 0x09CC y el adaptador inalámbrico 0x0BA0) y deje de usar DirectInput para Sony.
- **Hecho cuando**: DS4 con botones, sticks, gatillos y giroscopio correctos; tests del parser con reportes sintéticos USB y BT.

### Fase 2 — Pipeline de 1000 Hz sin asignaciones y thread-safe ⚡
- `ProcessFrame` sin asignaciones por frame: reutilizar `VirtualOutputState` (o doble buffer), cachear `Enum.GetValues`, precalcular el diccionario de turbo al cargar el perfil y evitar LINQ en la ruta caliente.
- Snapshot inmutable del perfil: la UI edita una copia y la publica con `Volatile.Write`/`Interlocked.Exchange`; el hilo de polling solo lee la referencia publicada.
- `_latestPhysical`/`_latestVirtual` publicados de forma segura para el timer de UI.
- **Hecho cuando**: test o benchmark que demuestre 0 bytes asignados por `ProcessFrame` en estado estable (`GC.GetAllocatedBytesForCurrentThread`) y que los tests existentes siguen pasando.

### Fase 3 — Force Feedback y LED hacia el mando físico
- Reenviar la vibración de ViGEm al mando real aplicando `ForceFeedbackConfiguration` (`OverallStrength`, ganancias por motor).
- DualSense: output report USB 0x02 / BT 0x31 (con CRC32 y semilla 0xA2); DS4: USB 0x05 / BT 0x11. Motores, lightbar (desde el LED del DS4 virtual o `LightbarHex` del perfil) y LED de jugador.
- Escritura en el hilo del reader o en uno dedicado, con tasa limitada (solo cuando cambia el valor).
- **Hecho cuando**: el botón "Probar vibración" hace vibrar el mando real; builders de output report con tests (incluido el CRC de BT).

### Fase 4 — Macros y calibración correctas ⚡
- El botón "Macro combo" debe inyectar en el pipeline (cola de macros del `MacroAndTurboEngine`), no escribir en ViGEm desde el hilo de UI.
- La calibración de drift debe muestrear durante ~2 s en el hilo de polling (no 64 lecturas instantáneas), con barra de progreso y la opción de cancelar.
- Revisar `RepeatWhileHeld` y que las macros liberen sus botones al cambiar de perfil.
- **Hecho cuando**: hay tests del engine para la macro disparada por la UI y para la calibración con muestras en el tiempo.

### Fase 5 — UI: edición completa del perfil (y migración a MVVM)
- Sticks izquierdo y derecho editables por separado, gatillos por separado, curva Bezier con puntos arrastrables.
- Editores para el modo de activación del giroscopio, las capas Shift (remaps y teclas), los turbos por botón y un editor de macros por pasos.
- Migrar progresivamente a MVVM (CommunityToolkit.Mvvm) empezando por el panel de perfil; validar los rangos en el ViewModel.
- Renombrar y eliminar perfiles; importar y exportar JSON.
- **Hecho cuando**: todo lo que soporta `ControllerProfile` se puede editar desde la UI y se persiste; tests de los ViewModels.

### Fase 6 — Hot-plug, persistencia y robustez ⚡
- Detectar conexión y desconexión (`DeviceList.Local.Changed` de HidSharp) y refrescar la lista sin reiniciar; recordar y reseleccionar el último mando.
- Guardar los ajustes de la app (último perfil, último dispositivo, auto-switch on/off, inicio con Windows, minimizar a bandeja) en `%LOCALAPPDATA%\VortexPad\settings.json`.
- Logging a archivo (rotativo) con los errores de ViGEm, HidHide y HID; quitar los `catch {}` silenciosos donde oculten fallos reales.
- **Hecho cuando**: desconectar y reconectar el mando con la emulación activa se recupera solo, y los errores quedan registrados en el log.

### Fase 7 — Tests de Input/Emulation y CI ⚡
- Abstraer `IHidHideCli` (ejecución del proceso) para testear `HidHideIntegrationService` (solo deshace lo propio, recuperación de sesión, errores del CLI).
- Proyecto `VortexPad.WindowsTests` (net8.0-windows) para Input/Emulation con fakes.
- GitHub Actions en `windows-latest`: restore, build y test en cada push y PR.
- **Hecho cuando**: el workflow de CI pasa en verde y HidHide tiene tests de sus casos críticos.

### Fase 8 — Extras DualSense
- Touchpad como ratón y gestos (dos dedos = scroll, clic de zona izquierda/derecha → botones configurables).
- Gatillos adaptativos (efectos de resistencia y vibración vía output report) configurables por perfil.
- Botones Fn del Edge y Mute como entradas mapeables (ampliar `ControllerButton` sin romper los JSON existentes).
- **Hecho cuando**: hay opciones en el perfil, funcionan por USB y BT y hay tests de los parsers y builders.

### Fase 9 — Empaquetado y distribución
- Publicación self-contained win-x64 (single-file si es viable con WPF), icono propio y manifiesto.
- Comprobación al iniciar de ViGEmBus y HidHide con enlaces de instalación; elevación solo cuando HidHide la necesite (no siempre).
- Instalador (WiX o Inno Setup) y workflow de release en GitHub que adjunte los binarios al crear un tag.
- **Hecho cuando**: un `git tag v0.x.0` + push genera un release descargable e instalable en un Windows limpio.

## Cómo empezar cada fase

1. Confirma la fase y muéstrame en 5–8 líneas el plan y los archivos que vas a tocar (si hay una decisión de diseño real, pregúntame antes de codificar).
2. Crea la rama, implementa, agrega los tests, compila y corre los tests.
3. Cierra según la regla 7 con los comandos de git para que yo haga el commit.
