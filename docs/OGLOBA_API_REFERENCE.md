# Referencia de la API REST de Ogloba GiftCard

> **Fuente**: colección Postman oficial de Ogloba (`documenter.getpostman.com/view/32475087/2s9YymGPrn`),
> Collection ID `86bd28f3-b9bb-43e9-a444-7aaf22949d07`, publicada 2024-07-24.
>
> **Propósito**: referencia completa de los 18 endpoints, sus schemas, los códigos de error y el
> changelog. Es la fuente de verdad del **contrato de Ogloba**; cómo lo usa el módulo está en
> [FLUJOS_DE_OPERACION.md](FLUJOS_DE_OPERACION.md) y la configuración por ambiente en
> [AMBIENTES.md](AMBIENTES.md).
>
> **Audiencia**: el equipo que mantiene la integración.
>
> Este documento es citado desde el código con la forma `docs/OGLOBA_API_REFERENCE.md §N`. Si se
> renumeran las secciones, hay que actualizar esas referencias.

---

## Tabla de contenido

0. [Estado de implementación por endpoint](#0-estado-de-implementación-por-endpoint)
1. [Configuración base (URL, headers, auth)](#1-configuración-base-url-headers-auth)
2. [Inventario completo de endpoints (18)](#2-inventario-completo-de-endpoints-18)
3. [Referencia endpoint por endpoint](#3-referencia-endpoint-por-endpoint)
   - 3.1 [`POST /activation`](#31-post-activation-step-1)
   - 3.2 [`POST /redemption`](#32-post-redemption-step-1)
   - 3.3 [`POST /reload`](#33-post-reload-step-1)
   - 3.4 [`POST /confirmTransaction`](#34-post-confirmtransaction-step-2)
   - 3.5 [`POST /cancelTransaction`](#35-post-canceltransaction-step-2)
   - 3.6 [`POST /reconciliation`](#36-post-reconciliation-step-3)
   - 3.7 [`POST /reversal`](#37-post-reversal)
   - 3.8 [`POST /voidTransaction`](#38-post-voidtransaction)
   - 3.9 [`POST /orderCreation` (NUEVO)](#39-post-ordercreation-nuevo)
   - 3.10 [`POST /orderConfirm` (NUEVO)](#310-post-orderconfirm-nuevo)
   - 3.11 [`POST /orderCancel` (NUEVO)](#311-post-ordercancel-nuevo)
   - 3.12 [`POST /orderStatus` (NUEVO)](#312-post-orderstatus-nuevo)
   - 3.13 [`POST /orderReturn` (NUEVO)](#313-post-orderreturn-nuevo)
   - 3.14 [`POST /balance`](#314-post-balance)
   - 3.15 [`POST /queryTransactionsHistory`](#315-post-querytransactionshistory)
   - 3.16 [`POST /getProducts`](#316-post-getproducts)
   - 3.17 [`GET /getBuInfo`](#317-get-getbuinfo)
   - 3.18 [`GET /test`](#318-get-test)
4. [Webhooks (Ogloba → nosotros)](#4-webhooks-ogloba--nosotros)
5. [Códigos de error completos](#5-códigos-de-error-completos)
6. [Changelog v2.18 → v2.20](#6-changelog-v218--v220)
7. [Inconsistencias internas de la documentación de Ogloba](#7-inconsistencias-internas-de-la-documentación-de-ogloba)
8. [Recomendaciones operativas (migration notes)](#8-recomendaciones-operativas-migration-notes)

---

## 0. Estado de implementación por endpoint

Qué endpoints consume el módulo hoy, y por dónde. **"Adaptador sin uso"** significa que el método
existe en `OglobaGiftCardProvider` y está cubierto por pruebas de contrato, pero ningún caso de uso
lo invoca en producción.

| Endpoint | Estado en el módulo | Quién lo invoca |
|---|---|---|
| `POST /activation` | En uso | `ProcessSaleHandler` (activación de bono físico) |
| `POST /redemption` | En uso | `ProcessSaleHandler` (cobro con bono) |
| `POST /confirmTransaction` | En uso | `ProcessSaleHandler` (Step 2) |
| `POST /reconciliation` | En uso | `ProcessSaleHandler` (Step 3) |
| `POST /reversal` | En uso | `ProcessSaleHandler`, ante un resultado incierto |
| `POST /balance` | En uso | Validación previa a redimir, pantalla *Consultar saldo*, `QUERY_TRANSACTION` |
| `POST /orderCreation` | En uso | `ActivateVirtualGiftCardHandler` |
| `POST /orderConfirm` | En uso | `ActivateVirtualGiftCardHandler` |
| `POST /orderStatus` | En uso | `ActivateVirtualGiftCardHandler` |
| `POST /orderCancel` | En uso | `ActivateVirtualGiftCardHandler`, para no dejar la orden colgada |
| `POST /getProducts` | En uso | Catálogo de la tienda; alimenta los rangos de importe de la UI |
| `GET /getBuInfo` | En uso | Nombre y saldo del comercio en el encabezado |
| `GET /test` | En uso | Indicador de conectividad |
| `POST /queryTransactionsHistory` | En uso | Pantalla *Bitácora* |
| `POST /voidTransaction` | Adaptador sin uso | Ninguno: los bonos no se anulan desde el POS ([DECISIONES.md](DECISIONES.md#d-04)) |
| `POST /reload` | Adaptador sin uso | Ninguno: no hay flujo de recarga definido |
| `POST /orderReturn` | Adaptador sin uso | Ninguno |
| `POST /cancelTransaction` | No implementado | El Step 2 siempre confirma; lo incierto se reversa |

Puntos del contrato ya resueltos y verificados, para que no se vuelvan a auditar:

* **URL base**: `co-ts` en desarrollo y UAT, `co-prod` en producción, siempre con el segmento
  `/gc-restful-gateway`. `srl-ts` es el tenant demo público del Postman y no aplica a KOAJ.
* **Versión de la API**: `2.18` en los tres ambientes ([DECISIONES.md](DECISIONES.md#d-15)). La
  colección pública muestra `2.20`, que corresponde a otro tenant.
* **`/getBuInfo` y `/test` van por GET**, no por POST.
* **El path de anulación es `/voidTransaction`**, no `/void`.
* **`/reversal`** viaja con `originalMerchantId`, `originalTerminalId`, `originalCashierId` y
  `originalTransNumber`.
* **`/balance`** viaja con `merchantId`, `terminalId`, `cashierId`, `transactionNumber` y `pinCode`
  opcional.
* **`referenceNumber` admite 40 caracteres** (límite elevado por Ogloba en v2.12), y así está en
  `ReferenceNumber`.
* **`Accept-Language: es-co`** se envía en todas las llamadas: Ogloba devuelve el `errorMessage` ya
  traducido ([DECISIONES.md](DECISIONES.md#d-17)).
* **Deserialización defensiva** de `/redemption`: `toBeChargedAmt`, `requestedCurrency`, `gencode` y
  `redeemShareDet` se mapean tolerando ausencias.
* **Los importes viajan en pesos enteros**, no en centavos
  ([DECISIONES.md](DECISIONES.md#d-02)).

---

## 1. Configuración base (URL, headers, auth)

### URL base
```
https://{host}/gc-restful-gateway/giftCardService
```

Para **testing**, el Postman oficial usa `https://srl-ts.ogloba.com/...` (tenant demo público). KOAJ usa `https://co-ts.ogloba.com/gc-restful-gateway/giftCardService` en testing/UAT y `https://co-prod.ogloba.com/gc-restful-gateway/giftCardService` en producción, según `AppEnvironment.cs`.

### Headers comunes (todos los endpoints)

| Header | Valor | Obligatorio | Notas |
|---|---|---|---|
| `Content-Type` | `application/json` | Sí | Siempre |
| `X-WSRG-API-Version` | **`2.18`** en este módulo (la colección pública muestra `2.20`, de otro tenant) | Sí | Versión de la API. Ver [DECISIONES.md](DECISIONES.md#d-15) |
| `Authorization` | `Basic <base64(login:password)>` | Sí | Basic Auth por tienda |
| `Accept-Language` | `es-co` en este módulo (`en` por defecto en la API) | No | Localiza los mensajes de error. Ver [DECISIONES.md](DECISIONES.md#d-17) |

### Autenticación (Basic Auth)

- **Username (`login`)**: Store ID de la tienda, con formato `K#####`. En PSP centralizados puede ser distinto al `merchantId`.
- **Password (`passphrase`)**: la asigna Ogloba por tienda o por tenant.

Las credenciales **no se documentan aquí**. En el módulo viven cifradas en `SecureStorage` y llegan
por el intent `INITIALIZE` o por la pantalla de configuración; los datos de autocompletado del
sandbox están en `AppEnvironment`. Ver
[AMBIENTES.md §3](AMBIENTES.md#3-credenciales-por-tienda) y [SEGURIDAD.md](SEGURIDAD.md).

Las credenciales demo que trae la colección Postman pública apuntan al tenant `srl-ts` y **no
aplican a KOAJ**.

---

## 2. Inventario completo de endpoints (18)

URL base: `{base}/giftCardService` (ver §1).

| # | Carpeta | Verbo | Path | Estado en el módulo (ver §0) |
|---|---|---|---|---|
| 1 | Transaction - Step 1 | `POST` | `/activation` | En uso |
| 2 | Transaction - Step 1 | `POST` | `/redemption` | En uso |
| 3 | Transaction - Step 1 | `POST` | `/reload` | Adaptador sin uso |
| 4 | Transaction - Step 2 | `POST` | `/confirmTransaction` | En uso |
| 5 | Transaction - Step 2 | `POST` | `/cancelTransaction` | No implementado |
| 6 | Transaction - Step 3 | `POST` | `/reconciliation` | En uso |
| 7 | Transaction - Reversal | `POST` | `/reversal` | En uso |
| 8 | Transaction - Void | `POST` | `/voidTransaction` | Adaptador sin uso |
| 9 | Order management | `POST` | `/orderCreation` | En uso |
| 10 | Order management | `POST` | `/orderConfirm` | En uso |
| 11 | Order management | `POST` | `/orderCancel` | En uso |
| 12 | Order management | `POST` | `/orderStatus` | En uso |
| 13 | Order management | `POST` | `/orderReturn` | Adaptador sin uso |
| 14 | Other | `POST` | `/balance` | En uso |
| 15 | Other | `POST` | `/queryTransactionsHistory` | En uso |
| 16 | Other | `POST` | `/getProducts` | En uso |
| 17 | Other | **`GET`** | `/getBuInfo` | En uso — verbo **GET**, no POST |
| 18 | Other | **`GET`** | `/test` | En uso — verbo **GET**, no POST |

**Resumen**: de los 18 endpoints, 14 están en uso, 3 tienen adaptador sin caso de uso que los
invoque y 1 no está implementado. Detalle y motivos en §0.

---

## 3. Referencia endpoint por endpoint

### 3.1 `POST /activation` (Step 1)

**Propósito**: activar una tarjeta regalo nueva (ecard o física). Deja la operación en estado `Requested`.

**Request body**

| Campo | Tipo | Máx | Obligatorio | Notas |
|---|---|---|---|---|
| `merchantId` | String | 20 | Sí | "The store Id" |
| `terminalId` | String | 15 | Sí | ECR terminal ID |
| `cashierId` | String | 20 | Sí | |
| `transactionNumber` | String | 20 | Sí | Único generado por el POS |
| `amount` | Decimal | 14 | Sí | Importe |
| `gencode` | String | 20 | Condicional | Obligatorio para digital. Para física, basta `cardNumber` |
| `cardNumber` | String | 60 | Condicional | Obligatorio para activación física |
| `note` | String | 200 | No | Texto libre, guardado en el servidor |
| `track2Data` | String | 60 | No | Banda magnética MSR. Vacío para barcode |

**Response (200)**

Siempre retorna:

| Campo | Tipo | Notas |
|---|---|---|
| `isSuccessful` | Boolean | |
| `errorCode` | String | |
| `errorMessage` | String | |
| `referenceNumber` | String | |
| `previousBalance` | Decimal | |
| `gencode` | String | |
| `amount` | Decimal | |
| `balance` | Decimal | |
| `note` | String | |
| `barcodeNumber` | String | |
| `eGiftCardUrl` | String (URL) | Link al voucher digital para el cliente |
| `cardType` | String | `0`=GC, `1`=Loyalty, `2`=Charity, `9`=Non-registered Loyalty |
| `physicalCardNumber` | String | |
| `issuerId` | String | |
| `mobileNo` | String | |
| `internalProductCode` | String | |
| `pinCode` | String | |
| `initialBalance` | Decimal | |
| `currency` | String | |
| `expireDate` | String (`YYYYMMDD`) | `null` = nunca expira |
| `fileUrl` | String | |
| `cardNumber` | String | |

**Ejemplo de response exitoso:**
```json
{
  "note": "",
  "amount": 50,
  "barcodeNumber": "543486496704423740342",
  "gencode": "3214567008158",
  "eGiftCardUrl": "https://auchan-ts.ogloba.com/eGiftCard/AUCHAN/tEDEbYGBtEkZRhF",
  "errorMessage": null,
  "cardType": "4",
  "errorCode": null,
  "mobileNo": "",
  "previousBalance": 0,
  "physicalCardNumber": "F01933040242402772493",
  "issuerId": "AUCHAN",
  "isSuccessful": true,
  "balance": 50,
  "referenceNumber": "00136544706x",
  "internalProductCode": "3214568729533",
  "pinCode": "4529",
  "initialBalance": 0,
  "currency": "EUR",
  "expireDate": "20260825",
  "fileUrl": "",
  "cardNumber": "543486496704423740342"
}
```

> v2.12: campo `responseType` puede ocultar `cardNumber`, `shortCardNumber`, `pinCode` si `responseType=2`.

---

### 3.2 `POST /redemption` (Step 1)

**Propósito**: redimir saldo de un bono (la operación normal de venta).

**Request body**

| Campo | Tipo | Máx | Obligatorio | Notas |
|---|---|---|---|---|
| `merchantId` | String | 20 | Sí | |
| `terminalId` | String | 15 | Sí | |
| `cashierId` | String | 20 | Sí | |
| `transactionNumber` | String | 20 | Sí | |
| `amount` | Decimal | 14 | Sí | |
| `currency` | String | 3 | No | ISO 4217 |
| `cardNumber` | String | 60 | Sí | |
| `pinCode` | String | 10 | No | **3 intentos incorrectos pueden bloquear la tarjeta** |
| `note` | String | 200 | No | |
| `track2Data` | String | 60 | No | |

**Response (200)** — misma forma que `/activation`, más:

| Campo extra | Tipo | Notas |
|---|---|---|
| `toBeChargedAmt` | Decimal | Importe a cobrar al cliente cuando se combinan medios |
| `requestedCurrency` | Object | Presente cuando la moneda de la tarjeta ≠ moneda de la transacción |
| `gencode` | String | **Añadido en v2.11** |
| `redeemShareDet` | Array | Presente en algunas redenciones |

Estructura de `requestedCurrency`:
```json
{
  "currency": "COP",
  "exchangeRate": 1.0,
  "balance": 50000,
  "previousBalance": 100000,
  "initialBalance": 0
}
```

Estructura de `redeemShareDet[*]`:
```json
[
  { "sharedAmt": 50000, "reloadSource": "0" }
]
```

---

### 3.3 `POST /reload` (Step 1)

**Propósito**: recargar saldo a un bono recargable.

**Request body** — idéntico a `/redemption` excepto que **no pide `pinCode`** y agrega `reason`:

| Campo | Tipo | Máx | Obligatorio | Notas |
|---|---|---|---|---|
| `merchantId` | String | 20 | Sí | |
| `terminalId` | String | 15 | Sí | |
| `cashierId` | String | 20 | Sí | |
| `transactionNumber` | String | 20 | Sí | |
| `amount` | Decimal | 14 | Sí | |
| `currency` | String | 3 | No | |
| `cardNumber` | String | 60 | Sí | |
| `note` | String | 200 | No | |
| `track2Data` | String | 60 | No | |
| `reason` | String | 10 | No | Usar `'03'` para implementar "Reload for Return" |

**Response (200)** — mismo shape que `/activation`. **v2.11** añade `gencode`.

---

### 3.4 `POST /confirmTransaction` (Step 2)

**Propósito**: confirmar la operación del Step 1 (Requested → Confirmed). El saldo se mueve permanentemente.

**Request body**

| Campo | Tipo | Máx | Obligatorio |
|---|---|---|---|
| `merchantId` | String | 20 | Sí |
| `terminalId` | String | 15 | Sí |
| `cashierId` | String | 20 | Sí |
| `referenceNumber` | String | **40** | Sí (eco del Step 1) |

**Response (200)**

| Campo | Tipo | Notas |
|---|---|---|
| `isSuccessful` | Boolean | |
| `errorCode` | String | |
| `errorMessage` | String | |
| `referenceNumber` | String | |
| `balance` | Decimal | Saldo actualizado de la tarjeta |
| `note` | String | |
| `pinCode` | String | Virtual card |
| `pointBalance` | Decimal | **Loyalty Bridge feature** |

---

### 3.5 `POST /cancelTransaction` (Step 2)

**Propósito**: cancelar la operación del Step 1 (Requested → Cancelled). Libera el saldo bloqueado. Solo se puede cancelar si aún está en `Requested`.

**Request body** — idéntico a `/confirmTransaction`:

| Campo | Tipo | Máx | Obligatorio |
|---|---|---|---|
| `merchantId` | String | 20 | Sí |
| `terminalId` | String | 15 | Sí |
| `cashierId` | String | 20 | Sí |
| `referenceNumber` | String | **40** | Sí |

**Response (200)** — mínima: `isSuccessful`, `errorCode`, `errorMessage`, `note`.

---

### 3.6 `POST /reconciliation` (Step 3)

**Propósito**: cierre administrativo / sincronización de libros entre cliente y Ogloba. Admite batch.

**Request body**

| Campo | Tipo | Máx | Obligatorio | Notas |
|---|---|---|---|---|
| `merchantId` | String | 20 | Sí | |
| `businessDate` | String | 8 | Sí | `YYYYMMDD` |
| `reconciliationRecords` | Array | 1000 | Sí | |
| `reconciliationRecords[*].terminalTxNo` | String | 50 | Sí | |
| `reconciliationRecords[*].lineCount` | Decimal | 4 | Sí | |
| `reconciliationRecords[*].terminalId` | String | 15 | Sí | |
| `reconciliationRecords[*].cashierId` | String | 20 | Sí | |
| `reconciliationRecords[*].transactionNumber` | String | 20 | Sí | |
| `reconciliationRecords[*].referenceNumber` | String | 20 | Sí | |
| `reconciliationRecords[*].transactionType` | String | 1 | Sí | **`A`** activation, **`L`** reload, **`P`** redemption, **`V`** void |
| `reconciliationRecords[*].cardNumber` | String | 60 | Sí | |
| `reconciliationRecords[*].currency` | String | 3 | Sí | |
| `reconciliationRecords[*].amount` | Decimal | 14 | Sí | |
| `reconciliationRecords[*].finalStatus` | String | 1 | Sí | **`Y`** confirmed, **`N`** cancelled |

**Response (200)**: `isSuccessful`, `errorCode`, `errorMessage`, `note`, `isSuccess` (duplicado legacy).

---

### 3.7 `POST /reversal`

**Propósito**: reversar una transacción cuando no se recibió respuesta del Step 1 (timeout, error de red, excepción). Deshace cualquier side-effect pendiente. **SIEMPRE** con el mismo `transactionNumber` que se usó en el Step 1.

**Request body**

| Campo | Tipo | Máx | Obligatorio | Notas |
|---|---|---|---|---|
| `merchantId` | String | 20 | Sí | |
| `terminalId` | String | 15 | Sí | |
| `cashierId` | String | 20 | Sí | |
| `originalMerchantId` | String | 20 | Sí | **Merchant ID original** |
| `originalTerminalId` | String | 15 | Sí | **Terminal ID original** |
| `originalCashierId` | String | 20 | Sí | **Cashier ID original** |
| `originalTransNumber` | String | 20 | Sí | **`transactionNumber` original** |
| `transactionNumber` | String | 20 | Sí | ID de la transacción actual (la reversal) |

**Response (200)**

| Campo | Tipo | Notas |
|---|---|---|
| `isSuccessful` | Boolean | |
| `errorCode` | String | |
| `errorMessage` | String | |
| `balance` | Decimal | Saldo actual de la tarjeta |
| `handlingFee` | Decimal | **Importe reversado** |
| `previousBalance` | Decimal | Saldo previo |
| `note` | String | |
| `issuerId` | String | |
| `requestedCurrency` | Object | Presente cuando la moneda de la reversal ≠ moneda de la tarjeta (**v2.11**) |

> El módulo envía los cuatro campos `original*` (`originalMerchantId`, `originalTerminalId`,
> `originalCashierId`, `originalTransNumber`), armados por `GiftCardRequestFactory`.

---

### 3.8 `POST /voidTransaction`

> **El path es `/voidTransaction`, no `/void`.** El nombre corto circuló en documentación interna
> antigua; la colección oficial confirma el path completo, y así está en `OglobaGiftCardProvider`.
>
> **Ningún caso de uso invoca este endpoint**: los bonos no se anulan desde el POS
> ([DECISIONES.md](DECISIONES.md#d-04)). Y ojo con la alternativa: `/reversal` **no** sirve para
> deshacer una redención ya confirmada — solo un Step 1 que quedó en `Requested`.

**Propósito**: anular una transacción **ya confirmada**. Es la devolución comercial.

**Request body**

| Campo | Tipo | Máx | Obligatorio | Notas |
|---|---|---|---|---|
| `merchantId` | String | 20 | Sí | |
| `terminalId` | String | 15 | Sí | |
| `cashierId` | String | 20 | Sí | |
| `originalMerchantId` | String | 20 | Sí | |
| `originalTerminalId` | String | 15 | Sí | |
| `originalCashierId` | String | 20 | Sí | |
| `referenceNumber` | String | **40** | Sí | |
| `transactionNumber` | String | 20 | No | |
| `note` | String | 200 | No | |
| `reason` | String | 10 | No | |
| `transactionTime` | String | 19 | No | `YYYY-MM-DD hh24:mi:ss` |

**Response (200)** — mismo shape que `/reversal`: `isSuccessful`, `errorCode`, `errorMessage`, `balance`, `handlingFee`, `previousBalance`, `note`, `issuerId`, `requestedCurrency`.

---

### 3.9 `POST /orderCreation` (NUEVO)

**Propósito**: crear un lote de pedidos de tarjetas digitales. Dos modos: `MA` (Batch card activation) o `WL` (Reload member's Wallet).

**Request body**

| Campo | Tipo | Máx | Obligatorio | Notas |
|---|---|---|---|---|
| `merchantId` | String | 20 | Sí | |
| `terminalId` | String | 15 | Sí | |
| `cashierId` | String | 20 | Sí | |
| `clientOrderNo` | String | 50 | Sí | ID de pedido nuestro |
| `salesType` | String | 2 | Sí | `MA` o `WL` |
| `orderItems` | Array | 100 | Sí | |
| `orderItems[*].itemCode` | String | 20 | Sí (específico) | |
| `orderItems[*].faceAmount` | Decimal | 14 | Sí | Valor facial |
| `orderItems[*].quantity` | Number | — | Sí | Cantidad |
| `orderItems[*].deliverType` | String | 1 | Sí | `0`=API, `1`=SMS, `2`=PDF (≥v2.11), `3`=eMail, `4`=SMS&eMail |
| `orderItems[*].deliverDate` | Date | — | No | Vacío = asap |
| `orderItems[*].message` | String | 100 | No | |
| `orderItems[*].receiverEmail` | String | 100 | No | según `deliverType` |
| `orderItems[*].receiverMobileNo` | String | 60 | No | |
| `orderItems[*].receiverAddress` | String | 300 | No | |
| `orderItems[*].receiverName` | String | 100 | No | |
| `orderItems[*].senderEmail` | String | 100 | No | |
| `orderItems[*].senderName` | String | 100 | No | |

**Response (200)**

| Campo | Tipo | Notas |
|---|---|---|
| `isSuccessful` | Boolean | |
| `errorCode` | String | |
| `errorMessage` | String | |
| `orderAmount` | Decimal | |
| `orderAmountVAT` | Decimal | |
| `orderAmountEXVAT` | Decimal | |
| `orderNo` | String | ID de pedido asignado por Ogloba |

---

### 3.10 `POST /orderConfirm` (NUEVO)

**Propósito**: confirmar el pago de un pedido creado.

**Request body**

| Campo | Tipo | Máx | Obligatorio | Notas |
|---|---|---|---|---|
| `merchantId` | String | 20 | Sí | |
| `terminalId` | String | 15 | Sí | |
| `cashierId` | String | 20 | Sí | |
| `orderNo` | String | 50 | Sí | Del `orderCreation` |
| `paymentList` | Array | 10 | No | |
| `paymentList[*].paymentType` | String | 2 | Sí | Default `"01"` |
| `paymentList[*].paymentId` | String | 50 | Sí | Único |
| `shippingFee` | Decimal | — | Sí | |
| `cardFee` | Decimal | — | Sí | |
| `returnFee` | Decimal | — | Sí | |

**Response (200)**

| Campo | Tipo | Notas |
|---|---|---|
| `isSuccessful` | Boolean | |
| `errorCode` | String | |
| `errorMessage` | String | |
| `orderAmount` | Decimal | |
| `orderAmountVAT` | Decimal | |
| `orderAmountEXVAT` | Decimal | |
| `orderNo` | String | |
| `orderStatus` | String | `03` no confirmado, `042` confirmado/tarjetas-listas, `043` confirmado/tarjetas-no-listas |
| `listOfCards` | Array | Cada item con `cardNumber`, `shortCardNumber`, `pinCode1`, `pinCode2`, `gencode`, `expiryDate` (`YYYY-MM-DD`, null=never), `cardBalance` |

---

### 3.11 `POST /orderCancel` (NUEVO)

**Propósito**: cancelar un pedido aún no confirmado.

**Request body**

| Campo | Tipo | Máx | Obligatorio |
|---|---|---|---|
| `merchantId` | String | 20 | Sí |
| `terminalId` | String | 15 | Sí |
| `cashierId` | String | 20 | Sí |
| `orderNo` | String | 50 | Sí |

**Response (200)**: `isSuccessful`, `errorCode`, `errorMessage`, `note`.

---

### 3.12 `POST /orderStatus` (NUEVO)

**Propósito**: consultar el estado actual de un pedido (incluye detalle de devoluciones).

**Request body** — idéntico a `/orderCancel`.

**Response (200)**

| Campo | Tipo | Notas |
|---|---|---|
| `isSuccessful` | Boolean | |
| `errorCode` | String | |
| `errorMessage` | String | |
| `orderAmount` | Decimal | |
| `orderNo` | String | |
| `orderStatus` | String | |
| `listOfCards` | Array | |
| `listOfCards[*].cardNumber` | String | |
| `listOfCards[*].shortCardNumber` | String | |
| `listOfCards[*].pinCode1` | String | |
| `listOfCards[*].pinCode2` | String | |
| `listOfCards[*].gencode` | String | |
| `listOfCards[*].expiryDate` | String | `YYYY-MM-DD` |
| `listOfCards[*].faceAmount` | Decimal | |
| `listOfCards[*].eGiftCardUrl` | String (URL) | PDF debe descargarse localmente |
| `listOfCards[*].cardPdfUrl` | String (URL) | Solo válido por **7 días**, descargar localmente |
| `listOfCards[*].deliveryType` | String | |
| `listOfCards[*].email` | String | |
| `listOfCards[*].mobileNo` | String | |
| `listOfCards[*].deliverAddress` | String | |
| `listOfCards[*].deliveryTracking` | String | |
| `listOfCards[*].returnOrderList` | Array | **v2.16**. Cada item con `returnOrderNo`, `returnStatus` (`02` cancelled, `04` submitted, `06` approved), `returnedAmount`, `returnedQty`, `returnedCards` (String Array) |

> ⚠️ La doc menciona explícitamente `041 y 043` como "en proceso", pero `/orderConfirm` solo lista `043`. Tratar `041` como `043` (sigue en proceso, reintentar).

---

### 3.13 `POST /orderReturn` (NUEVO)

**Propósito**: devolución total o parcial de un pedido confirmado. **Algunos productos no se pueden devolver** (`isRefundable=0` en `/getProducts`).

**Request body**

| Campo | Tipo | Máx | Obligatorio | Notas |
|---|---|---|---|---|
| `merchantId` | String | 20 | Sí | |
| `terminalId` | String | 15 | Sí | |
| `cashierId` | String | 20 | Sí | |
| `orderNo` | String | 50 | Sí | |
| `returnType` | String | 1 | Sí | `F`=full, `P`=partial |
| `returnFee` | Decimal | — | Sí | |
| `cardFee` | Decimal | — | Sí | |
| `shippingFee` | Decimal | — | Sí | |
| `partialReturnDetails` | Array | — | Si `returnType=P` | |
| `partialReturnDetails[*].cardNoB` | String | 60 | Sí | Rango inicio |
| `partialReturnDetails[*].cardNoE` | String | 60 | Sí | Rango fin |
| `partialReturnDetails[*].faceValue` | Decimal | — | Sí | Valor facial unitario |
| `partialReturnDetails[*].itemCode` | String | 20 | Sí | |
| `partialReturnDetails[*].qty` | Number | — | Sí | (qty o cardNoB/cardNoE) |

**Response (200)**

| Campo | Tipo | Notas |
|---|---|---|
| `isSuccessful` | Boolean | |
| `errorCode` | String | |
| `errorMessage` | String | |
| `status` | String | `04` = 1st approval, `06` = AR approval |
| `returnNo` | String | ID de la devolución |

---

### 3.14 `POST /balance`

**Propósito**: consultar saldo disponible y estado actual de una tarjeta. Recomendado antes de cualquier redemption.

**Request body**

| Campo | Tipo | Máx | Obligatorio | Notas |
|---|---|---|---|---|
| `merchantId` | String | 20 | Sí | |
| `terminalId` | String | 15 | Sí | |
| `cashierId` | String | 20 | Sí | |
| `transactionNumber` | String | 20 | Sí | Único por intento |
| `cardNumber` | String | 60 | Sí | |
| `pinCode` | String | 10 | No | 3 intentos incorrectos pueden bloquear |

**Response (200)**

| Campo | Tipo | Notas |
|---|---|---|
| `isSuccessful` | Boolean | |
| `errorCode` | String | |
| `errorMessage` | String | |
| `cardNumber` | String | |
| `gencode` | String | |
| `cardType` | String | `0`=GC, `1`=Loyalty, `2`=Charity, `9`=Non-reg Loyalty |
| `previousBalance` | Decimal | |
| `balance` | Decimal | |
| `amount` | Decimal | |
| `initialBalance` | Decimal | |
| `issuerId` | String | |
| `mobileNo` | String | |
| `note` | String | |
| `internalProductCode` | String | |
| `expireDate` | String | `YYYYMMDD`. `null` = nunca expira |
| `expiryAmountNextMon` | Decimal | Saldo que expira el próximo mes |
| `activationMinAmount` | String | Mínimo activable |
| `cardStatus` | String | `GENERATE`, `ALLOCATE`, `IN USE`, `SUSPEND`, `REFUND`, `EXPIRE` |
| `maxReloadAmount` | Decimal | Tope de recarga |
| `expiryDateNextMon` | String | `YYYYMMDD`. Fecha del próximo vencimiento |
| `productType` | String | `C`=Card, `V`=Voucher, `U`=Coupon |
| `activationDate` | String | `YYYYMMDD HH24:MI:SS`. **v2.13** |

> El módulo envía `merchantId`, `terminalId`, `cashierId`, `transactionNumber` y, cuando aplica,
> `pinCode`. Enviar solo `merchantId` y `cardNumber` era la causa de un HTTP 400 en las primeras
> pruebas.

---

### 3.15 `POST /queryTransactionsHistory`

**Propósito**: consultar historial de transacciones con filtros y paginación. Útil para auditoría / Bitácora.

**Request body** — al menos un filtro, **todos los demás opcionales**:

| Campo | Tipo | Máx | Obligatorio | Notas |
|---|---|---|---|---|
| `merchantId` | String | 20 | No | Filtro por tienda |
| `terminalId` | String | 15 | No | |
| `cashierId` | String | 20 | No | (la doc tiene typo "Sting") |
| `transDateFrom` | String | 16 | No | `YYYYMMDD hh:mm:ss` |
| `transDateTo` | String | 16 | No | |
| `cardNumber` | String | 60 | No | |
| `transType` | String | 1 | No | `A` activation, `P` redemption, `L` reload, `V` void |
| `transactionStatus` | String | 1 | No | `R` requested, `Y` confirmed, `N` cancelled |
| `reconciliationStatus` | String | 1 | No | `S` success, `F` failed, `W` wait |
| `orderNo` | String | 50 | No | |
| `referenceNumber` | String | 40 | No | |
| `pageNo` | Integer | — | Sí | Default 1 |
| `numberOfPage` | Integer | — | Sí | Default 10 |

**Response (200)**

| Campo | Tipo | Notas |
|---|---|---|
| `isSuccessful` | Boolean | |
| `transactionRecord` (también `transactionRecords` — ver §7) | Array | |
| `pointTransactionRecord` | Array | Loyalty |

Cada item de `transactionRecords[*]`:

| Campo | Tipo | Notas |
|---|---|---|
| `merchantId` | String | |
| `terminalId` | String | |
| `cashierId` | String | |
| `transactionTime` | String | `YYYY-MM-DD hh:mm:ss` |
| `transactionType` | String | `ACTIVATION`, `RELOAD`, `REDEMPTION`, `VOID` |
| `transactionNumber` | String | |
| `referenceNumber` | String | |
| `previousBalance` | Decimal | |
| `currency` | String | |
| `transactionAmount` | Decimal | |
| `balance` | Decimal | |
| `cardNumber` | String | |
| `gencode` | String | |
| `storeName` | String | |
| `pinCode1` | String | |
| `pinCode2` | String | |
| `shortCardNumber` | String | |
| `transactionStatus` | String | `R`/`Y`/`N` |
| `txnTypeDet` | String | `2` Activation, `0` Reload, `1` Reload for Return, `3` Redemption, `4` Loyalty point |
| `rowNumber` | Integer | |
| `totalRowCount` | Integer | |
| `issuerId` | String | |
| `reconciliationResult` | String | `S`/`F`/`W` |
| `expireDate` | String | |

---

### 3.16 `POST /getProducts`

**Propósito**: listar productos de bono disponibles en la tienda.

**Request body**

| Campo | Tipo | Máx | Obligatorio | Notas |
|---|---|---|---|---|
| `merchantId` | String | 20 | Sí | |
| `terminalId` | String | 15 | Sí | |
| `itemCode` | String | 20 | No | Filtra a un solo producto |

**Response (200)**

| Campo | Tipo | Notas |
|---|---|---|
| `isSuccessful` | Boolean | |
| `errorCode` | String | |
| `errorMessage` | String | |
| `productList` | Array | |

Cada item de `productList[*]`:

| Campo | Tipo | Notas |
|---|---|---|
| `itemCode` | String | |
| `productName` | String | |
| `isFixValue` | Number | `0`=variable, `1`=fijo |
| `faceValue` | Decimal | (cuando `isFixValue=1`) |
| `currency` | String | |
| `reloadList` | Decimal array | Valores de activación permitidos (cuando `isFixValue=0`) |
| `allowedActivate` | Number | `0`/`1` |
| `allowedReload` | Number | `0`/`1` |
| `allowedRedeem` | Number | `0`/`1` |
| `productType` | String | Card / Voucher / Coupon |
| `termAndCondition` | String | |
| `productImage` | String (URL) | |
| `activationMinAmt` | Decimal | cuando `isFixValue=0` y reloadList vacío |
| `activationMaxAmt` | Decimal | |
| `description` | String | |
| `maxCardAmount` | Decimal | |
| `minCardAmount` | Decimal | |
| `b2bDiscountRate` | Decimal | |
| `brandId` | Number | **v2.15** |
| `brandName` | String | |
| `brandLogo` | String (URL) | |
| `responseType` | Number | `1`=normal+URL+PDF, `2`=solo PDF. **v2.12** |
| `cardValidity` | Number | Meses. `0`=ilimitado |
| `category` | Array | Cada item `{categoryId, categoryName}` |
| `country` | Array | **v2.14** |
| `isRefundable` | Number | `0`/`1`. **v2.13** |
| `redeemLocation` | String | `O`/`P`/`OP` |
| `isDisplayedOnPortal` | Number | `0`/`1`/`2`/`3`. **v2.16** |
| `activateAmtInterval` | Decimal | Granularidad. **v2.19** |
| `productDescription` | String | **v2.20** |
| `productInstructions` | String | **v2.18** |

> **Campos en respuesta real pero NO documentados en la tabla/changelog**: `transFeeType`, `transFeeValue`. Probablemente transitorios. Confirmar con Ogloba.

---

### 3.17 `GET /getBuInfo`

**Propósito**: info del Business Unit: nombre, saldo disponible del comercio, neto.

> ⚠️ Verbo HTTP: **GET**, no POST. Auditar `OglobaGiftCardProvider`.

**Request body** (sí, aunque sea GET, según ejemplo del Postman):
```json
{ "itemCode": null }
```

Tabla dice solo `itemCode` (String 20, opcional).

**Response (200)**

| Campo | Tipo | Notas |
|---|---|---|
| `isSuccessful` | Boolean | |
| `errorCode` | String | |
| `errorMessage` | String | |
| `availableBalance` | Decimal | Saldo del BU |
| `buName` | String | |
| `buId` | String | |
| `unifyNo` | String | External Id del BU |
| `netBalance` | Decimal | **v2.20** |

---

### 3.18 `GET /test`

**Propósito**: health check. Devuelve `{"isSuccessful": true}`.

> ⚠️ Verbo HTTP: **GET**, no POST.

**Request body**: ninguno.

**Response (200)**: `isSuccessful` (Boolean). `errorCode` / `errorMessage` solo en fallo.

---

## 4. Webhooks (Ogloba → nosotros)

Ogloba puede hacer POST a **nuestra URL** (debe ser HTTPS, configurada por Ogloba) para notificarnos eventos.

### 4.1 Webhook de `orderStatus`

- **URL configurable**: `<our_url>/orderStatus` (el sufijo `/orderStatus` lo añade Ogloba; el prefijo lo configuramos nosotros con Ogloba).
- **Body**:
```json
{ "token": ["<JWT>"] }
```
- **JWT decodificado**:
```json
{
  "orderNo": "MA240300001",
  "confirmDate": "2024/03/20",
  "orderAmt": 10000,
  "orderStatus": "042",
  "errorCode": "0"
}
```

Los 5 campos del JWT son obligatorios (string/number). Valores de `orderStatus`:
- `01` created
- `02` cancelled
- `03` awaiting confirmation
- `042` completed
- `043` in progress

### 4.2 Webhook de catálogo

- **URL configurable** (sin sufijo fijo), POST con body:
```json
{ "itemCode": "20240311153903215" }
```
- Tras recibirlo, llamar a `/getProducts?itemCode=...` para obtener detalles actualizados.

---

## 5. Códigos de error completos

| Código | Significado |
|---|---|
| 22 | Unknown product |
| 23 | Unknown store |
| 24 | Invalid card in this store |
| 25 | This card can't be use in this store |
| 38 | Store/BU not enough credit |
| 39 | Store/BU not enough credit |
| 50 | Unknown reference or terminal |
| 52 | Unknown card |
| 53 | Card balance not enough |
| 54 | Activation amount missing |
| 56 | Card expired |
| 57 | Card archived |
| 59 | Invalid cashier |
| 73 | Invalid amount |
| 81 | Invalid card format |
| 85 | Card already activated |
| 86 | Member detail not found |
| 99 | Suspicion of fraud |
| 104 | The card is inactive |
| 105 | The card is blocked |
| 106 | The card is not blocked |
| 109 | Transaction validated, cancellation impossible |
| 110 | The card is suspended |
| 111 | The card has been refunded |
| 112 | The map has been destroyed |
| 151 | Card not available for sale |
| 152 | Card not activatable |
| 153 | Card not usable to pay |
| 154 | Non-reloadable card |
| 155 | Mnt max recharge reached |
| 156 | Insufficient amount |
| 157 | Amount too large |
| 158 | Invalid amount |
| 159 | Invalid amount |
| 160 | Negative amount rejected |
| 161 | Card already used |
| 162 | Internal processing error |
| 206 | Internal processing error |
| 207 | Internal processing error |
| 208 | Internal processing error |
| 209 | Internal processing error |
| 210 | Internal processing error |
| 211 | Internal processing error |
| 212 | Internal processing error |
| 213 | Internal processing error |
| 214 | Internal processing error |
| 215 | Internal processing error |
| 216 | Internal processing error |
| 217 | Internal processing error |
| 218 | Internal processing error |
| 219 | Internal processing error |
| 220 | Internal processing error |
| 221 | Internal processing error |
| 222 | Terminal/cash register inactive |
| 226 | Internal processing error |
| 227 | Internal processing error |
| 228 | Internal processing error |
| 229 | Internal processing error / Blacklisted card |
| 230 | Transaction already cancelled |
| 231 | Incorrect pin code |
| 232 | Card already used, cannot be refunded |
| 233 | Cancellation failed |
| 234 | Unauthorized partial payment |
| 235 | Negative balance |
| 236 | The card is not active |
| 237 | The card is not refundable |
| 238 | Deadline for cancellation |
| 239 | Incorrect terminal/origin reference |
| 240 | This card is no longer valid |
| 241 | Card blocked, too many attempts |
| 252 | Too many pending transactions |
| 322 | Member blocked |
| 401 | Authorization failure |
| 998 | Unexpected error |
| 999 | Unexpected error |
| 23005 | Order creation failure |
| 23010 | Unknown order no. |
| 23013 | Order approval failure |
| 23014 | Order return failure |
| 23016 | Order already returned |
| 230040 | Cashier Id wrong format |

---

## 6. Changelog v2.18 → v2.20

| Versión | Fecha (aprox.) | Cambios |
|---|---|---|
| **2.20** | (Postman 2024-07) | • `productDescription` añadido a `/getProducts`<br>• `netBalance` añadido a `/getBuInfo` |
| **2.19** | | • `activateAmtInterval` añadido a `/getProducts` |
| **2.18** | 2025-08-22 (según changelog del Postman) | • `productInstructions` añadido a `/getProducts` |
| **2.16** | | • `returnOrderList` añadido a `orderStatus`<br>• `trackingNo` añadido a `orderStatus`<br>• `isDisplayedOnPortal` añadido a `/getProducts` |
| **2.15** | | • `brandId` añadido a `/getProducts` |
| **2.14** | | • `country` añadido a `/getProducts` |
| **2.13** | | • `isRefundable` añadido a `/getProducts`<br>• `activationDate` añadido a `/balance` |
| **2.12** | | • `referenceNumber` max length elevado a **40** (era 20)<br>• `responseType` añadido (oculta `cardNumber`/`shortCardNumber`/`pinCode` cuando `responseType=2`) |
| **2.11** | | • `gencode` añadido a responses de `/reload` y `/redemption`<br>• `requestedCurrency` (Object) añadido a `/void` y `/reversal` |

---

## 7. Inconsistencias internas de la documentación de Ogloba

Detectadas al revisar el Postman Collection. **A flag con Ogloba** antes de tomar decisiones que dependan de estos puntos.

1. **`queryTransactionsHistory`**: la tabla de response indica `transactionRecords` (plural) pero el ejemplo de body usa `transactionRecord` (singular). Soportar ambos nombres.
2. **`cardType` enums**: la tabla de `/activation` lista `0=GC, 1=Loyalty, 2=Charity, 9=Non-registered Loyalty`, pero el ejemplo de body muestra `"cardType": "4"` (no documentado). Posibles valores extendidos no enumerados.
3. **`orderStatus` códigos de proceso**: `orderStatus` documenta `041 y 043` como "en proceso", pero `orderConfirm` solo lista `043`. Tratar `041` como equivalente a `043` hasta confirmar.
4. **Duplicados legacy**: `/reconciliation` retorna `isSuccess` además de `isSuccessful` en su response. Marcar como deprecated pero mantener compatibilidad.
5. **`/getProducts` campos sin documentar**: `transFeeType`, `transFeeValue` aparecen en respuestas reales pero no están en la tabla ni en el changelog. Probablemente transitorios.
6. **`/reconciliation` campos del record**: el campo se llama `reconciliationRecords` (plural) en la tabla pero la doc lo lista como singular en algunos lugares.
7. **`/queryTransactionsHistory` typo**: la doc dice "Sting" en lugar de "String" para el tipo de `cashierId`.
8. **`Accept-Language` ejemplo inconsistente**: el Postman usa `fr-fr` (en minúscula) pero la doc dice `FR-fr` (mixto). Usar lowercase según RFC.

---

## 8. Pendientes del contrato con Ogloba

Estado revisado contra el código el **2026-09-02**. Lo ya resuelto está listado en §0; acá quedan
solo los puntos abiertos. Los pendientes del proyecto completo, con dueño y riesgo, están en
[ESTADO.md](ESTADO.md).

| # | Pendiente | Detalle |
|---|---|---|
| 1 | **Versión de la API en producción** | `2.18` está verificado contra el sandbox. Falta confirmación escrita de Ogloba para `co-prod`. La diferencia con `2.20` es aditiva (campos nuevos en `/getProducts` y `/getBuInfo`) |
| 2 | **Código de producto digital de producción** | `Release` declara `113811` (dotación corporativa B2B), que no es el equivalente del bono B2C `113816` usado en sandbox. Confirmar con Ogloba ([DECISIONES.md](DECISIONES.md#d-14)) |
| 3 | **Nombres inconsistentes en `/queryTransactionsHistory`** | Ogloba usa `transactionRecord` y `transactionRecords` según el lugar de su documentación. Soportar los dos |
| 4 | **Cobertura de `OglobaFailureMapper`** | Menos crítico desde que Ogloba localiza los mensajes, pero la clasificación en rechazo vs. incierto **decide si se dispara `/reversal`**: conviene auditarla contra la tabla completa de §5 |
| 5 | **Receptor de webhooks** (`orderStatus` y catálogo) | Sin implementar. Requiere un endpoint HTTPS acordado con Ogloba |
| 6 | **Contraseña de Basic Auth en producción** | Sin definir si es única por tienda o común a las 512 |

---

## Apéndice A — Cómo regenerar este documento

Este MD se extrajo vía:

```bash
curl 'https://documenter.gw.postman.com/api/collections/32475087/2s9YymGPrn?segregateAuth=true&versionTag=latest' \
  -o ogloba-postman.json
```

Si Ogloba publica cambios, repetir la descarga y comparar `versionTag=latest` con la versión
vigente (el identificador `2s9YymGPrn` puede quedar obsoleto; revisar `publishedId`).

Al actualizar el documento: mantener la numeración de las secciones, porque el código las cita con
la forma `docs/OGLOBA_API_REFERENCE.md §N`, y registrar en §0 cualquier cambio de estado de
implementación.

---

**Base**: colección Postman de Ogloba publicada el 2024-07-24 · extraída el 2026-07-28.
**Última revisión contra el código**: 2026-09-02.
**Mantenedor**: equipo Permoda.Pay · Permoda Ltda.
