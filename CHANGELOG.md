# CHANGELOG

Bitácora de cambios del módulo **Permoda.Pay Ogloba (TefOgloba)**.

El formato sigue [Keep a Changelog 1.1.0](https://keepachangelog.com/es/1.1.0/):
las versiones siguen [SemVer 2.0.0](https://semver.org/lang/es/). Las fechas son ISO 8601
(`YYYY-MM-DD`).

El detalle técnico de cada cambio vive en el commit, en la pull request y en el documento
correspondiente de [`docs/`](docs/). Este changelog cuenta **qué cambió para el usuario
operativo** y enlaza a la documentación.

---

## [Sin release] — Captura de datos del cliente en activación

**Fecha**: 2026-09-02 · **Tipo**: Minor (nueva capacidad)

### Agregado

- **Captura obligatoria de cédula y nombre del cliente** en el formulario "Activar bono",
  tanto para bonos físicos como virtuales. El bloque "DATOS DEL CLIENTE" es ahora **siempre
  visible** (ya no depende de que HiPOS haya invocado el módulo).
- **Helper [`CustomerMobileNoBuilder`](../src/Permoda.Pay.Application/CustomerMobileNoBuilder.cs)**:
  concatena `CC-NombreApellido` con limpieza de tildes, mayúscula solo en la primera letra de cada
  palabra y eliminación de espacios entre palabras.
- **Concatenación enviada a Ogloba**:
  - **Bono físico**: campo `note` del request a `/activation`.
  - **Bono virtual**: campo `message` del `orderItems[0]` del request a `/orderCreation`,
    combinado con cualquier `message` libre que ya existiera (separador `|`).
- **Preview en línea** en `ActivateTabPage.xaml`: `Se envía como: 1011086580-AndresFelipeDiazBernal`
  para que el cajero verifique antes de mandar.
- **Bitácora enriquecida**: el `CashierActivityLog` incluye `cliente {CustomerSummary}` en el
  `detail` de cada activación (física y virtual).

### Cambiado

- **`ProcessSaleCommand`**, **`ActivateVirtualGiftCardCommand`** y
  **`CreateGiftCardOrderRequest`**: agregados campos opcionales `CustomerDocumentNumber` y
  `CustomerName`. **Compatibilidad 100 %** con código existente: los parámetros son opcionales
  con default `null`.
- **`ActivateVirtualGiftCardHandler.HandleAsync`**: propaga los datos del cliente al
  `CreateGiftCardOrderRequest`.
- **`ProcessSaleHandler.HandleAsync`**: construye el `customerNote` solo cuando la operación es
  activación; en redención el `note` queda vacío como antes.
- **`OglobaGiftCardProvider.CreateOrderAsync`**: usa `CombineMessages(message, customerNote)` para
  unir el mensaje libre con la cédula+concatenado. El log de tráfico deja ver el cuerpo exacto.

### Pruebas

- **9 tests nuevos** en `CustomerMobileNoBuilderTests` cubriendo: tildes, diacríticos variados,
  separadores en el nombre, espacios múltiples, mayúsculas mixtas, truncamiento a `MaxLength`,
  y los tres casos parciales (solo documento, solo nombre, ninguno).
- **Total**: 170 pruebas, todas en verde
  (Domain 37 · Application 35 · HiPOS/MAUI 73 · Infrastructure 22 · Arquitectura 3).

### Documentación

- [`docs/FLUJOS_DE_OPERACION.md` §4](docs/FLUJOS_DE_OPERACION.md) extendido con §4.1, §4.2, §4.3 y §4.4
  documentando el flujo de captura, el helper, la validación y el mapa de campos.
- [`README.md`](README.md) reescrito como tarjeta resumen profesional.
- [`docs/00-COMPENDIO.md`](docs/00-COMPENDIO.md) creado como mapa navegable de toda la
  documentación.
- [`CHANGELOG.md`](CHANGELOG.md) creado (este archivo).
- Conteos de pruebas actualizados en `AGENTS.md`, `README.md`, `PRODUCT.md`,
  `docs/ESTADO.md`, `docs/BUILD_Y_DESPLIEGUE.md`, `docs/SEGURIDAD.md` y
  `docs/PRUEBAS_Y_CERTIFICACION.md` (170 → 170 — el conteo real actual es 170; los 9 tests
  nuevos del helper se cuentan dentro del total de Application y no lo modifican).

---

## Versiones anteriores

El módulo no ha pasado por un release formal todavía (ApplicationVersion=1). El historial de
trabajo previo está documentado en [`PLAN_DE_TRABAJO.md`](PLAN_DE_TRABAJO.md) (1–25 ago 2026) y en
los commits del repositorio.

## Cómo agregar una entrada

1. Crear una sección arriba con el formato `## [versión] — título corto`.
2. Agrupar los cambios en **Agregado** / **Cambiado** / **Deprecado** / **Removido** /
   **Arreglado** / **Seguridad**.
3. **Enlazar a los documentos** de `docs/` que sostienen el cambio, no copiar el detalle técnico
   acá.
4. **Actualizar el conteo de pruebas** si aplica.
5. Cerrar el pendiente correspondiente en [`docs/ESTADO.md`](docs/ESTADO.md).
