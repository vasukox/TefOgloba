# Arquitectura

Clean Architecture con puertos y adaptadores. Cinco proyectos de producción y cinco suites de
prueba en una única solución (`Permoda.Pay.sln`). Todas las bibliotecas compilan a `net10.0`; solo
la app compila a `net10.0-android`.

## 1. Dirección de las dependencias

```
                 ┌─────────────────────────┐
   HiPOS ──────▶ │ Permoda.Pay.Maui        │  App MAUI: Activities Android, DI,
   (Intents)     │  (net10.0-android)      │  pantallas, sesión de caja
                 └───────────┬─────────────┘
                             │ referencia
        ┌────────────────────┼────────────────────┐
        ▼                    ▼                    ▼
┌───────────────┐   ┌──────────────────┐  ┌──────────────────────┐
│ Maui.HiPos    │   │ Infrastructure   │  │ (DI compone ambos)   │
│ Orquestador   │   │ Ogloba HTTP +    │  └──────────────────────┘
│ del contrato  │   │ SQLCipher        │
└───────┬───────┘   └────────┬─────────┘
        │                    │
        └────────┬───────────┘
                 ▼
        ┌──────────────────┐
        │ Application      │  Casos de uso + puertos (interfaces)
        └────────┬─────────┘
                 ▼
        ┌──────────────────┐
        │ Domain           │  Dinero, identificadores, máquina de estados
        └──────────────────┘
```

Las dependencias apuntan siempre **hacia adentro**. Las tres reglas están verificadas por pruebas
en [`tests/Permoda.Pay.Architecture.Tests/LayerDependencyTests.cs`](../tests/Permoda.Pay.Architecture.Tests/LayerDependencyTests.cs),
que inspeccionan los ensamblados compilados:

1. `Domain` no referencia `Application`, `Infrastructure` ni `Maui`.
2. `Application` no referencia `Infrastructure` ni `Maui`.
3. `Infrastructure` no referencia `Maui`.

Si alguien invierte una dependencia, la suite falla en el build, no en revisión de código.

## 2. Responsabilidad de cada proyecto

| Proyecto | Responsabilidad | Conoce |
| --- | --- | --- |
| `Permoda.Pay.Domain` | `Money`, `CardIdentifier`, `StoreId`/`TerminalId`/`CashierId`, `TransactionNumber`, `ReferenceNumber`, `PaymentTransaction` (máquina de estados) y los errores de dominio. Nada de I/O. | Nada |
| `Permoda.Pay.Application` | Casos de uso y **puertos**. Los puertos son las interfaces que la capa externa implementa: `IGiftCardProvider`, `IPendingPaymentRepository`, `IClock`, `ITransactionAudit`, `IOglobaTrafficLog`, `ITransactionNumberGenerator`, `IDatabaseEncryptionKeyProvider`. | `Domain` |
| `Permoda.Pay.Infrastructure` | `OglobaGiftCardProvider` (HTTPS, Basic Auth por tienda, serialización, mapeo de errores) y `SqlCipherPendingPaymentRepository` (SQLite cifrado). | `Application`, `Domain` |
| `Permoda.Pay.Maui.HiPos` | Todo el contrato de ICG expresado como código puro y testeable: `HiPosPaymentOrchestrator`, `HiPosRequestMapper`, `HiPosResponseComposer`, `ModifyDocumentResultBuilder`, `ReceiptBuilder`, `DianFieldSanitizer`, `TotalizationDocumentReader`. **No referencia tipos de Android**, por eso se puede probar sin emulador. | `Application` |
| `Permoda.Pay.Maui` | Composición (DI), Activities Android que reciben los intents, adaptadores de `SecureStorage`/Keystore, ViewModels y pantallas, `PosSession`. | Todo |

### Por qué `Maui.HiPos` está separado de `Maui`

El orquestador contiene la parte del sistema donde un error cuesta dinero: qué se le responde al
POS y con qué forma. Aislarlo en un proyecto sin dependencias de Android permite cubrirlo con 73
pruebas que corren en CI sin dispositivo. La frontera con Android se cruza mediante tres puertos
definidos en el propio proyecto:

| Puerto | Lo implementa | Para qué |
| --- | --- | --- |
| `IHiPosResponseSink` | La Activity Android | Devolver el resultado al POS (`SetResult` + extras + `Finish`) |
| `IHiPosStoreConfigurationSink` | `PosSession` (capa MAUI) | Persistir la configuración de tienda que llega en `INITIALIZE` |
| `IHiPosCardCapture` | La capa Android | Abrir la pantalla del módulo, capturar el bono y esperar el resultado |

## 3. Componentes clave

### Dominio

| Componente | Qué resuelve |
| --- | --- |
| `PaymentTransaction` | Máquina de estados del pago. Los estados son `Created → Requested → ConfirmationPending → Confirmed`, con las salidas `RequestRejected`, `Step2Failed` y la rama de reverso `ReversalPending → Reversed`. Cada transición valida su precondición y devuelve `Result`, nunca lanza. |
| `Money` | Importe con moneda. **Su propiedad `MinorUnits` contiene pesos enteros, no centavos** — ver [FLUJOS_DE_OPERACION.md](FLUJOS_DE_OPERACION.md#unidades-monetarias). |
| `CardIdentifier` | Distingue tarjeta física (PAN) de bono digital (`gencode`), y expone `MaskedValue` para todo lo visible o registrable. |
| `ReferenceNumber` | Referencia que devuelve Ogloba. Máximo 40 caracteres (límite elevado por Ogloba en v2.12). |
| `Result` / `DomainError` | Resultado explícito en lugar de excepciones: un rechazo de Ogloba es un valor de retorno, no un fallo de programa. |

### Casos de uso

| Caso de uso | Qué hace |
| --- | --- |
| `ProcessSaleHandler` | El cobro completo: validación de saldo, Step 1, persistencia, Step 2, Step 3 y las rutas de reverso. Es el corazón del módulo — detalle en [FLUJOS_DE_OPERACION.md](FLUJOS_DE_OPERACION.md). |
| `ActivateVirtualGiftCardHandler` | Bono virtual con entrega por correo: `/orderCreation` → `/orderConfirm`, con consulta de estado y cancelación de la orden si algo falla. |
| `RecoverPendingPaymentsHandler` | Drena las transacciones que quedaron a medias, retomando el paso que faltaba. |
| `RecoveryStartupRunner` | Dispara la recuperación en cada arranque de la app. |
| `GiftCardRequestFactory` | Único constructor de los DTO de Ogloba (Step 1, Step 2, reverso, conciliación). Evita que cada caso de uso arme el payload a su manera. |

### Infraestructura

| Componente | Notas de diseño |
| --- | --- |
| `OglobaGiftCardProvider` | Adaptador único de los 18 endpoints. Añade `X-WSRG-API-Version` y `Accept-Language: es-co` a toda llamada, resuelve las credenciales por tienda vía `IOglobaCredentialProvider`, aplica el tope de 90 s o de 15 s según si la operación mueve dinero, y registra cada par petición/respuesta en `IOglobaTrafficLog` y en `logcat` con el PAN enmascarado. |
| `OglobaFailureMapper` | Traduce el `errorCode` de Ogloba a `PortFailureType`. Esta clasificación decide algo crítico: si el fallo es **rechazo** (no se reintenta) o **incierto** (se dispara `/reversal`). |
| `SqlCipherPendingPaymentRepository` | SQLite cifrado con SQLCipher, en modo WAL, `busy_timeout=5000` y `cipher_memory_security=ON`. La clave la provee el Keystore de Android. |

### Capa MAUI

| Componente | Notas de diseño |
| --- | --- |
| `HiPosPaymentActivity` | Recibe los intents de HiPOS, valida el paquete llamante y despacha por la **operación** (último segmento de la acción). El intent se guarda en `OnCreate` y se procesa en `OnResume`, porque en arranque en frío los servicios de MAUI aún no existen en `OnCreate`. |
| `TotalizationCanceledActivity` | Activity aparte para `TOTALIZATION_CANCELED`, porque ese intent viaja en el namespace `icg.actions.document.*` y no en el de pagos. |
| `PosSession` | Estado único de la terminal: configuración de tienda, PIN de administrador, registro de cajeros (con contraseña, activos y desactivados), cajero en turno y catálogo de productos. Es lo que consultan todas las pantallas. |
| `AppServices` | Captura el `IServiceProvider` del host de MAUI. Se usa **este** y no un contenedor paralelo: construir un segundo proveedor duplicaba singletons y dejaba las Activities sin los handlers de plataforma. |
| ViewModels y páginas | Los **ViewModels son singleton** (guardan el estado del turno y del lote); las **páginas y el `AppShell` son transient** (se recrean con la Activity). Ver [DECISIONES.md](DECISIONES.md). |

## 4. Ciclo de vida del proceso

El módulo comparte tarea con HiPOS y sobrevive entre cobros:

1. HiPOS lanza `HiPosPaymentActivity` con un intent implícito.
2. La Activity valida el llamante, resuelve la operación y delega en `HiPosPaymentOrchestrator`.
3. Si la operación necesita interacción (capturar el bono), el orquestador levanta la pantalla del
   módulo y **espera** a que el cajero acepte el resultado antes de responderle al POS.
4. El resultado se devuelve por `setResult` con los extras del contrato y la Activity termina.
5. Al volver a HiPOS, la tarea del módulo se manda al fondo (`MoveTaskToBack`) en lugar de
   cerrarse: el siguiente cobro llega por `OnNewIntent`, no por `OnCreate`.

Consecuencia de diseño: **nada que deba sobrevivir a un cobro puede vivir en una Activity.** El
estado va en `PosSession` (persistido) o en los ViewModels singleton.

## 5. Principios aplicados

* **Responsabilidad única** — un ViewModel por pantalla; `PosSession` como única fuente de verdad
  de configuración y turno; `GiftCardRequestFactory` como único constructor de payloads.
* **Inversión de dependencias** — los casos de uso solo conocen puertos; Ogloba y SQLCipher son
  detalles reemplazables, y en pruebas se reemplazan por dobles.
* **Segregación de interfaces** — puertos pequeños y separados (`IGiftCardProvider`,
  `IPendingPaymentRepository`, `ITransactionAudit`, `IOglobaTrafficLog`) en lugar de una fachada.
* **Resultados explícitos** — `Result` en dominio y `PortResult` en aplicación: los fallos de
  negocio viajan como datos y quedan cubiertos por pruebas.
