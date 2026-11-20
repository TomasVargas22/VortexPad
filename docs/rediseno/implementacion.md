# R5 — Implementación en WPF

Implementa la alternativa A según [especificacion.md](especificacion.md).

## Qué se hizo

- **Tema**: `VortexPad.App/Themes/Tokens.xaml` (colores, tipografía, espaciado, radios, tamaños y duraciones con las
  claves de `tokens.json`) y `Themes/Controls.xaml` (botones primario/normal/ghost/peligro/icono, riel, campos,
  ComboBox oscuro, checkbox, interruptor, slider, medidor, segmentado, listas, scrollbar, tooltip, chips y teclas).
  Fusionados en `App.xaml`. Fuera de `Themes/` no queda ningún `#RRGGBB` ni `Color.FromRgb`.
- **Ventana**: riel de navegación (232 px; 64 px por debajo de 1280 px o con Ctrl+B, recordado en `settings.json`),
  barra de estado con mando, chips (emulación, ViGEmBus, HidHide, guardado), perfil y botón Iniciar/Detener, área de
  avisos y mensajes (sustituye al pie «Listo») y nueve secciones.
- **Editor dividido**: `EditorSticksView`, `EditorButtonsView`, `EditorGyroView`, `EditorFeedbackView`, `MacrosView`,
  `DualSenseView` y `ResponseCurveControl` (curva reutilizada en el panel y en el editor). Se eliminó
  `ProfileEditorView` con sus 8 pestañas.
- **Lógica nueva (con tests en `CoreTests/RedesignShellTests.cs`)**: `Autosaver<T>`, `NavigationState`, `StatusItem`,
  `ResetCurveStickCommand`, `AnalogSelection` (stick izq./der./gatillos) y `AddWaitStepCommand`.
- **Autoguardado (C1)**: 800 ms después de la última edición; se fuerza al cambiar de perfil, duplicar, borrar,
  reiniciar como administrador y cerrar. Se quitó el botón «Guardar».
- **Teclado (C3)**: foco visible en todos los controles, F5, Ctrl+1…5, Ctrl+Tab / Ctrl+Mayús+Tab, Ctrl+B;
  los puntos de la curva se mueven con flechas (Mayús = pasos de 10 %); sliders con RePág/AvPág = 10 pasos.
- **Accesibilidad**: nombres accesibles en todos los controles (incluidos los botones icono + texto, vía
  `AccessibleNames`), chips y avisos con `LiveSetting`, sin emoji ni símbolos en etiquetas («A / Cruz»…).
- **Estados**: vacío sin mando, aviso de mando desconectado, «Comprobar de nuevo» tras instalar un driver,
  botón Iniciar deshabilitado con el motivo cuando falta ViGEmBus, macros vacías.
- **Animación**: solo el cambio de sección y la aparición de mensajes (180 ms, ease-out), y solo si
  `SystemParameters.ClientAreaAnimation` está activo.

## Desviaciones respecto a la especificación

- Los chips se agrupan en el chip resumen cuando **no caben** (medido), no solo por debajo de 1280 px: con nombres de
  mando y perfil largos no cabían a 1360 px.
- Las transiciones de color en hover/pulsado son instantáneas (sin animar); el riel cambia de ancho sin animación.
- Las opciones con etiquetas largas (tipo de curva, mando virtual) usan ComboBox oscuro en lugar de segmentado.
- «Grabar desde el mando» no existe en la app y no se implementó (fuera de alcance, como dice la especificación).

## Verificación hecha

- `dotnet build -c Release -warnaserror`: sin advertencias. `dotnet test`: 148 + 24 en verde.
- Capturas de todas las secciones a 1360×840 y a 1120×720, con señal de prueba y con foco de teclado.
- UI Automation: 0 controles interactivos visibles sin nombre accesible en la pantalla probada.

## Pendiente (R6)

- Narrador y uso solo con teclado en todas las secciones.
- Escalado de Windows al 150 % y 200 %.
- Con hardware real: hot-plug con el aviso de desconexión, sección DualSense apareciendo/desapareciendo,
  «Probar» macro y «Probar vibración».
