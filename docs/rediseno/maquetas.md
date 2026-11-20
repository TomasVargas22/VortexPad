# R3 — Maquetas y arquitectura de información

Lienzo en Claude Design: [Maquetas VortexPad](https://claude.ai/artifact/AuYzSAA4XY6nj4dMfyorYX) (privado del usuario),
construido sobre el sistema [VortexPad](https://claude.ai/artifact/5TM6fq6oQC2r2fmoZk4iQ7) de R2.

## Alternativas

| | A · Riel de navegación con secciones | B · Panel en vivo + editor en una pantalla |
|---|---|---|
| Estructura | Riel izquierdo (232 px, colapsable a 64 px) → una sección a pantalla completa | Barra superior + panel en vivo fijo (400 px) + editor con lista vertical de secciones |
| Tableros | A1 Panel en vivo · A2 Editor sticks · A3 Macros · A4 Perfiles · A5 Ajustes · A6 Errores y vacíos · A7 1120×720 | B1 Principal · B2 1120×720 |
| Ventaja | Cada sección tiene todo el ancho; escala a más secciones; ajustes y perfiles son de primer nivel | Ves el efecto en vivo mientras editas, sin cambiar de pantalla (lo más cercano a la app actual) |
| Coste | Para ver el resultado al editar hay que confiar en la vista previa pequeña (A2) o alternar con Ctrl+1 | A 1120×720 el editor queda en ~720 px y hay que desplazar (B2); perfiles y ajustes van a menús |
| Atajos | Ctrl+1…5 mapean a secciones del riel | Ctrl+1…8 a pestañas del editor; perfil y ajustes sin atajo propio |

**Recomendación: A**, con la vista previa en vivo del stick dentro de cada sección del editor (como en A2).
Resuelve I1 (pestañas que se reordenan), I3 (estado y ajustes escondidos) y C2 (1120×720) sin desplazamiento.

## Crítica de diseño (design-critique)

| Hallazgo | Severidad | Recomendación |
|---|---|---|
| El riel de A no tiene Giroscopio ni Vibración y luz; «Capas y botones» y «DualSense» apuntan a maquetas aún no hechas | 🔴 | Grupo Editor = Sticks y gatillos · Botones y capas · Giroscopio · Vibración y luz · Macros; «DualSense» solo aparece si el mando lo es |
| El glifo E7FC (mando) se repite en Sticks, Gatillos y DualSense | 🟡 | Glifos distintos en R4 (p. ej. E879 sticks, E7B3 giro, E877 vibración); el texto siempre acompaña salvo en el riel colapsado |
| Barra de estado de A1: 4 chips + perfil + botón ocupan ~1100 px y se parten en dos líneas por debajo de ~1280 px | 🟡 | Desde 1280 px agrupar en un chip «Todo listo» desplegable (ya en A7); el chip solo se expande si algo falla |
| Ctrl+1…5 no alcanza para 8 entradas del riel | 🟡 | Ctrl+1 Panel · Ctrl+2 Perfiles · Ctrl+3 Editor (última sección abierta) · Ctrl+4 Macros · Ctrl+5 Ajustes |
| Jerarquía: el botón primario (cian) y el chip «Emulando» compiten en la barra | 🟢 | Correcto: es la acción principal de la app. Mantener un único botón primario por pantalla |
| Funciona: la leyenda de colores y el «lectura → salida» en mono resuelven M6; la curva enseña el punto en vivo | ✅ | — |

## Accesibilidad (accessibility-review, WCAG 2.1 AA)

| # | Hallazgo | Criterio | Severidad | Corrección para R4/R5 |
|---|---|---|---|---|
| 1 | Los puntos de la curva (14 px) no se pueden mover con teclado | 2.1.1, 2.5.5 | 🔴 | Foco con Tab, flechas mueven 1 % (Mayús 10 %) y campos numéricos equivalentes junto a la curva |
| 2 | La matriz de botones está `aria-hidden`: un lector no sabe qué botones están pulsados | 4.1.2 | 🟡 | Región `aria-live="off"` con resumen bajo demanda («Pulsados: A, LB»); no anunciar a 1000 Hz |
| 3 | Radares con etiqueta estática | 1.1.1 | 🟢 | `AutomationProperties.HelpText` con la lectura numérica, actualizado a 10 Hz como mucho |
| 4 | Riel colapsado y botones solo con icono | 4.1.2, 1.4.13 | ✅ | Ya llevan `aria-label` + tooltip con atajo; mantener en WPF con `AutomationProperties.Name` |
| 5 | Botón «Iniciar emulación» deshabilitado en A6 | 3.3.1 | ✅ | Va enlazado al motivo (`aria-describedby`); en WPF `ToolTipService.ShowOnDisabled` |
| 6 | Estados con color | 1.4.1 | ✅ | Todos los chips llevan icono + palabra (ok y danger solo difieren 1,38:1 en luminancia) |
| 7 | Contraste | 1.4.3, 1.4.11 | ✅ | Texto ≥ 4,5:1 y bordes de control ≥ 3:1 en todas las superficies usadas (tabla de R2); `text-faint` no aparece sobre `surface-3` |
| 8 | Buscador de perfiles con etiqueta solo visual | 3.3.2 | 🟢 | Etiqueta oculta visualmente pero presente para UI Automation |
| 9 | Objetivos | 2.5.5 | ✅ | Riel 44×44, controles ≥ 32 px (`target-min`) |

Pendiente de probar en R6 con Narrador y solo teclado; esta revisión es analítica sobre el marcado de las maquetas.

## Textos (ux-copy)

Corregido en las maquetas:
- «Hair-trigger en R2» → **«Disparo rápido en RT»** (sin inglés y con la nomenclatura del mando virtual activo).
- Aviso de ViGEmBus: «vuelve a abrir VortexPad» → **«pulsa «Comprobar de nuevo»; no hace falta cerrar VortexPad»** (coincide con el botón).

Glosario (un término por concepto):

| Usar | No usar |
|---|---|
| mando físico / mando virtual | control, gamepad, dispositivo |
| Iniciar / Detener emulación | Start, Activar, Parar |
| Emulando · Emulación detenida | Running, Ready, Listo (como estado global) |
| Guardado | Saved, Cambios guardados correctamente |
| zona muerta, curva de respuesta, sensibilidad | deadzone, curve |
| Disparo rápido | hair-trigger |
| perfil / preset incluido | configuración, plantilla |

Patrones: errores = qué pasó + por qué importa + cómo se arregla, con el botón que lo arregla; vacíos = qué es + cómo empezar
(A6: «Conecta un mando», «Este perfil no tiene macros»); botones con verbo en infinitivo; números con coma decimal y espacio
antes de % y unidades («8,0 %», «13,8 µs»).
