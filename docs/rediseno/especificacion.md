# R4 — Especificación para implementar (alternativa A aprobada)

Fuente visual: [Maquetas VortexPad](https://claude.ai/artifact/AuYzSAA4XY6nj4dMfyorYX), tableros A1–A7.
Tokens y componentes: [sistema VortexPad](https://claude.ai/artifact/5TM6fq6oQC2r2fmoZk4iQ7) y [sistema-diseno.md](sistema-diseno.md).
Correcciones de la crítica: [maquetas.md](maquetas.md). Esta especificación manda sobre la maqueta cuando difieren.

## 1. Estructura de la ventana

```
Window (MinWidth 1120, MinHeight 720, tamaño inicial 1360×840, Background=bg)
└─ Grid: columnas [Auto riel | *]
   ├─ Views/NavRail            (232 px expandido · 64 px colapsado)
   └─ Grid: filas [56 barra de estado | Auto avisos | * contenido]
      ├─ Views/StatusBar
      ├─ ItemsControl NoticesBar  (0–n avisos apilados, gap space-2)
      └─ ContentControl ActiveSection (una vista por sección; ScrollViewer vertical propio)
```

Vistas nuevas en `VortexPad.App/Views/`: `NavRail`, `StatusBar`, `LivePanelView`, `ProfilesView`,
`EditorSticksView`, `EditorButtonsView`, `EditorGyroView`, `EditorFeedbackView`, `MacrosView`,
`DualSenseView`, `SettingsView`. `ProfileEditorView` se divide entre las vistas del editor (mismo
`DataContext = ProfileEditorViewModel`). La sección activa se guarda en un `NavigationState` sencillo
(`enum AppSection` + `INotifyPropertyChanged`) en `VortexPad.ViewModels`, testeable en CoreTests.

### Secciones del riel y atajos

| Orden | Sección | Glifo | Atajo | Vista |
|---|---|---|---|---|
| 1 | Panel en vivo | E9D2 | Ctrl+1 | LivePanelView |
| 2 | Perfiles | E77B | Ctrl+2 | ProfilesView |
| — | *grupo «Editor»* | | Ctrl+3 = última sección del editor abierta | |
| 3 | Sticks y gatillos | E879 | | EditorSticksView |
| 4 | Botones y capas | E8B9 | | EditorButtonsView |
| 5 | Giroscopio | E7B3 | | EditorGyroView |
| 6 | Vibración y luz | E877 | | EditorFeedbackView |
| 7 | Macros y turbo | E945 | Ctrl+4 | MacrosView |
| 8 | DualSense | E7FC | | DualSenseView — **solo visible si el mando físico es DualSense** |
| — | *grupo «App»* | | | |
| 9 | Ajustes | E713 | Ctrl+5 | SettingsView |

Otros atajos: **F5** iniciar/detener emulación (también con la ventana en cualquier sección),
**Ctrl+Tab / Ctrl+Mayús+Tab** perfil siguiente/anterior, **Ctrl+B** colapsar/expandir riel.
Se registran como `InputBindings` de la ventana con `RoutedCommand`s.

## 2. Responsive

| Ancho de ventana | Cambios |
|---|---|
| ≥ 1280 px | Riel expandido (232). Barra con chips individuales: Emulación · ViGEmBus · HidHide · Guardado. Paneles en 2 columnas. |
| 1120–1279 px | Riel colapsado a 64 (solo glifos 44×44 con tooltip «Nombre · atajo»). Chips de ViGEmBus/HidHide/Guardado se agrupan en un chip resumen («Todo listo», o el peor estado: «HidHide requiere permisos»); clic abre un `Popup` con los tres. Botón primario «Detener»/«Iniciar» sin «emulación». Nombre de mando y perfil con `TextTrimming=CharacterEllipsis` (máx. 220 / 200 px). Padding de contenido `space-4` en vez de `space-5`. |
| Alto < 800 px | Cada sección hace scroll vertical dentro de `ActiveSection`; barra de estado y riel quedan fijos. |

El usuario puede fijar el riel colapsado (Ctrl+B o botón «Expandir el menú») y se recuerda en `AppSettings`
(nueva propiedad `NavRailCollapsed`; si es `null` se decide por ancho).

## 3. Tokens por elemento

Recursos en `Themes/Tokens.xaml` con las mismas claves que `tokens.json` (`Brush.Bg`, `Brush.Surface`,
`Brush.Accent`…; `Space.4` como `Thickness`/`Double`; `Radius.Md` como `CornerRadius`; `Font.Mono`…).

| Elemento | Fondo | Borde | Texto | Medidas |
|---|---|---|---|---|
| Ventana | `bg` | — | `text` | — |
| Riel | `surface` | derecha 1 px `line-strong` | ítems `text-muted`; seleccionado `text` sobre `accent-soft` + barra 3 px `accent` | ítem alto `control-md` (36), padding `space-3`, radio `md`; grupo `label` `text-faint` |
| Barra de estado | `surface` | inferior 1 px `line-strong` | mando `hero`; rótulo `caption` `text-muted` | alto `topbar-h` 56, padding horizontal `space-5`, gap `space-4` |
| Chip | `*-soft` del estado | — | color del estado + icono | alto 24, radio `pill`, padding `space-2` |
| Botón primario | `accent` → hover `accent-hover` → pulsado `accent-pressed` | — | `accent-ink` | `control-lg` 44 en la barra, `control-md` en el resto |
| Botón normal | `surface-2` | 1 px `line-control` | `text` | `control-md`, `sm` = `control-sm` 28 |
| Card | `surface` | 1 px `line` | título `heading`, subtítulo `secondary` `text-muted` | padding `space-4`, gap `space-3`, radio `lg` |
| Aviso | `info/warn/danger-soft` | izquierda 3 px del color | `text`; negrita para el qué | padding `space-3`, radio `md` |
| Lecturas numéricas | — | — | `data` / `data-lg` (`Cascadia Mono`) | — |
| Foco (todo control) | — | `FocusVisualStyle`: 2 px `accent`, separación 2 px, radio del control + 2 | — | — |

Prohibido: `#RRGGBB` literales fuera de `Themes/`, `text-faint` o `line-control` sobre `surface-3`.

## 4. Estados e interacciones

| Elemento | Estado | Comportamiento |
|---|---|---|
| Botón primario Iniciar/Detener | Deshabilitado | Sin ViGEmBus o sin mando (y sin modo teclado/ratón). Tooltip visible deshabilitado (`ShowOnDisabled`) con el motivo. |
| | Ocupado | Mientras arranca el motor: texto «Iniciando…», spinner 16 px, deshabilitado. |
| Chip Guardado | Guardando / Guardado / Error | «Guardando…» (info) mientras corre el debounce; «Guardado» (ok); «No se pudo guardar» (danger) con clic → reintentar y abrir registro. |
| Ítem del riel | hover / pressed / seleccionado / foco | `surface-2` / `surface-3` / `accent-soft` + barra / anillo de foco. |
| Slider | arrastre / teclado | Flechas ±1 paso, RePág/AvPág ±10, Inicio/Fin a límites. Valor mono a la derecha del rótulo. |
| Curva Bézier | puntos | Foco con Tab, flechas mueven 0,01 (Mayús 0,1). Además dos campos numéricos P1/P2 bajo la curva. Solo visible con tipo «Personalizada». |
| Segmented | teclado | Un único tab stop; flechas cambian la opción (patrón radiogroup). |
| Lista de perfiles | selección | Clic o Enter cambia el perfil activo. Presets incluidos: solo lectura, con «Duplicar para editar». |
| Borrar perfil/macro | confirmación | Diálogo «¿Borrar «Elden Ring · precisión»? No se puede deshacer.» → «Borrar perfil» / «Cancelar». |

### Autoguardado (C1)

`ProfileEdited` → debounce de 800 ms en hilo de UI → `ProfileRepository.Save` en `Task.Run` (nunca en el
tick de 1000 Hz). Al cerrar la ventana o cambiar de perfil se fuerza el guardado pendiente. Se quita el botón
«Guardar». Estado expuesto como `SaveStatus` (`Idle | Pending | Saved | Failed`) en un `ProfileAutosaver`
de Core/ViewModels con test (debounce, flush al cerrar, error).

## 5. Animación

Solo si `SystemParameters.ClientAreaAnimation` es `true`; si no, cambios instantáneos.

| Elemento | Disparo | Animación | Duración | Curva |
|---|---|---|---|---|
| Fondo de botones, ítems, chips | hover/pressed | color | `fast` 120 ms | ease-out |
| Cambio de sección | navegar | opacidad 0→1 + desplazamiento Y 8→0 px | `base` 180 ms | ease-out |
| Aviso | aparece / se cierra | opacidad + altura | `base` 180 ms | ease-out / ease-in-out |
| Riel | colapsar/expandir | ancho 232↔64 | `slow` 280 ms | ease-in-out |
| Radares, medidores, curva | en vivo | sin animación (valor directo a 60 fps vía `CompositionTarget.Rendering`) | — | — |

## 6. Mapa maqueta → binding

Leyenda: **VM** = binding a ViewModel existente · **CB** = code-behind de `MainWindow.xaml.cs` existente
(se mueve a la vista o se expone como evento) · **NUEVO** = hay que crearlo en R5.

### Barra de estado (todas las pantallas)

| Elemento | Origen |
|---|---|
| Rótulo «Mando físico» + nombre | CB `CmbPhysicalDevices` (selección actual). Clic → menú con la lista + «Buscar mandos» (`BtnRefreshDevices_Click`) |
| Chip Emulación | CB estado de `BtnToggleEngine` (motor activo) + `TargetControllerType` |
| Chip ViGEmBus | CB `TxtVigemStatus` → pasa a un `StatusItem` (texto + severidad) |
| Chip HidHide | CB `TxtHidHideStatus` → `StatusItem` |
| Chip Guardado | **NUEVO** `ProfileAutosaver.SaveStatus` |
| Selector de perfil | CB `LstProfiles` (misma fuente) en un `ComboBox`/menú; Ctrl+Tab |
| Botón Iniciar/Detener | CB `BtnToggleEngine_Click` |
| Pie «Listo» `TxtFooterStatus` | **Se elimina**: los mensajes pasan a chips o avisos |

### Avisos (A6)

| Elemento | Origen |
|---|---|
| Lista de avisos | CB `NoticesBar` + `DependencyNotices` (sin cambios de lógica) |
| Botón de acción («Descargar ViGEmBus», «Reiniciar como administrador») | CB `NoticeAction_Click` / `RestartAsAdministrator` |
| «Comprobar de nuevo» | **NUEVO** botón que llama a `RefreshNotices()` |
| Cerrar aviso | CB `NoticeDismiss_Click` (`aria-label` «Ocultar aviso de …») |
| «Se desconectó el …» | CB hot-plug (`HidDeviceWatcher`) → **NUEVO** aviso `warn` temporal; se retira al reconectar |

### A1 · Panel en vivo (`LivePanelView`)

| Elemento | Origen |
|---|---|
| Radares izq./der. | CB `CanvasLeftStick`, `EllipseLeftDeadzone`, `DotLeftRaw`, `DotLeftOut`, `TxtLeftStickCoords` y equivalentes `Right*` (`UpdateLiveVisualizerUi`, `UpdateDeadzoneCircles`) |
| Subtítulo «Zona muerta 8,0 % · lineal» | VM `LeftStick.InnerDeadzone`, `LeftStick.CurveType` |
| Medidores LT/RT | CB `BarL2`, `TxtL2Value`, `BarR2`, `TxtR2Value`; marca del umbral VM `RightTrigger.HairTriggerThreshold` (visible si `HairTriggerMode`) |
| Botones del mando virtual | CB `PillBtnSouth…PillBtnR3`, `LblBtn*` (las etiquetas cambian Xbox/PS según `TargetControllerType`), `PillBtnTouchpad` |
| Curva del stick derecho + «Editar curva» | VM `CurveStick` (solo lectura, mismo control que el editor en modo compacto) → navega a Sticks |
| Telemetría | CB `TxtPollingHz`, `TxtLatencyJitter`, `TxtActiveProcess` (`OnPollingTelemetryUpdated`, `OnForegroundProcessChanged`) |
| Vibración y giroscopio en vivo (no estaban en A1) | CB `BarRumbleLarge`, `BarRumbleSmall`, `TxtGyroTelemetry` → card «Vibración y giroscopio» bajo Telemetría |
| Simulador en vivo | CB `ChkDemoSignal` → se mueve a Ajustes › Diagnóstico |

### A4 · Perfiles (`ProfilesView`)

| Elemento | Origen |
|---|---|
| Buscar perfil | **NUEVO** filtro de `CollectionView` sobre la lista |
| Mis perfiles / Presets incluidos | CB `LstProfiles` + `RefreshProfilesList`, separados por `IsBuiltInPreset`; «En uso» = perfil activo; meta = `LinkedProcessesText` o «Teclado y ratón» |
| Presets Xbox / PS5 / IA | CB `BtnPresetXbox/Ps5/Ai_Click` → filas de «Presets incluidos» (se eliminan los botones de la cabecera) |
| Nuevo perfil | CB `BtnCloneProfile_Click` (clona el actual) |
| Duplicar | CB `BtnCloneProfile_Click` |
| Importar… / Exportar… | CB `BtnImportProfile_Click` / `BtnExportProfile_Click` |
| Borrar | CB `BtnDeleteProfile`, deshabilitado si `IsBuiltInPreset` |
| Nombre | VM `Name` |
| Descripción (no estaba en A4) | VM `Description`, campo multilínea bajo el nombre |
| Mando virtual (segmented) | VM `TargetControllerType` (`EditorOptions.TargetControllers`) |
| Activar al abrir el juego | VM `LinkedProcessesText` (varios separados por coma; ayuda lo explica) |
| Color de la barra de luz | VM `LightbarHex`, error VM `LightbarError`; muestra CB `BdLightbarPreview` |
| Ocultar el mando físico con HidHide | VM `HidePhysicalControllerWithHidHide` (es **por perfil**: se mueve aquí desde Ajustes) |
| Frecuencia de polling | VM `PollingRateHz` (`EditorOptions.PollingRates`; es **por perfil**: se mueve aquí desde Ajustes) |
| Ajustar por género (IA) | CB `CmbAiGenre` + `BtnApplyAiGenre_Click`, `TxtAiDeviceSignature` → card «Ajuste automático» con textos en español |
| Resumen | VM `CurveStick.CurveType`, `LeftStick.InnerDeadzone`, `ShiftLayers.Count + 1`, `Macros.Count` |
| Título y descripción en cabecera | CB `TxtActiveProfileTitle`, `TxtActiveProfileDesc` (`RefreshProfileHeader`) → se sustituyen por el selector de la barra |

### A2 · Sticks y gatillos (`EditorSticksView`)

| Elemento | Origen |
|---|---|
| Selector Izquierdo/Derecho/Gatillos | VM `CurveStickIndex` (0/1) + **NUEVO** índice 2 = gatillos (muestra `LeftTrigger`/`RightTrigger`) |
| Restablecer stick | **NUEVO** `ResetStickCommand` (valores del preset base) |
| Calibrar drift… | CB `BtnAiCalibrateDrift_Click`, `BarCalibration`, `TxtAiCalibrationResult` → diálogo/flyout con barra de progreso y resultado |
| Tipo de curva | VM `CurveStick.CurveType` (`EditorOptions.CurveTypes`) |
| Exponente | VM `CurveStick.CurveExponent` (visible `UsesExponent`) |
| Curva + puntos | CB `CanvasCurve`, `PolyCurve`, `HandleP1/P2`, `LineHandle1/2`, `DotLive`, `TxtCurveSummary`; VM `BezierP1/BezierP2` (visible `IsBezier`) |
| Zona muerta interior/exterior | VM `InnerDeadzone`, `OuterDeadzone` |
| Forma | VM `DeadzoneMode` (`EditorOptions.DeadzoneModes`); eje VM `AxialDeadzone` (visible `UsesAxialDeadzone`) |
| Sensibilidad | VM `Sensitivity` |
| Anti zona muerta | VM `AntiDeadzone` |
| Invertir X/Y | VM `InvertX`, `InvertY` |
| Gatillos: zonas, sensibilidad, curva | VM `LeftTrigger/RightTrigger.InnerDeadzone`, `OuterDeadzone`, `Sensitivity`, `CurveType`, `CurveExponent` |
| Disparo rápido + umbral | VM `HairTriggerMode`, `HairTriggerThreshold` |
| Vista previa | Radar compacto reutilizando el mismo control del panel |

### Botones y capas (`EditorButtonsView`) — sin maqueta propia; usa el patrón lista + detalle de A3

| Elemento | Origen |
|---|---|
| Lista de capas | VM `AllLayers`, `SelectedLayer`; añadir/quitar `AddShiftLayerCommand` / `RemoveShiftLayerCommand` |
| Nombre de capa, botón modificador | VM `Name`, `ShiftModifierButton` (visible `IsShiftLayer`) |
| Stick a WASD / ratón, sensibilidad | VM `LeftStickToWasd`, `RightStickToMouse`, `StickMouseSensitivity` |
| Reasignaciones | VM `Remaps` (`Source`, `Target`), `AddRemapCommand`, `RemoveRemapCommand`; aviso con `HasDuplicateSources` |
| Teclas | VM `KeyBindings` (`Source`, `VirtualKey` de `VirtualKeyCatalog`), `AddKeyBindingCommand`, `RemoveKeyBindingCommand` |

### Giroscopio (`EditorGyroView`)

VM `Gyro.OutputMode`, `ActivationMode`, `ActivationButton` (visible `NeedsActivationButton`), `SensitivityX`,
`SensitivityY`, `NoiseThresholdDegPerSec`, `SmoothingFactor`, `InvertYaw`, `InvertPitch`; todo deshabilitado
si `!IsEnabled` salvo el modo. Lectura en vivo: CB `TxtGyroTelemetry`.

### Vibración y luz (`EditorFeedbackView`)

VM `ForceFeedback.Enabled`, `OverallStrength`, `LargeMotorGain`, `SmallMotorGain`; «Probar vibración»
CB `BtnTestRumble_Click`; medidores CB `BarRumbleLarge/Small`; color de luz VM `LightbarHex` (mismo campo que Perfiles).

### A3 · Macros y turbo (`MacrosView`)

| Elemento | Origen |
|---|---|
| Lista de macros | VM `Macros`, `SelectedMacro`; meta = `TriggerButton` + `Steps.Count` + `TotalDurationMs` |
| Nueva macro / Borrar | VM `AddMacroCommand` / `RemoveMacroCommand` (con confirmación) |
| Nombre, Se activa con | VM `Name`, `TriggerButton` |
| «Al repetir» | VM `RepeatWhileHeld` → **toggle** «Repetir mientras se mantiene» (la maqueta mostraba un select) |
| Pasos | VM `Steps` (`StepType`, `Button` si `UsesButton`, `VirtualKeyCode` si `UsesKey`, `DurationMs`); quitar `RemoveStepCommand`; ordenar `MoveStepUpCommand`/`MoveStepDownCommand` (botones ↑↓ además de Alt+↑/↓) |
| Agregar pulsación / espera | VM `AddStepCommand` + tipo preseleccionado (**NUEVO** parámetro `MacroStepType`) |
| Probar | CB `BtnTriggerComboMacro_Click` (generalizado a la macro seleccionada) |
| Grabar desde el mando | **Fuera de alcance**: se quita de la maqueta en R5 |
| Duración total | VM `TotalDurationMs` |
| Turbo | VM `Turbos` (`Button`, `Enabled`, `FrequencyHz`, `DutyCycle`), `AddTurboCommand`, `RemoveTurboCommand` |
| Vacío | «Este perfil no tiene macros» + «Nueva macro» cuando `Macros.Count == 0` |

### DualSense (`DualSenseView`)

Touchpad: VM `Touchpad.Mode`, `MouseSensitivity` (visible `IsMouseMode`), `TwoFingerScroll`, `ScrollSensitivity`,
`InvertScroll`, `ClickAsMouseButtons`, `SplitClickZones`, `LeftZoneButton`/`RightZoneButton` (visible `UsesZones`).
Gatillos adaptativos (dos cards L2/R2): VM `LeftAdaptiveTrigger`/`RightAdaptiveTrigger` `.Mode`, `StartPosition`,
`EndPosition` (visible `UsesEnd`), `Strength`, `FrequencyHz` (visible `UsesFrequency`).

### A5 · Ajustes (`SettingsView`, `DataContext = AppSettingsViewModel`)

| Elemento | Origen |
|---|---|
| Abrir al iniciar Windows | VM `StartWithWindows` |
| Iniciar la emulación al abrir | VM `StartEmulationOnLaunch` |
| Al cerrar la ventana → minimizar a la bandeja | VM `MinimizeToTray` → **toggle** «Minimizar a la bandeja al cerrar» (era un select en la maqueta) |
| Cambiar de perfil según el juego | VM `AutoSwitchEnabled` |
| Carpeta de perfiles · Abrir carpeta | **NUEVO** handler que abre la ruta del `ProfileRepository` con `explorer.exe` |
| Atajos de teclado | Texto estático |
| ViGEmBus / HidHide | Mismos `StatusItem` de la barra; «Reiniciar como administrador» CB `RestartAsAdministrator` (visible solo si HidHide lo pide) |
| ~~Ocultar el mando físico~~ / ~~Frecuencia de polling~~ | Se mueven a Perfiles (son propiedades del perfil) |
| Registro · Abrir registro | CB `BtnOpenLogs_Click` |
| Simulador en vivo | CB `ChkDemoSignal_Changed` → toggle «Señal de prueba (sin mando)» en Diagnóstico |
| Versión | `Assembly` informational version |

### Bandeja del sistema

Sin cambios visuales: `InitializeSystemTray`, `ShowFromTray`, `OnWindowStateChanged`. Textos del menú revisados al glosario.

Comprobación: todos los `x:Name` y handlers actuales de `MainWindow.xaml` y `ProfileEditorView.xaml` tienen destino
en las tablas; los **NUEVO** son: `NavigationState`, `ProfileAutosaver`/`SaveStatus`, `StatusItem`, «Comprobar de
nuevo», aviso de desconexión, filtro de perfiles, `ResetStickCommand`, índice 2 del selector, parámetro de tipo en
`AddStepCommand`, abrir carpeta de perfiles, `NavRailCollapsed`.

## 7. Casos límite

- **Nombre de perfil largo** (p. ej. 60 caracteres): elipsis en barra (200 px), lista y riel; nombre completo en tooltip.
  El campo admite hasta 64 caracteres.
- **20 macros**: la lista hace scroll propio (alto máx. = alto de la card), el detalle no se mueve. Contador en el riel
  hasta «99+».
- **8 capas Shift**: lista vertical con scroll; el botón «Agregar capa» se deshabilita al llegar al máximo con tooltip
  «Máximo 8 capas».
- **Macro con 50+ pasos**: lista virtualizada (`VirtualizingStackPanel`).
- **Sin mando**: estado vacío de A6 en Panel en vivo; el editor sigue usable (edita el perfil sin vista previa).
- **Modo teclado y ratón**: chip «Emulando · Teclado y ratón»; ViGEmBus no bloquea el botón Iniciar.
- **DualSense desconectado con la sección DualSense abierta**: la sección se queda visible con aviso hasta navegar fuera.
- **Textos en otros idiomas**: no aplica (solo español), pero todos los textos van en recursos para no partir layouts
  con cadenas largas; los botones nunca tienen ancho fijo.
- **Escalado de Windows 150 %/200 %**: medidas en DIP; iconos de fuente (sin bitmaps salvo el logo de 256 px).

## 8. Accesibilidad

- **Orden de foco**: riel → barra de estado (mando, perfil, chips, Iniciar/Detener) → avisos → contenido de izquierda a
  derecha y de arriba abajo. `KeyboardNavigation.TabNavigation="Local"` por zona; F6 salta entre zonas.
- **Nombres**: `AutomationProperties.Name` en todo control sin texto (riel colapsado, iconbuttons, radares, curva);
  sin emoji en ningún nombre.
- **Roles**: riel como lista de navegación con el seleccionado `IsSelected`; segmentados como `RadioButton` en grupo;
  toggles como `ToggleButton`.
- **Anuncios**: chips y avisos con `AutomationProperties.LiveSetting="Polite"` (errores `Assertive`); valores en vivo
  **no** se anuncian; botón «Leer estado de los botones» opcional en la card de botones.
- **Curva**: operable con teclado y equivalente numérico (ver §4).
- Validación en R6 con Narrador y solo teclado.
