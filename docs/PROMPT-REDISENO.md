# Prompt — Rediseño de la interfaz de VortexPad

> Para ejecutarlo: en una sesión de Claude Code abierta en este repo escribe
> **"Trabaja la etapa R1 de docs/PROMPT-REDISENO.md"** (y luego R2, R3…).
> Las reglas generales (español, ramas, commits) están en [CLAUDE.md](../CLAUDE.md).

---

## Objetivo

Rediseñar la interfaz WPF de VortexPad para que se vea y se sienta como una app de escritorio
moderna y pulida, **sin tocar el motor**: el bucle de 1000 Hz, los ViewModels y los bindings existentes
siguen funcionando igual. El rediseño se **diseña primero en Claude Design**, se aprueba con el usuario y
**después** se implementa en XAML.

## Herramientas que hay que usar

| Para qué | Herramienta |
|---|---|
| Maquetas visuales (pantallas, estados, variantes) | **Claude Design**: `Artifact` con `action: "quickstart"`, `intent: "design"` → crear desde el tipo *Design* que indique el resultado |
| Sistema de diseño (tokens + componentes) | `Artifact` quickstart con `intent: "other"` → tipo *Design System*; las maquetas de R3 se construyen con él |
| Crítica de lo actual y de cada maqueta | skill `design:design-critique` |
| Accesibilidad (contraste WCAG AA, foco, teclado, tamaños) | skill `design:accessibility-review` |
| Tokens, nombres, documentación de componentes | skill `design:design-system` |
| Textos de la UI (botones, avisos, estados vacíos) | skill `design:ux-copy` |
| Pulido, animación y detalles "invisibles" | skill `emil-design-eng:emil-design-eng` |
| Especificación para implementar | skill `design:design-handoff` |

Carga cada skill (`Skill`) **antes** de la tarea que cubre. Las maquetas y el sistema de diseño quedan como
Artifacts privados del usuario: comparte el enlace al terminar cada etapa.

## Estado actual (medido al escribir este prompt)

- **Pantalla única** de 1360×840 (mínimo 1120×720) con cabecera, franja de telemetría, barra de avisos y
  3 columnas fijas: izquierda 330 px (mando, perfiles, IA, ajustes de la app — con scroll largo),
  centro (visualizador: gatillos, radares de sticks, botones, vibración), derecha 370 px (editor de perfil).
- **Editor de perfil** con **8 pestañas** (Sticks, Gatillos, Giro, Capas, Turbo, Macros, DualSense, General)
  que no caben en una fila y se parten en dos.
- **Colores sueltos**: 93 literales `#RRGGBB` (28 distintos) en `MainWindow.xaml`, 40 (16 distintos) en
  `ProfileEditorView.xaml`; los tokens de `App.xaml` (`BgPrimary`, `BgCard`, `AccentCyan`…) apenas se usan.
- **Tipografía sin escala**: tamaños 9, 10, 10.5, 11, 11.5, 12, 16, 17 y 20 mezclados.
- **Controles nativos claros** sobre fondo oscuro: `ComboBox`, `Slider`, `CheckBox` y `ScrollBar` usan la
  plantilla por defecto de Windows (fondo blanco/gris), solo `Button` y `TabItem` tienen estilo propio.
- **Iconos con emoji** en botones (🎮 🔵 ✨ ▶ 💾 📂 📤 🗑 🎯), que dependen de la fuente del sistema.
- **Textos mezclados** español/inglés ("Rápidos", "LED LIGHTBAR", "Raw vs Procesado", "Polling Real").
- **Problemas vistos en capturas**: con la barra de avisos visible los títulos de los radares se recortan;
  la casilla "Simulador en Vivo" se superpone a los botones de presets en la cabecera; el panel de ajustes
  queda escondido al fondo de la columna izquierda.
- Archivos de UI: `VortexPad.App/App.xaml`, `MainWindow.xaml(.cs)`, `Views/ProfileEditorView.xaml(.cs)`,
  `Views/LabeledSlider.xaml(.cs)`. Lógica de UI en `VortexPad.ViewModels`.

## Restricciones

1. **WPF .NET 8 nativo**. No añadir librerías de UI pesadas (MahApps, MaterialDesignInXaml, WPF-UI…) salvo que
   el usuario lo apruebe explícitamente tras ver una comparación; preferir estilos y plantillas propias.
2. **No romper bindings ni ViewModels**: renombrar o mover un control está bien; cambiar la API de los
   ViewModels requiere tests nuevos. `dotnet test` debe seguir en verde y el CI (Release, `-warnaserror`) también.
3. **Rendimiento**: el visualizador se refresca cada 16 ms; nada de efectos costosos (sombras grandes animadas,
   blur en tiempo real) en los elementos que se actualizan por frame. El bucle de 1000 Hz no se toca.
4. **Tema oscuro primero**, con contraste **WCAG AA** (4.5:1 texto normal, 3:1 texto grande y componentes).
   Un tema claro es opcional (solo si el sistema de tokens lo permite sin duplicar XAML).
5. **Todo en español**, con el tono ya usado en la app (claro, directo, sin jerga innecesaria).
6. **Accesible por teclado**: orden de tabulación lógico, foco visible en todos los controles, atajos para
   iniciar/detener emulación.
7. **Animaciones** cortas (120–220 ms, ease-out), solo donde aporten (cambio de pestaña, aparición de avisos,
   estados hover/pressed); respetar `SystemParameters.ClientAreaAnimation` (si Windows tiene las animaciones
   desactivadas, no animar).

## Etapas

Cada etapa termina con un resumen, el enlace a lo creado y la pregunta de si se aprueba antes de seguir.
**No implementar en XAML (R5) hasta que el usuario apruebe las maquetas de R3.**

### R1 — Auditoría de la interfaz actual
- Arranca la app y captura cada pantalla y estado relevante con `PrintWindow` (como en las fases anteriores):
  ventana principal, cada una de las 8 pestañas del editor, barra de avisos visible, mando desconectado,
  calibración en curso, ventana al tamaño mínimo (1120×720). Guarda las capturas en el scratchpad.
- Ejecuta `design:design-critique` (jerarquía, consistencia, densidad, flujo de tareas) y
  `design:accessibility-review` (contraste de cada par de colores usado, foco, tamaños de objetivo).
- Lista las **tareas principales del usuario** y cuántos clics cuestan hoy: elegir mando, cambiar de perfil,
  iniciar/detener emulación, ajustar deadzone/curva, crear una macro, ver si algo falla (drivers, HidHide).
- **Hecho cuando**: hay un informe (Artifact o `docs/rediseno/auditoria.md`) con capturas, hallazgos
  priorizados (crítico / importante / menor) y las métricas de colores y tipografías.

### R2 — Sistema de diseño
- Con `design:design-system`, define los **tokens**: paleta (fondos por elevación, bordes, texto primario/
  secundario/deshabilitado, acento cian de la marca, estados éxito/aviso/error/info), escala tipográfica
  (Segoe UI Variable / Segoe UI, 5–6 tamaños como máximo), espaciado en rejilla de 4 px, radios, elevación,
  duración y curvas de animación.
- Define los **componentes** con todos sus estados (normal, hover, pressed, foco, deshabilitado, error):
  botón primario/secundario/fantasma/peligro, botón de icono, ComboBox, Slider con valor, CheckBox y
  ToggleSwitch, TabControl (o navegación lateral), tarjeta, aviso (info/aviso/error), barra de scroll,
  campo de texto, chip de estado (ViGEm, HidHide, mando).
- Iconografía: **Segoe Fluent Icons / Segoe MDL2 Assets** (vienen con Windows) en lugar de emoji.
- Crea el sistema en **Claude Design** (tipo *Design System*) para que las maquetas lo usen.
- **Hecho cuando**: el Artifact del sistema de diseño existe y se aprueba; cada token tiene nombre, valor y
  uso, y los pares de color de texto cumplen AA.

### R3 — Maquetas en Claude Design
- Replantea la **arquitectura de la información** antes de pintar: por ejemplo, navegación lateral
  (Panel en vivo · Perfiles · Editor · Macros · Ajustes) en lugar de tres columnas fijas, o un panel en vivo
  siempre visible con el editor a la derecha. Propón 2 alternativas y deja elegir.
- Maqueta en **Claude Design** (tipo *Design*, con el sistema de R2): ventana principal, editor (cada sección),
  barra de avisos, ajustes de la app, estados vacíos/error (sin mando, sin ViGEmBus, mando desconectado),
  y la ventana a 1120×720.
- Pasa `design:design-critique` y `design:accessibility-review` sobre las maquetas; corrige y vuelve a
  compartir.
- Con `design:ux-copy`, revisa todos los textos: etiquetas, botones (verbo + objeto), avisos, tooltips y
  estados vacíos.
- **Hecho cuando**: el usuario aprueba una alternativa de maquetas y sus textos.

### R4 — Especificación para implementar
- Con `design:design-handoff`, genera la especificación: medidas, tokens por elemento, estados, comportamiento
  responsive (qué se colapsa por debajo de 1280 px), animaciones y casos límite (nombres de perfil largos,
  20 macros, 8 capas Shift).
- Mapea cada elemento de la maqueta a su control/binding actual (`ProfileEditorViewModel.*`,
  `AppSettingsViewModel.*`, controles con `x:Name` usados por `MainWindow.xaml.cs`).
- **Hecho cuando**: `docs/rediseno/especificacion.md` existe y no queda ningún elemento sin binding asignado.

### R5 — Implementación en WPF
- Crea `VortexPad.App/Themes/Tokens.xaml` (colores, tipografía, espaciado como recursos) y
  `Themes/Controls.xaml` (estilos y plantillas de todos los componentes de R2), fusionados en `App.xaml`.
- Rehaz `MainWindow.xaml` y `Views/*.xaml` según la maqueta aprobada. Divide la ventana en `UserControl`s
  si la nueva navegación lo pide (p. ej. `Views/LivePanelView`, `Views/SettingsView`).
- Sustituye **todos** los colores literales por tokens (objetivo: 0 literales `#RRGGBB` fuera de `Themes/`)
  y los emoji por glifos de Segoe Fluent Icons.
- Aplica el pulido de `emil-design-eng`: transiciones de estado, foco visible, alineación óptica, ritmo de
  espaciado. Respeta la restricción 7 sobre animaciones.
- Mantén funcionando: bandeja del sistema, avisos, hot-plug, calibración, macro de prueba, curva Bézier
  arrastrable, radares a 60 fps.
- **Hecho cuando**: la app compila sin advertencias, los tests pasan, `grep` no encuentra colores literales
  fuera de `Themes/` y las capturas coinciden con la maqueta aprobada.

### R6 — Verificación y cierre
- Repite las capturas de R1 y arma una comparación **antes / después** (Artifact).
- Vuelve a pasar `design:accessibility-review` sobre la app real (contraste medido en capturas, navegación
  completa solo con teclado, foco visible).
- Prueba a 1120×720, a 1920×1080 y con escalado de Windows al 125 % y 150 %.
- CI en verde; commits según `CLAUDE.md`.
- **Hecho cuando**: la comparación está compartida, no quedan hallazgos críticos de accesibilidad y el usuario
  da el visto bueno.

## Cómo trabajar cada etapa

1. Crea la rama `feature/rediseno-ui` desde `main` actualizado (una sola rama para todo el rediseño, o una por
   etapa si el usuario lo prefiere).
2. Carga las skills de la etapa y, si es una etapa de Claude Design, empieza con `Artifact` `quickstart`.
3. Al terminar: resumen corto, enlace a los Artifacts, qué debe revisar el usuario y la pregunta de aprobación.
   No hagas commit ni push salvo que el usuario lo pida.
