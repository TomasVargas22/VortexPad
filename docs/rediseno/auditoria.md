# R1 — Auditoría de la interfaz de VortexPad

Fecha: 19-11-2026 · Versión auditada: `main` @ `ecb9f50` (v0.1.0) · Estándar: WCAG 2.1 AA

El informe visual con capturas está en el Artifact [Auditoría de VortexPad](https://claude.ai/artifact/8odQa1w5zrnGumtFRucdrP) (privado del usuario).
Este archivo es la versión de texto para las etapas siguientes.

## Método

- 11 capturas con `PrintWindow` + UI Automation: ventana principal, las 8 pestañas del editor,
  intento de foco con teclado y ventana a 1120×720 (tamaño mínimo).
- Contraste WCAG calculado sobre los pares de color del XAML.
- Recuento de controles con UI Automation: **74 interactivos, 39 sin nombre accesible**.
- Revisión del XAML: estilos, plantillas, foco, atajos, `AutomationProperties`.

**No cubierto**: captura de la calibración en curso (la ventana cambia de estado al invocarla y la captura
sale vacía), estado "mando desconectado" (requiere desenchufar un mando), lector de pantalla real
(NVDA/Narrador) y escalado de Windows al 125/150 %. Quedan para R6.

## Hallazgos priorizados

### Críticos

| # | Hallazgo | Tipo | Recomendación |
|---|---|---|---|
| C1 | Los cambios del editor **se pierden al cerrar**: solo se guardan con "Guardar" y no hay indicador de cambios sin guardar ni aviso al salir. | Usabilidad | Autoguardado (con debounce) o indicador "sin guardar" + diálogo al cerrar. |
| C2 | A **1120×720** (mínimo permitido) la interfaz se rompe: presets de la cabecera cortados, telemetría solapada ("VortexPad.App.exeViGEmBus"), radares y matriz de botones recortados ("ew / Sha", "enu / O"), etiquetas de gatillos cortadas. | Layout | Layout adaptable: colapsar columnas o navegación lateral por debajo de ~1280 px; textos con trimming + tooltip. |
| C3 | **Sin foco visible**: las plantillas de botón/pestaña no tienen estado de foco y el `FocusVisualStyle` por defecto (punteado negro) es invisible sobre fondo oscuro. **Sin atajos** (iniciar/detener emulación, cambiar de perfil). | WCAG 2.4.7, 2.1.1 | Estado de foco en todos los componentes (anillo de 2 px con el acento) y atajos de teclado documentados. |

### Importantes

| # | Hallazgo | Tipo | Recomendación |
|---|---|---|---|
| I1 | El editor tiene **8 pestañas en dos filas** y el `TabControl` multifila **reordena las filas** al seleccionar (la pestaña elegida salta a la fila de abajo). | Usabilidad | Navegación vertical (lista lateral de secciones) o pestañas con scroll en una sola fila. |
| I2 | **ComboBox, CheckBox, ScrollBar y la selección de la lista** usan las plantillas claras de Windows sobre el tema oscuro. | Consistencia | Plantillas propias para todos los controles (R2). |
| I3 | **Jerarquía difusa**: el estado clave (¿emulando?, ¿qué mando?, ¿todo bien?) está repartido entre cabecera, franja de telemetría y pie; los **ajustes de la app** quedan al fondo de la columna izquierda con scroll. | Jerarquía | Un bloque de estado único y prominente; ajustes en su propia sección de navegación. |
| I4 | **39 de 74 controles sin nombre accesible** (ComboBox, Slider, CheckBox de plantillas). Los botones incluyen el **emoji en el nombre** ("🎮 Xbox (Standard / Series)"), que el lector de pantalla lee en voz alta. | WCAG 4.1.2 | `AutomationProperties.Name` en controles sin etiqueta; iconos como glifos fuera del texto del nombre. |
| I5 | **Contraste de componentes**: bordes de tarjeta 1,34:1 y de botón 1,72:1 (se pide 3:1); la píldora "LED LIGHTBAR" pone texto oscuro sobre el color del perfil: 3,74:1 con Xbox y **1,87:1 con PS5**. | WCAG 1.4.11, 1.4.3 | Bordes más claros o fondos con más separación; texto de la píldora calculado según la luminancia del color (claro u oscuro). |
| I6 | El **selector de stick de la curva** no está ligado a la sección del editor: la curva muestra el stick derecho mientras la pestaña Sticks abre con el izquierdo arriba. | Usabilidad | Seleccionar el stick una vez y que curva y controles lo sigan. |
| I7 | **Estados vacíos sin guía**: Macros muestra un desplegable vacío; Capas sin remapeos no explica qué hacer. | Usabilidad / copy | Estado vacío con explicación y acción principal ("Crear tu primera macro"). |
| I8 | **Textos de estado truncados sin tooltip** ("Mando físico oculto para evitar doble input (…"). | Legibilidad | Trimming con tooltip o texto más corto + detalle al pasar el ratón. |

### Menores

| # | Hallazgo | Recomendación |
|---|---|---|
| M1 | 9 tamaños de letra (9–20 px); 28 usos de 11 px y 5 de 10,5 px. | Escala de 5–6 tamaños (R2). |
| M2 | 133 colores literales en `MainWindow.xaml` y `ProfileEditorView.xaml`. | Tokens (R2/R5). |
| M3 | Idioma mezclado: "Rápidos", "LED LIGHTBAR", "Raw vs Procesado", "Polling Real", "1000Hz ENGINE", "Base Layer", subtítulo en inglés. | Revisión de copy (R3, `ux-copy`). |
| M4 | Emoji como iconos (dependen de la fuente, no escalan con el texto). | Segoe Fluent Icons. |
| M5 | La casilla "Simulador en Vivo" se superpone a los presets; "Restablecer" se recorta. | Rediseño de cabecera. |
| M6 | Radares sin leyenda (punto gris = entrada, color = salida); curva sin ejes ni etiquetas. | Leyenda y ejes. |
| M7 | El pie dice "ViGEmBus + HidHide Ready" siempre, aunque falte un driver. | Texto de estado real o quitarlo. |

## Contraste WCAG medido

| Elemento | Texto | Fondo | Ratio | Req. | Resultado |
|---|---|---|---|---|---|
| Texto principal | #F1F5F9 | #131822 | 16,23 | 4,5 | ✅ |
| Texto secundario | #94A3B8 | #131822 | 6,93 | 4,5 | ✅ |
| Texto secundario en tarjeta | #94A3B8 | #0F141E | 7,19 | 4,5 | ✅ |
| Coordenadas del radar | #94A3B8 | #111827 | 6,92 | 4,5 | ✅ |
| Títulos cian | #00E5FF | #131822 | 11,56 | 4,5 | ✅ |
| Estado HidHide (violeta) | #A78BFA | #0F141E | 6,77 | 4,5 | ✅ |
| Polling (verde) | #10B981 | #0F141E | 7,27 | 4,5 | ✅ |
| Resultado IA (lila) | #E0AAFF | #18122B | 9,79 | 4,5 | ✅ |
| Botón normal | #F1F5F9 | #1E293B | 13,35 | 4,5 | ✅ |
| Botón primario | #061018 | #00B4D8 | 7,78 | 4,5 | ✅ |
| Píldora LED (Xbox) | #04080E | #107C10 | 3,74 | 4,5 | ❌ |
| Píldora LED (PS5) | #04080E | #003791 | 1,87 | 4,5 | ❌ |
| Botón virtual activo | #061018 | #00E5FF | 12,47 | 4,5 | ✅ |
| Aviso de error | #F1F5F9 | #3A1219 | 14,97 | 4,5 | ✅ |
| Borde de tarjeta (componente) | #263044 | #131822 | 1,34 | 3,0 | ❌ |
| Borde de botón (componente) | #334155 | #131822 | 1,72 | 3,0 | ❌ |
| Punto "Raw" del radar (gráfico) | #64748B | #111827 | 3,73 | 3,0 | ✅ |
| Círculo de deadzone (gráfico) | #EF4444 | #111827 | 4,71 | 3,0 | ✅ |

## Tareas principales

| Tarea | Pasos hoy | Problema |
|---|---|---|
| Elegir mando | 2 clics (desplegable + opción) | Correcto. Nombre largo cortado. |
| Cambiar de perfil | 1 clic (lista o preset) | Correcto, pero hay dos sitios distintos para lo mismo. |
| Iniciar / detener emulación | 1 clic | Sin atajo de teclado; el estado solo se lee en el texto del botón y el pie. |
| Ajustar deadzone del stick derecho | Pestaña Sticks + scroll hasta la 2.ª tarjeta + slider | La curva y la tarjeta no comparten selección de stick (I6). |
| Crear una macro | Pestaña Macros (que salta de fila) + "+ Macro" + pasos + **"Guardar" en la otra columna** | Guardado lejos y fácil de olvidar (C1). |
| Ver si algo falla | Leer barra de avisos + franja + pie | Estados truncados (I8) y pie que no refleja la realidad (M7). |
| Cambiar un ajuste de la app | Scroll hasta el final de la columna izquierda | Escondido (I3). |

## Lo que funciona

- Los **radares en vivo y la curva** muestran el efecto de cada ajuste al instante: es el corazón de la app.
- La **telemetría** (Hz reales, latencia, jitter) da confianza y es un rasgo propio de VortexPad.
- La **marca** es reconocible (cian, logo VP, tema oscuro) y el contraste del texto es alto (16:1).
- La **barra de avisos** con acciones (descargar driver, reiniciar como administrador) es el patrón correcto.
- El **editor** ya muestra solo los controles que aplican (Bézier, umbral de hair-trigger, botón de activación del giroscopio).

## Para R2

- El usuario ya tiene un sistema de diseño en Claude Design: **"XOutput Galaxy"** (tokens + 15 componentes).
  Decidir en R2 si se parte de él o se crea uno nuevo para VortexPad.
