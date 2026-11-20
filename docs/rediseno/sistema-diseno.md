# R2 — Sistema de diseño de VortexPad

Sistema en Claude Design: [VortexPad](https://claude.ai/artifact/5TM6fq6oQC2r2fmoZk4iQ7) (privado del usuario).
Fuente de verdad de tokens, componentes y reglas de uso para R3 (maquetas), R4 (especificación) y R5 (WPF).

## Decisiones

- **Sistema nuevo para VortexPad**, no una copia de «XOutput Galaxy»: se hereda de Galaxy la estructura
  (escalas de espaciado 4/8, tipografía Segoe UI Variable + Cascadia Mono, radios anidados, duraciones,
  voz en español, reglas de estados y foco) y se mantiene la marca propia de VortexPad (cian `#00E5FF`,
  logo VP, fondos actuales `#0B0E14` / `#131822`).
- **Solo tema oscuro**, como hoy. Un tema claro queda fuera de alcance.
- **Iconos**: Segoe Fluent Icons (glifos del sistema) en lugar de emoji.
- **Contraste verificado (WCAG AA)**: todo el texto ≥ 4,5:1 en los fondos donde se usa; bordes de control
  `line-control` ≥ 3:1 sobre `surface` y `surface-2`. Límites documentados: `text-faint` y `line-control` no se
  usan sobre `surface-3`.

## Propuestas que R3 debe confirmar con maquetas

Están escritas en el sistema como dirección, pero se validan en R3:

- Navegación lateral (riel) con secciones en lugar de tres columnas y pestañas (auditoría I1, I3).
- Barra de estado con chips (emulación, mando, ViGEmBus, HidHide, guardado) en lugar de cabecera + franja + pie (I3, I8, M7).
- **Autoguardado** del perfil y estado «Guardado» en la barra (C1).
- Selector de stick único que mueve curva y controles a la vez (I6).
- Atajos: F5 iniciar/detener, Ctrl+1…5 secciones, Ctrl+Tab siguiente perfil (C3).

## Inventario

| Grupo | Componentes |
|---|---|
| Acciones | Button, IconButton |
| Entradas | Select, Slider, Checkbox, Toggle, SegmentedControl, TextField |
| Navegación | SectionNav, ProfileList |
| Estado | Notice, StatusChip, Tooltip |
| Contenedores | Card |
| En vivo | StickRadar, ResponseCurve, TriggerMeter, ButtonMatrix, Telemetry |

Tokens: 25 colores, 10 estilos de texto en 3 grupos, 7 espaciados, 4 radios, 2 sombras, 3 duraciones,
2 curvas y 9 tamaños (controles, riel, radar, ventana mínima).
