# Flujos de operación

Ciclo de vida de cada operación contra Ogloba, y las pantallas del módulo que las disparan.

## 1. Unidades monetarias

> **Ogloba recibe importes en pesos enteros, no en centavos.**

Verificado en vivo contra el sandbox el 2026-07-28: enviar el importe multiplicado por 100 se
rechaza con `errorCode 73` ("Wrong activation amount"); enviar el valor en pesos funciona.
`OglobaGiftCardProvider.AuthorizeAsync` lo dice en un comentario — **no reintroducir el ×100**.

Consecuencias en cadena:

| Frontera | Escala | Quién convierte |
| --- | --- | --- |
| HiPOS → módulo (extra `Amount`) | Importe **×100** (los dos últimos dígitos son decimales, según el contrato de ICG) | `HiPosRequestMapper.TryParseHiPosAmount` divide entre 100 |
| Módulo → Ogloba | **Pesos enteros** | — |
| Ogloba → módulo (`/balance`, saldos) | **Pesos enteros**: las comparaciones saldo-vs-importe se hacen sin escalar | — |
| Módulo → HiPOS (extras `Amount`, `FixedPaymentMeanAmount`) | Importe **×100**, de vuelta en la escala en que llegó | `HiPosPaymentOrchestrator.SendOutcome` |
| UI del módulo | **Pesos enteros**: `50000` en pantalla es $50.000 COP | — |

El peso colombiano no maneja céntimos. Si un importe llega con parte decimal distinta de cero, el
mapper la **trunca** (nunca cobra de más) y deja constancia para poder auditarlo.

> **Nota de nomenclatura**: `Money.MinorUnits` y `HiPosTransactionRequest.AmountMinorUnits` se
> llaman así por la suposición original de centavos, pero **contienen pesos de punta a punta**. Los
> nombres engañan; el comportamiento es el documentado aquí.

## 2. El ciclo obligatorio de 3 pasos de Ogloba

Ogloba no tiene operaciones atómicas. Toda operación que mueve dinero es una secuencia de tres
pasos, y dejarla a medias deja el saldo del cliente en un estado indefinido:

```
  Step 1                    Step 2                        Step 3
┌──────────────┐    ┌────────────────────────┐    ┌──────────────────┐
│ /activation  │    │ /confirmTransaction    │    │ /reconciliation  │
│ /redemption  │──▶ │   (o /cancelTransaction│──▶ │                  │
│ /reload      │    │    para descartarla)   │    │                  │
└──────┬───────┘    └────────────────────────┘    └──────────────────┘
   Requested                Confirmed                  Conciliada
       │
       └── resultado incierto (timeout / red) ──▶ /reversal ──▶ Reversed
```

La máquina de estados que lo gobierna es
[`PaymentTransaction`](../src/Permoda.Pay.Domain/Payments/PaymentTransaction.cs):

```
Created ──▶ Requested ──▶ ConfirmationPending ──▶ Confirmed
   │            │                  │
   │            │                  └──▶ Step2Failed
   │            └──▶ RequestRejected
   └──▶ ReversalPending ──▶ Reversed
```

Cada transición valida su precondición y devuelve `Result`; no lanza excepciones. Las invariantes
están cubiertas por 37 pruebas en `Permoda.Pay.Domain.Tests`.

## 3. Cobro con bono (el flujo principal)

Implementado en
[`ProcessSaleHandler`](../src/Permoda.Pay.Application/Features/Sales/ProcessSaleHandler.cs). Es el
camino que recorre una redención invocada por HiPOS o desde la pantalla del módulo:

```
1. Validación previa (solo redención)
   POST /balance ──▶ ¿la tarjeta está activa? ¿el saldo alcanza?
   · Falla acá = nada se movió. Se rechaza con mensaje en español.

2. Step 1
   POST /redemption  (o /activation si la operación es una activación)
   · Rechazo explícito de Ogloba  ──▶ FAILED. Fin.
   · Resultado incierto (timeout)  ──▶ POST /reversal con el mismo transactionNumber
                                       y se responde "reversado con seguridad".

3. Persistencia ANTES del Step 2
   La transacción autorizada se guarda en la base cifrada.
   · Si la persistencia falla, se REVERSA lo autorizado antes de seguir:
     autorizar sin poder recordarlo es peor que no autorizar.

4. Step 2
   POST /confirmTransaction
   · Rechazo   ──▶ FAILED, la transacción queda marcada Step2Failed.
   · Incierto  ──▶ queda pendiente y la recuperación la retomará (§5).

5. Step 3
   POST /reconciliation
   · Si falla, el pago YA es válido: queda pendiente de conciliar y la
     recuperación la reintenta. No se le devuelve error al cajero por esto.
```

La regla que sostiene todo: **persistir antes del Step 2**. Es lo que hace que apagar la tablet,
matar el proceso o perder la red entre el Step 1 y el Step 3 sea recuperable en el siguiente
arranque, y no dinero bloqueado.

### Clasificación de fallos

[`OglobaFailureMapper`](../src/Permoda.Pay.Infrastructure/Ogloba/Errors/OglobaFailureMapper.cs)
traduce cada respuesta de Ogloba a un tipo de fallo, y esa clasificación decide el comportamiento:

| Tipo | Significado | Qué hace el módulo |
| --- | --- | --- |
| Rechazo | Ogloba respondió y dijo "no" (saldo insuficiente, tarjeta inactiva…) | Falla limpio, sin reintentar |
| Incierto | Timeout, red caída, respuesta ilegible | Dispara `/reversal` o deja la transacción pendiente |

Los mensajes al cajero llegan **ya traducidos por Ogloba**, gracias al header `Accept-Language:
es-co` que viaja en toda llamada (verificado: "Wrong activation amount" → "Monto de activación no
válido"). El mapeo local de códigos existe como respaldo y para decidir rechazo vs. incierto.

## 4. Activación de bonos

Dos caminos, según el tipo de bono. **Ninguno de los dos se invoca desde HiPOS**: la activación es
un flujo manual con autenticación de cajero (ver [INTEGRACION_HIPOS.md](INTEGRACION_HIPOS.md#41-enrutamiento-por-tipo)).

**Antes de cualquier activación** (física o virtual), el cajero debe capturar los datos del cliente
en el formulario que está siempre visible: **tipo de documento**, **número de documento** y **nombre y
apellidos**. Estos campos son obligatorios para poder activar. La concatenación `CC-NombreApellido`
viaja a Ogloba en el campo `note` (física) o `message` (virtual). Detalle: §4.3.

### 4.1 Bono físico

```
/activation ──▶ /confirmTransaction ──▶ /reconciliation
```
Mismo `ProcessSaleHandler` que el cobro, con `GiftCardOperation.Activation`. El serial se escanea
con el lector o se digita.

El handler concatena la cédula y el nombre del cliente con
[`CustomerMobileNoBuilder`](#44-mapa-de-campos-del-cliente-en-activación) y los añade al campo
`note` del request a `/activation`. Ese campo es el único que Ogloba acepta como texto libre y
persiste en el servidor asociado a la transacción.

### 4.2 Bono virtual con entrega por correo

```
/orderCreation ──▶ /orderConfirm     (y /orderStatus, /orderCancel si algo falla)
```
Implementado en
[`ActivateVirtualGiftCardHandler`](../src/Permoda.Pay.Application/Features/Sales/ActivateVirtualGiftCardHandler.cs)
y
[`OglobaGiftCardProvider.CreateOrderAsync`](../src/Permoda.Pay.Infrastructure/Ogloba/OglobaGiftCardProvider.cs).
Usa el módulo *Order management* de Ogloba con `deliverType = 3` (eMail) — el único valor con el
que Ogloba envía el bono al correo del cliente automáticamente. El `itemCode` del producto digital
depende del ambiente (ver [AMBIENTES.md](AMBIENTES.md#por-qué-el-código-de-producto-digital-cambia-entre-ambientes)).

Estados de la orden: `042` = lista; `041` y `043` = en proceso. Si la confirmación falla, el
handler cancela la orden para no dejarla colgada.

El handler concatena la cédula y el nombre del cliente en el campo `message` del `orderItems[0]`
del request a `/orderCreation`, junto a cualquier `message` libre que la activación tuviera
previamente. Si ambos vienen vacíos no se envía nada.

Rango de importe aceptado por el catálogo del producto (sandbox `113816`, verificado 2026-08-24):
**$30.000 – $500.000**. La UI limita el importe a ese rango; si el catálogo aún no se cargó, cae a
las mismas constantes. Desalinear ese rango produce rechazos con `errorCode 73` en montos que la
pantalla acepta.

### 4.3 Captura de datos del cliente (física y virtual)

Todo bono — físico o virtual — se emite con los datos del destinatario del bono. Esos datos
viajan a Ogloba concatenados como un solo string en el campo de texto libre del request:

```
1011086580-AndresFelipeDiazBernal
                    ↑ cédula + nombre del cliente, una sola unidad
```

**Por qué se concatena en lugar de enviarse por separado**:

1. El endpoint `/activation` (física) **no tiene un campo dedicado** para datos del destinatario.
   Su body solo acepta `merchantId`, `terminalId`, `cashierId`, `transactionNumber`, `amount`,
   `gencode`, `cardNumber`, `note` y `track2Data`. El único texto libre es `note`.
2. El endpoint `/orderCreation` (virtual) sí tiene `orderItems[*].receiverName`,
   `receiverMobileNo` y `receiverAddress` (ver §3.9 del [OGLOBA_API_REFERENCE.md](OGLOBA_API_REFERENCE.md#39-post-ordercreation-nuevo)).
   Sin embargo, Ogloba tiene un comportamiento verificado: cuando se manda `receiverName`,
   **lo guarda en el campo `mobileNo` del bono** (comportamiento observado en pedido
   `MA2608000014`, ver [`OglobaRequests.cs` línea 103](../src/Permoda.Pay.Infrastructure/Ogloba/Contracts/OglobaRequests.cs)).
   Eso ensucia el registro del bono sin un destino claro.

Concatenar en el campo `note` / `message` evita ambos escollos con el mismo formato para los dos
endpoints.

**Implementación**: [`CustomerMobileNoBuilder`](../src/Permoda.Pay.Application/CustomerMobileNoBuilder.cs).
Es un helper estático que:

* Recibe `CustomerDocumentNumber` (string de 6-15 dígitos) y `CustomerName` (texto libre).
* Quita tildes (`Andrés` → `Andres`) usando `Normalize(FormD)` + filtrado de diacríticos.
* Pone mayúscula solo en la primera letra de cada palabra (PascalCase sin tildes).
* Elimina espacios entre palabras (`Andres Felipe` → `AndresFelipe`).
* Une con guión: `"1011086580-AndresFelipeDiazBernal"`.
* Trunca a 100 caracteres conservando el documento y el guión si el nombre es demasiado largo.
* Si solo hay un dato, devuelve ese dato. Si no hay ninguno, devuelve cadena vacía.

**UI**: el bloque "DATOS DEL CLIENTE" en `ActivateTabPage.xaml` siempre está visible (no depende de
que HiPOS haya invocado el módulo). El cajero ve además un preview en línea que muestra el
string concatenado (`Se envía como: 1011086580-AndresFelipeDiazBernal`) para que pueda
verificar antes de activar.

**Validación** en `ActivateTabViewModel.ProcessAllAsync`:

* `CustomerDocumentNumber` debe coincidir con `^\d{6,15}$` (sin puntos ni comas).
* `CustomerName` es obligatorio y de cualquier tamaño (el helper lo normaliza).
* Sin ambos datos capturados, la activación **no procede**: se muestra el motivo en el banner
  inline y el lote queda sin enviar.

**Tests**: 9 casos en `tests/Permoda.Pay.Application.Tests/Common/CustomerMobileNoBuilderTests.cs`
cubren tildes, diacríticos variados, separadores, espacios múltiples, mayúsculas mixtas,
truncamiento y combinaciones parciales (solo documento, solo nombre, ninguno).

### 4.4 Mapa de campos del cliente en activación

```
        Cajero captura                Aplicación concatena            Ogloba guarda
        ──────────────────            ──────────────────────           ─────────────────
        Cédula: 1011086580            CustomerMobileNoBuilder          En la transacción:
        Nombre: Andrés Felipe        "1011086580-AndresFelipeDiaz..."  /activation → note
                  Díaz Bernal                                             /orderCreation → message
```

Por qué: el endpoint `/activation` no tiene un campo dedicado para datos del cliente, y en
`/orderCreation` Ogloba mezcla `receiverName` con `mobileNo` (comportamiento documentado y
verificado). Concatenar en `note` / `message` evita ambos escollos con el mismo formato.

## 5. Recuperación de pagos pendientes

[`RecoverPendingPaymentsHandler`](../src/Permoda.Pay.Application/Features/Recovery/RecoverPendingPaymentsHandler.cs),
disparado en cada arranque por `RecoveryStartupRunner`:

1. Lee de la base cifrada las transacciones que no llegaron a `Confirmed` + conciliada.
2. Por cada una, retoma **el paso que faltaba**: confirmar, reversar o conciliar.
3. Registra el resultado en la auditoría del turno.

El cajero no necesita llamar a soporte: al reabrir el módulo ve qué quedó pendiente y puede
cerrarlo o cancelarlo.

## 6. Consulta de saldo

`POST /balance`. No inicia ninguna operación y no consume saldo. Se usa en tres sitios: como
validación previa de cada redención, como pantalla propia del módulo, y como respuesta al intent
`QUERY_TRANSACTION` (deshabilitado hoy, ver
[INTEGRACION_HIPOS.md](INTEGRACION_HIPOS.md#5-query_transaction)).

Devuelve saldo, estado de la tarjeta y fecha de vencimiento. Admite `pinCode` opcional — **tres
intentos incorrectos pueden bloquear la tarjeta**.

## 7. Cierre de caja

El intent `BATCH_CLOSE` se responde `ACCEPTED` sin trabajo adicional: cada venta ya llamó a
`/reconciliation` inmediatamente después de `/confirmTransaction`. Ogloba espera la conciliación
diaria de todas las transacciones iniciadas con éxito, y eso ya está cubierto por transacción.

## 8. Operaciones fuera de alcance

| Operación | Estado real | Por qué |
| --- | --- | --- |
| Anulación (`/voidTransaction`) | El adaptador existe (`VoidAsync`); **ningún caso de uso lo invoca** | Decisión de KOAJ (2026-09-02): un cobro con bono no se deshace desde el POS |
| Nota de crédito (`REFUND`) | Rechazado explícitamente en el orquestador | Misma decisión. Responder `ACCEPTED` sin mover saldo era lo peor de los dos mundos |
| Recarga (`/reload`) | El adaptador existe (`ReloadAsync`, concilia como tipo `L`); **ningún caso de uso ni pantalla lo invoca** | No hay flujo de recarga definido para el cajero |
| Devolución de orden (`/orderReturn`) | El adaptador existe; sin caso de uso | Sin flujo definido |
| Pago parcial devuelto | No soportado | Deshacer suelta la línea completa |

Cerrar **las dos** puertas de deshacer (`REFUND` y `VOID_TRANSACTION`) y no solo una es lo que hace
real la prohibición: ver [DECISIONES.md](DECISIONES.md#d-04).

## 9. Pantallas del módulo

La navegación es un `Shell` con menú lateral. Las rutas se declaran como constantes en
[`AppRoutes`](../src/Permoda.Pay.Maui/Common/AppRoutes.cs) — nunca como literales en XAML.

### Visibles en el menú

| Pantalla | Ruta | Qué hace |
| --- | --- | --- |
| Inicio | `//home` | Tienda y cajero activos, estado de conexión con Ogloba, saldo del comercio |
| Consultar saldo | `//balance` | Serial → `POST /balance`. Saldo, estado y vencimiento |
| Bitácora | `//audit` | Operaciones del turno, con timestamp, tipo, estado, referencia y código de error |
| Usuarios | `//users` | Gestión de cajeros. Pide el PIN de administrador antes de mostrar nada |
| Configuración | `//setup` | Tienda, caja, contraseña de Ogloba, PIN de administrador |

### Ocultas del menú, alcanzables por flujo

| Pantalla | Ruta | Por qué está oculta |
| --- | --- | --- |
| Activar bono | `//activate` | Mueve dinero de una factura. Se entra por el selector de tipo de bono, no por el menú |
| Redimir saldo | `//redeem` | La levanta HiPOS durante un cobro, no el cajero por su cuenta |
| Registrar cajeros | `//cashiersetup` | Paso 3 del alta inicial, no un módulo |
| Tipo de bono | `activatemode` | Paso previo a activar (físico o virtual). Ruta global: se empuja sobre Inicio |
| Ingreso de cajero | `cashierlogin` | Ruta global que se empuja sobre la pestaña activa |

### Cómo se decide la pantalla de entrada

Al abrir la app o al recibir un cobro, el destino se decide **antes** de navegar:

1. Terminal sin configurar (falta tienda, PIN de administrador o cajeros dados de alta)
   → **Configuración**. Mandarla al selector de cajero con la lista vacía deja al cajero sin nada
   que tocar.
2. Cobro ordenado por HiPOS → **derecho a operar**, sin pedir cajero: el cajero ya se identificó en
   el POS para poder facturar y el POS está esperando respuesta.
3. Apertura manual → **ingreso de cajero** con usuario y contraseña. La operación se firma con ese
   nombre en la bitácora y en lo que se envía a Ogloba, y en una caja compartida el turno anterior
   puede ser de otra persona.

### Reglas de interacción

* **Un lote por pantalla.** Activar y Redimir trabajan con filas: el cajero agrega bonos, y un
  único CTA procesa el lote. Cada fila avanza Pendiente → Procesando → Aprobado / En proceso /
  Rechazado, y puede seguir editando otras filas mientras unas ya se procesaron.
* **Dos columnas en los formularios operativos.** El teclado en pantalla ocupa buena parte del alto
  útil: a la izquierda el serial (o el correo), que recibe el foco al abrir y captura el disparo del
  lector; a la derecha el segundo campo. El CTA del lote va a lo ancho, debajo.
* **El resultado se comunica con un toast nativo de Android** (`IToastService`), no con un banner de
  pantalla completa. `StatusBannerView` sigue vigente para avisos que deben quedarse a la vista.
* **Redimir no consulta saldo a demanda**: para eso está *Consultar saldo*. La validación de saldo
  previa a cada redención sigue corriendo por dentro.
* **No hay catálogo de tarjetas precargadas**: el serial siempre se escanea o se digita.

Detalle visual y de componentes: [DESIGN.md](../DESIGN.md).

## 10. Sesión de caja y cajeros

[`PosSession`](../src/Permoda.Pay.Maui/Services/PosSession.cs) es la única fuente de verdad:

| Concepto | Dónde vive | Notas |
| --- | --- | --- |
| Configuración de tienda | `SecureStorage` (`StoreConfiguration`) | Tienda, caja, URL, versión de API y nombre del comercio (traído de `/getBuInfo`) |
| Contraseña de Ogloba | `SecureStorage`, por tienda | Nunca en código ni en logs |
| PIN de administrador | Hash en `SecureStorage` | No se puede recuperar |
| Registro de cajeros | `SecureStorage`, por tienda | Con contraseña propia. Un cajero puede **desactivarse** sin borrarlo: conserva su contraseña y su historial |
| Cajero en turno | `Preferences` | Se cierra al terminar una activación o al volver al selector |
| Último cajero | `Preferences` | Respaldo para los cobros de HiPOS, que no piden cajero |

`OperatingCashierId` = cajero en turno, y si no hay, el último que operó en esa caja. El respaldo
existe porque en un cobro ordenado por HiPOS el módulo no pide cajero y el turno puede estar
cerrado; sin él, Ogloba rechazaba el cobro con `cashier_id.required` al escanear el bono.
