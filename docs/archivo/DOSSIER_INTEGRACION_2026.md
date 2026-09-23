# [ARCHIVADO] Dossier de integración TEF Ogloba ↔ HiPOS — KOAJ (Permoda)

> # ⚠️ DOCUMENTO ARCHIVADO — NO ES FUENTE DE VERDAD
>
> Este es el dossier de integración tal como se acordó con Ogloba e ICG en **julio de 2026**. Se
> conserva por trazabilidad histórica. **Donde este documento y el código difieran, manda el
> código**, y la documentación vigente está en [`docs/`](../README.md).
>
> Diferencias conocidas frente al estado actual, para leerlo sin equivocarse:
>
> | Este documento dice | Hoy es |
> | --- | --- |
> | Cuatro acciones de HiPOS (`INITIALIZE`, `FINALIZE`, `GET_VERSION`, `TRANSACTION`) | Once acciones de pago más `TOTALIZATION_CANCELED` ([INTEGRACION_HIPOS.md](../INTEGRACION_HIPOS.md)) |
> | `apk_name` `oglobapay` / `tefogloba` | `permoglobal`, leído del APK que distribuye CloudLicense ([DECISIONES.md](../DECISIONES.md#d-01)) |
> | Nueve endpoints de Ogloba | Dieciocho, con Order management incluido ([OGLOBA_API_REFERENCE.md](../OGLOBA_API_REFERENCE.md)) |
> | Importes en centavos | **Pesos enteros** hacia Ogloba ([DECISIONES.md](../DECISIONES.md#d-02)) |
> | La activación entra por el intent de HiPOS | La activación es un flujo manual del módulo ([DECISIONES.md](../DECISIONES.md#d-07)) |
> | Escenarios de anulación y recarga como parte de la certificación | Fuera de alcance por decisión de KOAJ ([ESTADO.md](../ESTADO.md)) |
>
> Contiene además correspondencia con el proveedor y credenciales de prueba que **no deben
> copiarse a documentos nuevos** ni citarse como configuración vigente.

Cliente: **KOAJ** (marca de moda del grupo **Permoda Ltda**, Colombia)
Pasarela: **Ogloba GiftCard** (bonos físicos y digitales)
POS: **HiPOS** (HioPosCloud) de ICG, sobre tablets Android
Fecha del documento: julio de 2026

---

## Tabla de contenido

1. [Contexto y actores del proyecto](#1-contexto-y-actores-del-proyecto)
2. [Las 512 tiendas KOAJ en el back office de Ogloba](#2-las-512-tiendas-koaj-en-el-back-office-de-ogloba)
3. [Datos de prueba completos (testing)](#3-datos-de-prueba-completos-testing)
4. [Arquitectura: tu APK habla con Ogloba por HTTPS](#4-arquitectura-tu-apk-habla-con-ogloba-por-https)
5. [Los 9 endpoints REST de Ogloba](#5-los-9-endpoints-rest-de-ogloba)
6. [El flujo obligatorio de 3 pasos](#6-el-flujo-obligatorio-de-3-pasos)
7. [Lo que HiPOS espera de tu módulo (4 acciones)](#7-lo-que-hipos-espera-de-tu-módulo-4-acciones)
8. [Mapeo Ogloba ↔ HiPOS](#8-mapeo-ogloba--hipos)
9. [Estrategia de implementación en .NET](#9-estrategia-de-implementación-en-net)
10. [El TransactionLogger — la pieza que el XLSX UAT te obliga a construir](#10-el-transactionlogger--la-pieza-que-el-xlsx-uat-te-obliga-a-construir)
11. [El OglobaFlow — la máquina de estados de 3 pasos](#11-el-oglobaflow--la-máquina-de-estados-de-3-pasos)
12. [Secuencia completa de un SALE con bono Ogloba](#12-secuencia-completa-de-un-sale-con-bono-ogloba)
13. [Manejo de errores y los 80+ códigos de Ogloba](#13-manejo-de-errores-y-los-80-códigos-de-ogloba)
14. [La plantilla UAT que Ogloba te pide diligenciar (8 escenarios)](#14-la-plantilla-uat-que-ogloba-te-pide-diligenciar-8-escenarios)
15. [Compromisos de la solicitud HiOSTORE](#15-compromisos-de-la-solicitud-hiostore)
16. [Checklist de certificación final](#16-checklist-de-certificación-final)
17. [Contactos clave](#17-contactos-clave)
18. [Anexo: los 10 correos electrónicos completos](#18-anexo-los-10-correos-electrónicos-completos)
19. [Anexo: cuerpo completo de los 9 endpoints JSON de Ogloba](#19-anexo-cuerpo-completo-de-los-9-endpoints-json-de-ogloba)

---

## 1. Contexto y actores del proyecto

KOAJ (cadena de tiendas de moda colombiana, marca del grupo Permoda) tiene desplegadas tablets HiPOS en sus **512 tiendas** en Colombia. Hasta hoy, no tienen integrada ninguna pasarela de gift cards/bonos. Ahora quieren empezar a aceptar **bonos Ogloba** (físicos y digitales) como medio de pago.

Para eso hay que construir una **APK Android** que se comunique con el gateway de Ogloba vía REST/JSON sobre HTTPS, y que cumpla con el contrato de HiPOS (Intents Android).

### Actores

| Rol | Persona | Email | Teléfono |
|---|---|---|---|
| **Solicitante original (KOAJ/Permoda)** | Luis Felipe Quintero Mejía | `luisfqm@permoda.com.co` | — |
| **Project lead KOAJ/Permoda (firmó solicitud HiOSTORE)** | Roger Moreno | `rogerm@permoda.com.co` | +57 312 470 7394 |
| **IT Lead Latam Ogloba** | _removed_ | _removed_ | _removed_ |
| **Soporte Ogloba** | Marcus Lin | `team.support@ogloba.com` | — |
| **Customer Success Ogloba** | Maria Andrea | `maa@ogloba.com` | — |

### Datos del proyecto

- **App a construir**: Módulo de Cobro Electrónico Ogloba (publicado en HiOSTORE)
- **Cliente retail**: KOAJ (marca del grupo Permoda Ltda, Colombia)
- **Empresa integradora / Partner Advanced de ICG**: Permoda Ltda
- **Tiendas a integrar**: 512 tiendas KOAJ registradas en el back office de Ogloba con Store IDs `K00002`, `K00003`, `K00009`, `K00012`, `K00026`, `K00031`, `K00036`, `K00037`, `K00041`, `K00042`, `K00054`, `K00056`, `K00060`, ...
- **Solicitud HiOSTORE**: firmada por Roger Moreno el 14/07/2026
- **Funciones HiPOS a implementar**: `INITIALIZE`, `FINALIZE`, `GET_VERSION`, `TRANSACTION`
- **Componentes internos** (de la solicitud): Proceso de venta · Gestión de respuesta · Gestión de token · Servicio API
- **Plantilla UAT recibida**: XLSX de Ogloba con 4 escenarios de proceso + 4 de error para diligenciar y devolver

### Lo que hay hoy vs. lo que hay que construir

| Hoy | Mañana |
|---|---|
| 512 tablets HiPOS en tiendas KOAJ | Misma tablet + nueva APK de cobro electrónico |
| Sin pasarela de gift card/bonos | Bonos Ogloba físicos y digitales como medio de pago |
| 10 tarjetas físicas de prueba del producto `113817` | Las 512 tiendas listas para producción |
| Back office Ogloba con 512 tiendas KOAJ precargadas (Store IDs K000XX) | App validada por Roger |

---

## 2. Las 512 tiendas KOAJ en el back office de Ogloba

El back office de Ogloba (`https://co-ts.ogloba.com/gcap/`) ya tiene cargadas las tiendas de KOAJ en la vista **Store/Department**. La captura que envió el equipo de KOAJ muestra la paginación "1–20 / 512".

### Ejemplo de tiendas KOAJ registradas (primeras 13 de 512)

| Store ID | Unify No. | Store Formal Name | Store Type | Merchandise System Store Code | ERP Store Code | ECR Store Code |
|---|---|---|---|---|---|---|
| K00002 | K00002 | TIENDA KOAJ PUNTO 2 | STORE | K00002 | K00002 | K00002 |
| K00003 | K00003 | TIENDA KOAJ PUNTO 4 | STORE | K00003 | K00003 | K00003 |
| K00009 | K00009 | TIENDA KOAJ GALERIAS | STORE | K00009 | K00009 | K00009 |
| K00012 | K00012 | TIENDA KOAJ SUBA IMPERIAL | STORE | K00012 | K00012 | K00012 |
| K00026 | K00026 | TIENDA KOAJ SAN MARTIN P2 | STORE | K00026 | K00026 | K00026 |
| K00031 | K00031 | TIENDA KOAJ CAFAM FLORESTA | STORE | K00031 | K00031 | K00031 |
| K00036 | K00036 | TIENDA KOAJ UNICENTRO | STORE | K00036 | K00036 | K00036 |
| K00037 | K00037 | T.KOAJ CALLE 18 MONTEVIDEO | STORE | K00037 | K00037 | K00037 |
| K00041 | K00041 | TIENDA KOAJ CARRERA 62 N | STORE | K00041 | K00041 | K00041 |
| K00042 | K00042 | TIENDA KOAJ CENTRO MAYOR | STORE | K00042 | K00042 | K00042 |
| K00054 | K00054 | TIENDA KOAJ CALIMA | STORE | K00054 | K00054 | K00054 |
| K00056 | K00056 | TIENDA KOAJ DIVER PLAZA | STORE | K00056 | K00056 | K00056 |
| K00060 | K00060 | TIENDA KOAJ CHAPINERO | STORE | K00060 | K00060 | K00060 |

> Las restantes 499 tiendas se consultan en el back office con las credenciales que KOAJ ya tiene asignadas.

### Reglas de autenticación (correo de Ogloba)

> *"El usarname es el mismo store ID de la tienda. En ambiente de pruebas pueden utilizar cualquiera de las tiendas que se encuentran creadas como usuario (store ID) para la autenticación. Todas tienen asignadas la misma contraseña."*

Lo que eso significa para tu APK:

- **Username de Basic Auth = Store ID de la tienda** (ej. `K00036` para Tienda KOAJ Unicentró)
- **Password de Basic Auth**:
  - En testing: `«pedir por el canal de credenciales — no se versiona»` (igual para todas las tiendas)
  - En producción: la asigna Ogloba tienda por tienda, o puede ser la misma para todas (eso lo confirma Roger con Ogloba)
- **El `merchantId` que envías en el body JSON es el mismo valor del username** (ej. `"merchantId": "K00036"`)

### Configuración por tienda vía CloudLicense (recomendado)

Para que ICG pueda asignar el Store ID tienda por tienda sin tocar código, la APK lo recibe como parámetro en el Intent `INITIALIZE`:

```xml
<Configuration>
  <Parameters>
    <Param Key="StoreId">K00036</Param>
    <Param Key="Passphrase">«pedir por el canal de credenciales — no se versiona»</Param>
    <Param Key="BaseUrl">https://co-ts.ogloba.com/gc-restful-gateway/giftCardService</Param>
    <Param Key="ApiVersion">2.18</Param>
  </Parameters>
</Configuration>
```

Cada tienda KOAJ recibe su propio bloque de Parameters desde CloudLicense con su Store ID específico.

---

## 3. Datos de prueba completos (testing)

### Ambiente de pruebas (testing)

- **Base URL**: `https://co-ts.ogloba.com/gc-restful-gateway/giftCardService`
- **Header obligatorio en TODOS los requests**: `X-WSRG-API-Version: 2.18`
- **Auth**: Basic Auth
  - **Username** = Store ID (cualquiera de los 512 de KOAJ, ej. `K00002`)
  - **Password** = `«pedir por el canal de credenciales — no se versiona»`
- **Back office de pruebas**: `https://co-ts.ogloba.com/gcap/`

> **Nota sobre códigos KOAJ-internos** (ej. `91237` que KOAJ maneja como BD de pruebas para Tienda 037 / K00037): son referencias internas del sistema de KOAJ y NO son aceptados por la API de Ogloba. El único identificador que viaja en cada request es el `StoreId` (ej. `K00037`).

### 10 tarjetas físicas de prueba (producto `113817`)

Para pruebas con tarjetas físicas (las que mandó Ogloba en el correo):

```
1138170025515937
1138170025526298
1138170025537105
1138170025549068
1138170025556642
1138170025569488
1138170025570783
1138170025585013
1138170025593603
1138170025606892
```

### Tarjetas digitales (producto `113815`)

Para pruebas con tarjetas digitales: usar el producto `113815` (disponible en el ambiente de testing). El campo del body JSON a usar es `gencode` con el código del producto, **no** `cardNumber`.

### Headers de todos los requests

| Header | Valor | Obligatorio |
|---|---|---|
| `Content-Type` | `application/json` | Sí |
| `X-WSRG-API-Version` | `2.18` | Sí |
| `Authorization` | `Basic <base64(username:password)>` | Sí |
| `Accept-Language` | `es-co` (opcional) | No — EN por defecto |

### Credenciales de demo (NO usar en producción)

El Postman de Ogloba trae estas credenciales de demo (NO son válidas para tiendas reales):

| Campo | Valor demo |
|---|---|
| URL | `https://srl-ts.ogloba.com/gc-restful-gateway/giftCardService` |
| merchantId / Username | `TestOgloba` |
| terminalId | `demo` |
| cashierId | `John Do` |
| passphrase / Password | `«pedir por el canal de credenciales — no se versiona»` |
| API Version | `2.20` (versión actual — en el correo se usa `2.18`) |
| Accept-Language | `FR-fr` (ejemplo) |

> **Para KOAJ en testing**: usá cualquier Store ID de los 512 (ej. `K00002`), con la passphrase común de testing, y el `terminalId` con el ID de la caja POS que tengas asignada.

---

## 4. Arquitectura: tu APK habla con Ogloba por HTTPS

```
┌─────────────────────────────────┐
│  Cloud Ogloba                   │
│  ┌──────────────────────────┐   │
│  │ Ogloba GiftCard API      │   │  ← REST/JSON · Basic Auth · 9 endpoints
│  │ co-ts.ogloba.com/.../    │   │
│  │ giftCardService          │   │
│  └──────────────────────────┘   │
│  ┌──────────────────────────┐   │
│  │ Backoffice Ogloba (gcap) │   │  ← 512 tiendas KOAJ precargadas
│  └──────────────────────────┘   │
└────────────┬────────────────────┘
             │ HTTPS 443
┌────────────▼────────────────────┐
│  Red tienda (LAN / Internet)    │
│  ┌──────────────────────────┐   │
│  │ Router / firewall        │   │
│  └──────────────────────────┘   │
└────────────┬────────────────────┘
┌────────────▼────────────────────┐
│  Caja (tablet Android)          │
│  ┌──────────────────────────┐   │
│  │ HiPosCloud (HPC)         │   │  ← POS de ICG · lanza Intents
│  └──────────┬───────────────┘   │
│             │ Intent             │
│  ┌──────────▼───────────────┐   │
│  │ APK del Módulo de Cobro  │   │  ← .NET MAUI / Xamarin.Android en C#
│  │ (Permoda Pay Oglobal)    │   │     Filtra los 4 Intents + llama a Ogloba
│  └──────────────────────────┘   │
└─────────────────────────────────┘
┌─────────────────────────────────┐
│  Cloud Permoda                  │
│  ┌──────────────────────────┐   │
│  │ CloudLicense             │   │  ← Portal admin HiPOS · registra el módulo
│  └──────────────────────────┘   │
│  ┌──────────────────────────┐   │
│  │ HiOSTORE                 │   │  ← Tienda de apps de ICG · publica la APK
│  └──────────────────────────┘   │
└─────────────────────────────────┘
```

### Los 4 componentes de la solicitud HiOSTORE ↔ carpetas del proyecto

| Componente | Carpeta .NET | Responsabilidad |
|---|---|---|
| Proceso de venta | `HiPos/PaymentActivity.cs` | Recibe los 4 Intents de HiPOS y orquesta el flujo |
| Gestión de respuesta | `HiPos/ResponseMapper.cs` + `HiPos/ReceiptBuilder.cs` | Normaliza la respuesta de Ogloba al formato HiPOS |
| Gestión de token | `Security/TokenStore.cs` | Android Keystore + SQLCipher para credenciales |
| Servicio api | `Network/HttpClientFactory.cs` + `Ogloba/OglobaClient.cs` | Capa HTTP + los 9 endpoints REST |

---

## 5. Los 9 endpoints REST de Ogloba

> **Importante**: el header `X-WSRG-API-Version: 2.18` va en **TODOS** los requests, sin excepción.

### Resumen

| Paso | Método | Path | Para qué |
|---|---|---|---|
| **Step 1** | POST | `/activation` | Activar un bono nuevo |
| **Step 1** | POST | `/redemption` | Redimir saldo de un bono |
| **Step 1** | POST | `/reload` | Recargar saldo a un bono recargable |
| **Step 2** | POST | `/confirmTransaction` | Confirmar la operación del Step 1 |
| **Step 2** | POST | `/cancelTransaction` | Cancelar y liberar el saldo bloqueado |
| **Step 3** | POST | `/reconciliation` | Cierre administrativo (sync de libros) |
| **Error** | POST | `/void` | Anular una transacción ya confirmada (refund comercial) |
| **Error** | POST | `/reversal` | Reversar por timeout/falla técnica |
| **Consulta** | POST | `/balance` | Ver saldo actual y estado de un bono |
| Catálogo | GET | `/getProducts` | Listar productos de bono disponibles |
| Catálogo | GET | `/getBuInfo` | Info del comercio (saldo del BU) |
| Salud | GET | `/test` | Health check |

### Step 1 — Solicitud de operación

#### POST `/activation`

Activar una tarjeta regalo nueva (ecard o física). La operación queda en estado `Requested`.

**Request:**
```json
{
  "merchantId": "K00036",
  "terminalId": "caja-5",
  "cashierId": "operador-123",
  "transactionNumber": "1756113295",
  "amount": 50000,
  "currency": "COP",
  "gencode": "3214567008158",
  "cardNumber": "",
  "note": "Activación caja 5, tienda KOAJ Unicentró",
  "track2Data": ""
}
```

Campos:
- `merchantId` (string, 20, **obligatorio**): el Store ID de la tienda (ej. `K00036`)
- `terminalId` (string, 15, **obligatorio**): el ID de la caja POS
- `cashierId` (string, 20, **obligatorio**): el ID del cajero
- `transactionNumber` (string, 20, **obligatorio**): ID único TUYO, 60 días unique
- `amount` (decimal, 14, **obligatorio**): importe en PESOS completos (`50000` = $50.000 COP). Verificado contra el sandbox — no multiplicar por 100
- `currency` (string, 3, opcional): ISO 4217
- `gencode` (string, 20, **obligatorio en digital**): código del producto
- `cardNumber` (string, 60, **obligatorio en física**): PAN del bono
- `pinCode` (string, 10, opcional): PIN si la tarjeta lo requiere
- `note` (string, 200, opcional): texto libre
- `track2Data` (string, 60, opcional): datos de banda magnética

**Response (success):**
```json
{
  "isSuccessful": true,
  "errorCode": null,
  "errorMessage": null,
  "referenceNumber": "00136544706x",
  "cardNumber": "543486496704423740342",
  "previousBalance": 0,
  "gencode": "3214567008158",
  "amount": 50,
  "balance": 50,
  "note": "",
  "barcodeNumber": "543486496704423740342",
  "eGiftCardUrl": "https://auchan-ts.ogloba.com/eGiftCard/AUCHAN/tEDEbYGBtEkZRhF",
  "cardType": "4",
  "physicalCardNumber": "F01933040242402772493",
  "issuerId": "AUCHAN",
  "mobileNo": "",
  "internalProductCode": "3214568729533",
  "pinCode": "4529",
  "initialBalance": 0,
  "currency": "COP",
  "expireDate": "20260825"
}
```

#### POST `/redemption`

Redimir el saldo de un bono para pagar una venta.

**Request:**
```json
{
  "merchantId": "K00036",
  "terminalId": "caja-5",
  "cashierId": "operador-123",
  "transactionNumber": "1756113296",
  "amount": 50000,
  "currency": "COP",
  "cardNumber": "1138170025515937",
  "pinCode": "",
  "note": "Venta #1234",
  "track2Data": ""
}
```

**Response (success):**
```json
{
  "isSuccessful": true,
  "errorCode": null,
  "errorMessage": null,
  "referenceNumber": "00136544716V",
  "cardNumber": "F09358522316214623609",
  "amount": 50,
  "previousBalance": 50,
  "balance": 0,
  "note": "",
  "cardType": "4",
  "mobileNo": "",
  "initialBalance": 50,
  "currency": "COP",
  "expireDate": "20260825",
  "toBeChargedAmt": 0,
  "redeemShareDet": [
    {
      "sharedAmt": 50,
      "reloadSource": "0"
    }
  ],
  "internalProductCode": "3214568729533",
  "issuerId": "x",
  "requestedCurrency": null,
  "gencode": "3214567008158"
}
```

#### POST `/reload`

Recargar saldo a un bono recargable. La operación queda en estado `Requested` y el saldo pre-cargado hasta el Step 2.

**Request:**
```json
{
  "merchantId": "K00036",
  "terminalId": "caja-5",
  "cashierId": "operador-123",
  "transactionNumber": "1756113297",
  "amount": 50000,
  "currency": "COP",
  "cardNumber": "1138170025515937",
  "note": "Recarga"
}
```

### Step 2 — Confirmación o Cancelación

#### POST `/confirmTransaction`

Confirma la operación del Step 1 → pasa a `Confirmed`, el saldo se mueve permanentemente.

**Request:**
```json
{
  "merchantId": "K00036",
  "terminalId": "caja-5",
  "cashierId": "operador-123",
  "referenceNumber": "00136544716V",
  "transactionNumber": "1756113296",
  "amount": 50000,
  "note": "Confirmado por venta #1234"
}
```

#### POST `/cancelTransaction`

Cancela la operación del Step 1 → libera el saldo bloqueado. Solo se puede cancelar si aún está en `Requested`.

**Request:**
```json
{
  "merchantId": "K00036",
  "terminalId": "caja-5",
  "cashierId": "operador-123",
  "referenceNumber": "00136544716V",
  "transactionNumber": "1756113296",
  "amount": 50000,
  "note": "Cliente se arrepintió"
}
```

### Step 3 — Reconciliación

#### POST `/reconciliation`

Cierre administrativo. Sync de libros entre el sistema del cliente y Ogloba. Admite UNA transacción o un batch de varias.

**Request (una transacción):**
```json
{
  "merchantId": "K00036",
  "terminalId": "caja-5",
  "transactionDetails": [
    {
      "referenceNumber": "00136544716V",
      "transactionType": "P",
      "finalStatus": "Y",
      "amount": 50000,
      "transactionNumber": "1756113296"
    }
  ]
}
```

`transactionType` puede ser:
- `A` = Activation
- `P` = Redemption (Payment)
- `L` = Load/Reload

`finalStatus`:
- `Y` = success (confirmada)
- `N` = failed / aborted

### Flujos de error

#### POST `/void`

Anula una transacción **ya confirmada**. Es la devolución comercial. Necesita el `referenceNumber` original.

**Request:**
```json
{
  "merchantId": "K00036",
  "terminalId": "caja-5",
  "cashierId": "operador-123",
  "referenceNumber": "00136544716V",
  "transactionNumber": "1756113296",
  "amount": 50000,
  "note": "Devolución solicitada por cliente"
}
```

#### POST `/reversal`

Reversión técnica cuando no se recibió respuesta del Step 1 (timeout, error de red, excepción). Deshace cualquier side-effect pendiente. **SIEMPRE** se llama con el mismo `transactionNumber` que se usó en el Step 1.

**Request:**
```json
{
  "merchantId": "K00036",
  "terminalId": "caja-5",
  "cashierId": "operador-123",
  "transactionNumber": "1756113296",
  "referenceNumber": "00136544716V",
  "note": "Reversal por timeout"
}
```

### Consulta

#### POST `/balance`

Consulta el saldo disponible y el estado actual de una tarjeta. **Paso previo recomendado** antes de cualquier redemption.

**Request:**
```json
{
  "merchantId": "K00036",
  "cardNumber": "1138170025515937"
}
```

**Response:**
```json
{
  "isSuccessful": true,
  "errorCode": null,
  "errorMessage": null,
  "cardNumber": "1138170025515937",
  "balance": 50000,
  "currency": "COP",
  "cardStatus": "ACTIVE",
  "expireDate": "20260825"
}
```

### Catálogo

#### GET `/getProducts`

Lista los productos de bono disponibles en la tienda.

```
GET /getProducts
```

Sin body, con headers estándar.

#### GET `/getBuInfo`

Info del Business Unit: nombre, saldo disponible del comercio, neto.

```
GET /getBuInfo
```

#### GET `/test`

Health check. Devuelve `{"isSuccessful": true}`.

```
GET /test
```

---

## 6. El flujo obligatorio de 3 pasos

**Regla de oro**: toda operación con impacto financiero (activación, redención, recarga) tiene que pasar por 3 llamadas en este orden estricto.

### Diagrama

```
       Step 1: Solicitud              Step 2: Confirmar o Cancelar      Step 3: Reconciliación
       ══════════════════             ═════════════════════════════     ════════════════════

  ┌─────────────┐  HTTP POST    ┌─────────────┐   HTTP POST   ┌─────────────┐
  │  Tu APK     │──────────────→│  Ogloba     │──────────────→│  Ogloba     │
  │  .NET MAUI  │  /redemption  │  (Requested)│  /confirm     │  (Confirmed)│
  │             │               │  saldo      │  Transaction  │  saldo      │
  │             │  ←────────────│  bloqueado  │               │  movido     │
  │             │  referenceNo  │             │   ←───────────│             │
  │             │               │             │  isSuccessful │             │
  │             │               │             │               │             │
  │             │               │             │               │             │
  │             │  HTTP POST    │             │               │             │
  │             │──────────────→│             │               │             │
  │             │  /reconcili   │             │               │             │
  │             │  ation        │             │               │             │
  │             │  ←────────────│             │               │             │
  │             │  success      │             │               │             │
  └─────────────┘               └─────────────┘               └─────────────┘
```

### Las 3 fases

#### Step 1 — Solicitud (`Requested`)

Llamas a `/activation`, `/redemption` o `/reload`. Ogloba valida (tarjeta existe, saldo suficiente, etc.) y, si pasa, **pre-autoriza**:

- En **redemption**: bloquea el saldo temporalmente.
- En **activation**: marca la tarjeta como no reactivable hasta cerrar la operación.
- En **reload**: pre-carga el saldo temporalmente.

Te devuelve un `referenceNumber` que **debes guardar** en SQLite local (clave: `transactionNumber`).

#### Step 2 — Confirmar o Cancelar (`Confirmed` / `Cancelled`)

Ya cerraste la venta en la caja → llamas a `/confirmTransaction`. La operación pasa a `Confirmed` y el saldo se mueve **para siempre**.

Si la venta se cae (cliente se arrepiente, error) → llamas a `/cancelTransaction` y liberas el bloqueo.

**Toda Step 1 DEBE terminar en un Step 2**, sin excepciones.

#### Step 3 — Reconciliación

Llamas a `/reconciliation` inmediatamente después del Step 2 (transacción por transacción) o en batch al cierre del día. Sincroniza los libros. También se reconcilian las Step 2 fallidas (`finalStatus=N`).

### Reglas duras

| Regla | Por qué |
|---|---|
| `transactionNumber` debe ser único por 60 días | Es tu propio ID, lo generas tú. Ogloba lo usa para idempotencia. |
| Si no recibes respuesta de Step 1 → llama `/reversal` con el mismo `transactionNumber` | Nunca reintentes Step 1 a ciegas, eso crea duplicados. |
| Una vez confirmado, ya no hay `/reversal`. Solo `/void` | El saldo ya se movió, hay que hacer una devolución comercial. |
| Step 2 debe llegar en un tiempo razonable | El "lifetime" de la pre-autorización suele ser hasta 24h — confirmar ASAP. |

---

## 7. Lo que HiPOS espera de tu módulo (4 acciones)

El "Módulo de Cobro Electrónico" de HiPOS es un APK Android que responde a Intents. Todas las acciones usan el namespace `icg.actions.electronicpayment.<apk_name>.*` donde `<apk_name>` lo asigna ICG en CloudLicense.

### Las 4 acciones que vas a implementar

| Acción | Intent-filter | Cuándo se llama | Qué hacer |
|---|---|---|---|
| `INITIALIZE` | `icg.actions.electronicpayment.<apk_name>.INITIALIZE` | Al arrancar HiPosCloud | Leer el XML `Parameters` y guardar config (Store ID, passphrase, URL, API version) |
| `FINALIZE` | `icg.actions.electronicpayment.<apk_name>.FINALIZE` | Al cerrar HiPosCloud | Cerrar HttpClient, liberar timers y recursos |
| `GET_VERSION` | `icg.actions.electronicpayment.<apk_name>.GET_VERSION` | Para verificar versión instalada vs. cloud | Devolver string de versión de la APK. Único de los 4 que lleva guión bajo en el action string. |
| `TRANSACTION` | `icg.actions.electronicpayment.<apk_name>.TRANSACTION` | El corazón de todo — cuando el cliente paga con bono | Orquestar el flujo de pago y devolver el resultado |

### El `AndroidManifest.xml`

```xml
<manifest xmlns:android="http://schemas.android.com/apk/res/android"
          package="com.koaj.oglobapay">

  <uses-permission android:name="android.permission.INTERNET" />
  <uses-permission android:name="android.permission.ACCESS_NETWORK_STATE" />

  <application android:label="Permoda Pay Oglobal">

    <activity android:name=".HiPos.PaymentActivity"
              android:exported="true">

      <!-- INITIALIZE -->
      <intent-filter>
        <action android:name="icg.actions.electronicpayment.<apk_name>.INITIALIZE" />
        <category android:name="android.intent.category.DEFAULT" />
      </intent-filter>

      <!-- FINALIZE -->
      <intent-filter>
        <action android:name="icg.actions.electronicpayment.<apk_name>.FINALIZE" />
        <category android:name="android.intent.category.DEFAULT" />
      </intent-filter>

      <!-- GET_VERSION -->
      <intent-filter>
        <action android:name="icg.actions.electronicpayment.<apk_name>.GET_VERSION" />
        <category android:name="android.intent.category.DEFAULT" />
      </intent-filter>

      <!-- TRANSACTION -->
      <intent-filter>
        <action android:name="icg.actions.electronicpayment.<apk_name>.TRANSACTION" />
        <category android:name="android.intent.category.DEFAULT" />
      </intent-filter>

    </activity>

  </application>
</manifest>
```

### Inputs del Intent `TRANSACTION`

Cuando el cajero selecciona "Bono Ogloba" en la pantalla de pagos, HiPOS lanza el Intent `TRANSACTION` con estos extras:

| Extra | Tipo | Descripción |
|---|---|---|
| `TransactionType` | string | `SALE` (la más común), `REFUND`, `NEGATIVE_SALE`, `VOID_TRANSACTION`, `QUERY_TRANSACTION`, `BATCH_CLOSE` |
| `TenderType` | string | `CREDIT` (para bonos) / `DEBIT` / `EBT_FOODSTAMP` |
| `Amount` | string | Importe sin puntos ni comas. El contrato de ICG lo describe **en centavos**; nuestro `HiPosRequestMapper` lo reenvía a Ogloba **sin escalar**, y Ogloba trabaja en pesos. Si HiPOS efectivamente manda centavos, una venta por Intent quedaría 100× desfasada — **medir contra un bono de saldo conocido antes de certificar** y, si aplica, escalar en el mapper (nunca en el provider) |
| `TipAmount` | string | Mismo formato (para bonos, probablemente 0) |
| `TaxAmount` | string | Mismo formato |
| `TaxDetail` | string | XML con el desglose de impuestos |
| `SurchargeAmount` | string | Recargo, mismo formato |
| `TransactionId` | string | ID generado por HiPOS (correlacionar logs) |
| `TransactionData` | string | ≤250 chars. **Aquí va el `referenceNumber` de Ogloba** para refunds futuros |
| `ReceiptPrinterColumns` | int | Ancho de la impresora térmica (típicamente 42) |
| `ShopData` | string | XML con info de tienda |
| `SellerData` | string | XML con info de vendedor |
| `DocumentPath` | string | Ruta al XML del documento de venta (si `OnlyUseDocumentPath=true`) |
| `OverPaymentType` | int | `-1`=no, `0`=cambio, `1`=propina, `2`=sobrante, `3`=CASHDRO |
| `LanguageISO` | string | `es`, `en`, etc. |
| `IsAdvancedPayment` | bool | Adelanto de pedido |
| `Token` | string | UUID para auditoría (broadcast) |

### Outputs que tienes que devolver en el `setResult`

| Extra | Tipo | Descripción |
|---|---|---|
| `TransactionResult` | string | `ACCEPTED` / `FAILED` / `UNKNOWN_RESULT` |
| `TransactionType` | string | Eco del input |
| `BatchNumber` | string | (opcional) número de batch |
| `TransactionData` | string | ≤250 chars. Devuelve el `referenceNumber` de Ogloba para futuros refunds |
| `AuthorizationId` | string | `referenceNumber` de Ogloba (HiPOS lo guarda en `Doc__DocGateway`) |
| `CardHolder` | string | Nombre del titular (si Ogloba lo devuelve) |
| `CardType` | string | `OGLOBA` o como lo identifiques |
| `CardNum` | string | PAN ofuscado: `************5937` o `113817******5937` |
| `MerchantReceipt` | string | XML `<Receipt>` con el comprobante para el comercio |
| `CustomerReceipt` | string | XML `<Receipt>` con el comprobante para el cliente |
| `ReceiptFailed` | string | XML `<Receipt>` del comprobante de error (opcional) |
| `Amount` | string | Importe final, en la misma escala en que llegó en el input |
| `TipAmount` | string | Propina (si aplica) |
| `TaxAmount` | string | Impuestos |
| `SurchargeAmount` | string | Sobrecargo |
| `ErrorMessage` | string | Solo si `TransactionResult=FAILED` |
| `ErrorMessageTitle` | string | Solo si `TransactionResult=FAILED` |
| `ModifyDocumentResult` | string | Para inyectar líneas adicionales en el documento |
| `ShopData` | string | XML (eco) |
| `SellerData` | string | XML (eco) |
| `DocumentPath` | string | Ruta al XML del documento modificado |

### El XML `<Receipt>` — el "lenguaje" de impresión de HiPOS

HiPOS no imprime directo; te pide que le devuelvas un XML que él traduce a comandos ESC/POS:

```xml
<?xml version="1.0" encoding="UTF-8"?>
<Receipt numCols="42">
  <ReceiptLine type="TEXT">
    <Formats>
      <Format from="0" to="42">BOLD</Format>
    </Formats>
    <Text>BONO OGLOBA</Text>
  </ReceiptLine>
  <ReceiptLine type="TEXT">
    <Formats>
      <Format from="0" to="42">NORMAL</Format>
    </Formats>
    <Text>Tarjeta: ****5937</Text>
  </ReceiptLine>
  <ReceiptLine type="TEXT">
    <Formats>
      <Format from="0" to="42">NORMAL</Format>
    </Formats>
    <Text>Monto: $500.00 COP</Text>
  </ReceiptLine>
  <ReceiptLine type="CUT_PAPER"/>
</Receipt>
```

**Tipos de línea**: `TEXT`, `CUT_PAPER`, `QR_CODE`, `IMAGE`.
**Formatos**: `NORMAL`, `BOLD`, `UNDERLINE`, `DOUBLE_WIDTH`, `DOUBLE_HEIGHT`.
Para QR, el texto va en `<Text>`. Para imagen, el Base64 PNG/JPG va en `<Text>`.

---

## 8. Mapeo Ogloba ↔ HiPOS

La columna "Estado" refleja lo que hace la APK hoy (`HiPosPaymentOrchestrator`), no lo que
permitiría el contrato.

| HiPOS `TransactionType` | Endpoints Ogloba (en orden) | Estado |
|---|---|---|
| `SALE` | `/balance` (pre-check) → `/redemption` → `/confirmTransaction` → `/reconciliation` | ✅ Implementado |
| `NEGATIVE_SALE` | Mismo camino que `SALE` | ✅ Implementado (mismo despacho) |
| `REFUND` | `/void` (con `referenceNumber` original) → `/reconciliation` | ❌ Responde `FAILED` — KOAJ no maneja devoluciones |
| `VOID_TRANSACTION` | `/cancelTransaction` (si aún en Requested) o `/reversal` (si timeout) | ❌ Responde `FAILED` — KOAJ no maneja cancelaciones |
| `BATCH_CLOSE` | Loop sobre `/reconciliation` con todas las transactions del día | ✅ Responde `ACCEPTED` sin trabajo extra: cada venta ya se concilió individualmente |
| `QUERY_TRANSACTION` | `/balance` | ✅ Implementado (sin `/queryTransactionsHistory`) |
| Activación de bono físico (no estándar TEF) | `/activation` → `/confirmTransaction` → `/reconciliation` | ✅ Implementado, solo desde la UI del módulo |
| Activación de bono virtual con envío por correo | `/orderCreation` → `/orderConfirm` | ✅ Implementado, solo desde la UI del módulo |
| Recarga de bono (no estándar TEF) | `/reload` → `/confirmTransaction` → `/reconciliation` | ⚠️ El adaptador existe (`ReloadAsync`), pero ningún caso de uso ni pantalla lo invoca |

> La reversión técnica (`/reversal` con el mismo `transactionNumber`) sí está implementada y se
> dispara sola cuando el Step 1 queda incierto; no depende de que HiPOS mande `VOID_TRANSACTION`.

---

## 9. Estrategia de implementación en .NET

### Decisión recomendada: .NET MAUI / Xamarin.Android, todo en C#

Una sola APK Android escrita 100% en C# con .NET MAUI o Xamarin.Android. Filtra los 4 Intents de HiPOS directamente y hace las llamadas HTTPS a Ogloba con un `HttpClient` compartido.

**Stack recomendado:**

| Capa | Librería |
|---|---|
| HTTP | `HttpClient` (System.Net.Http) — nativo, sin dependencias |
| JSON | `System.Text.Json` (nativo, rápido) o `Newtonsoft.Json` (más maduro) |
| Configuración | `Microsoft.Extensions.Configuration` |
| Inyección de dependencias | `Microsoft.Extensions.DependencyInjection` |
| Almacenamiento seguro | `Android Keystore` + `SQLCipher` (SQLite cifrado) |
| Logging | `Microsoft.Extensions.Logging` |

### Diseño de carpetas del proyecto .NET MAUI

```
OglobaPay/
├── Platforms/Android/
│   ├── AndroidManifest.xml            // declara los 4 intent-filters
│   └── MainActivity.cs                // entry point Android
├── HiPos/                              // Proceso de venta
│   ├── PaymentActivity.cs             // recibe INITIALIZE/FINALIZE/GET_VERSION/TRANSACTION
│   ├── ReceiptBuilder.cs              // genera los XML <Receipt>
│   └── ResponseMapper.cs              // OglobaResponse → TransactionResult + extras
├── Ogloba/                             // Servicio api
│   ├── OglobaClient.cs                // los 9 endpoints REST
│   ├── OglobaDtos.cs                  // request/response modelos
│   ├── OglobaFlow.cs                  // máquina de 3 pasos
│   └── OglobaErrorMap.cs              // 80+ errorCode → mensaje legible
├── Security/                           // Gestión de token
│   └── TokenStore.cs                  // Android Keystore + SQLCipher
├── Network/
│   └── HttpClientFactory.cs           // pool compartido, retry, logging
├── Persistence/                        // recovery tras crash
│   ├── LocalDb.cs                     // SQLite — tabla PendingTransaction
│   └── PendingTransaction.cs          // step1_ok / step2_pending / step3_pending
└── Audit/
    └── BroadcastAuditor.cs            // icg.actions.externalApi.AUDIT
```

---

## 10. El TransactionLogger — la pieza que el XLSX UAT te obliga a construir

El XLSX de UAT tiene columnas vacías `REQUEST (JSON)` y `RESPONSE (JSON)` que KOAJ tiene que diligenciar. Tu APK **debe** logear cada par request/response y permitir exportarlo a JSON.

```csharp
public class TransactionLogger
{
    public async Task LogAsync(string action, object request, object response)
    {
        var entry = new {
            Timestamp          = DateTime.UtcNow.ToString("o"),
            StoreId            = _config.StoreId,        // ej: K00036
            TerminalId         = _config.TerminalId,     // ej: caja-5
            Action             = action,                 // "redemption", "confirmTransaction", etc.
            ReferenceNumber    = _currentRef,            // si ya hay
            TransactionNumber  = _currentTxn,            // si ya hay
            Request            = request,
            Response           = response,
            HttpStatus         = ...
        };
        await _db.InsertAuditLog(entry);
    }

    // Botón "Exportar log para UAT" — genera JSON agrupado por scenario
    public string ExportForUat(DateTime from, DateTime to)
    {
        return JsonSerializer.Serialize(_db.GetAuditLogs(from, to),
            new JsonSerializerOptions { WriteIndented = true });
    }
}
```

Luego en el XLSX pegas el JSON literal de cada par request/response en su celda. **Esto vale oro para la trazabilidad** — la auditoría ve exactamente lo que mandaste/recibiste.

---

## 11. El OglobaFlow — la máquina de estados de 3 pasos

```csharp
public class OglobaFlow
{
    public async Task<OglobaResult> PayAsync(SaleRequest req)
    {
        // Step 1: Solicitud de operación
        var step1 = await _client.Redemption(...);
        if (!step1.IsSuccessful)
            return OglobaResult.Failed(step1.ErrorMessage);

        // Persistir ANTES del Step 2 — si crashea aquí, recovery al reabrir
        await _db.SavePending(step1.ReferenceNumber, req);

        // Step 2: Confirmar (Requested → Confirmed)
        var step2 = await _client.ConfirmTransaction(step1.ReferenceNumber, ...);
        if (!step2.IsSuccessful) {
            // Dejar en BD como step1_ok_step2_pending para reintento
            return OglobaResult.Unknown(step1.ReferenceNumber);
        }

        // Step 3: Reconciliación (cierre administrativo)
        var step3 = await _client.Reconciliation(step1.ReferenceNumber, ..., "Y");

        // Limpiar BD y devolver resultado
        await _db.DeletePending(step1.ReferenceNumber);
        return OglobaResult.Approved(step1.ReferenceNumber, step1.CardNumber, step1.Balance);
    }
}
```

---

## 12. Secuencia completa de un SALE con bono Ogloba

### Diagrama de secuencia

```
Cajero        HiPosCloud         Tu APK (.NET)        Ogloba
  │                │                    │                 │
  │  1. Totaliza + selecciona "Bono"    │                 │
  │───────────────→│                    │                 │
  │                │  2. Intent: TRANSACTION              │
  │                │     TransactionType=SALE             │
  │                │     Amount=50000 (ver §7)            │
  │                │     TenderType=CREDIT                │
  │                │───────────────────→│                 │
  │                │                    │  3. POST /balance (opcional)
  │                │                    │─────────────────→│
  │                │                    │  ←──────────────│  (verifica saldo)
  │                │                    │                 │
  │                │                    │  4. POST /redemption (Step 1)
  │                │                    │─────────────────→│
  │                │                    │  ←──────────────│  referenceNumber
  │                │                    │                 │
  │                │                    │  5. Persistir en SQLite local
  │                │                    │     step1_ok
  │                │                    │                 │
  │                │                    │  6. POST /confirmTransaction (Step 2)
  │                │                    │─────────────────→│
  │                │                    │  ←──────────────│  isSuccessful=true
  │                │                    │                 │
  │                │                    │  7. POST /reconciliation (Step 3)
  │                │                    │─────────────────→│
  │                │                    │  ←──────────────│  success
  │                │                    │                 │
  │                │                    │  8. setResult(RESULT_OK)
  │                │                    │  TransactionResult=ACCEPTED
  │                │                    │  AuthorizationId=referenceNumber
  │                │                    │  CardNum=****5937
  │                │                    │  MerchantReceipt=<XML>
  │                │                    │  CustomerReceipt=<XML>
  │                │  ←────────────────│                 │
  │                │                    │                 │
  │  9. Imprime vouchers + cierra venta │                 │
  │←───────────────│                    │                 │
```

### Paso a paso

1. **Cajero totaliza** la venta y selecciona "Bono Ogloba" en la pantalla de pagos. Escanea o tipea el PAN del bono.

2. **HiPosCloud** lanza el Intent `TRANSACTION` con `TransactionType=SALE`, `Amount=50000` (escala pendiente de verificar, ver §7), `TenderType=CREDIT`, y los extras de tienda/vendedor.

3. **Tu módulo** (opcional pero recomendado) llama `POST /balance` con el `cardNumber` para verificar saldo y evitar el error 53.

4. **Tu módulo** llama `POST /redemption` con el `cardNumber`, `amount`, `merchantId=K00036`, `terminalId=caja-5`, `cashierId=operador-123`, y un `transactionNumber` único generado por ti (ej. timestamp + random).

5. **Ogloba** responde con `isSuccessful=true` y un `referenceNumber` (ej. `00136544716V`). **Tu módulo lo guarda en SQLite local** junto con `transactionNumber` y el estado `step1_ok`.

6. **Tu módulo** llama `POST /confirmTransaction` con ese `referenceNumber`. Ogloba mueve el saldo definitivamente.

7. **Tu módulo** llama `POST /reconciliation` con `finalStatus=Y`. Cierre administrativo.

8. **Tu módulo** responde a HiPOS con `setResult(RESULT_OK)` + `TransactionResult=ACCEPTED` + `AuthorizationId=referenceNumber` + `CardNum=************5937` + los XML `<Receipt>` para comercio y cliente.

9. **HiPosCloud** imprime los comprobantes y cierra el documento de venta.

---

## 13. Manejo de errores y los 80+ códigos de Ogloba

### Escenarios típicos y cómo reaccionar

| Escenario | Qué hacer |
|---|---|
| **Timeout en Step 1** | Llamar `/reversal` con el mismo `transactionNumber` antes de cualquier otra cosa. Si reversal OK, reintentar Step 1 con `transactionNumber` nuevo. Si reversal falla, NO reintentar. |
| **Saldo insuficiente (error 53)** | Devolver `FAILED` a HiPOS con mensaje claro. No intentar Step 2. |
| **Step 1 OK, Step 2 falla** | Dejar registro "step1_ok_step2_pending" en SQLite. Reintentar en próximo arranque. |
| **UNKNOWN_RESULT en SALE** | Devolver `UNKNOWN_RESULT` a HiPOS. En próximo intento, HiPOS llamará `VOID_TRANSACTION` y tu módulo hace `/reversal` con el `transactionNumber` original. |
| **Card not found (error 52)** | Devolver `FAILED` con mensaje claro al cajero. |
| **Auth fail (401)** | No reintentar. Mostrar error fatal. |
| **PIN incorrecto 3 veces (241)** | No insistir. Pedir al cliente que contacte al emisor. |
| **Demasiadas pendientes (252)** | Esperar unos segundos y reintentar. |

### Los 80+ códigos de error de Ogloba

| Código | Significado | Mensaje sugerido al cajero |
|---|---|---|
| 22 | Unknown product | "Producto no encontrado en el catálogo Ogloba" |
| 23 | Unknown store | "Tienda no registrada en Ogloba" |
| 24 | Invalid card in this store | "Esta tarjeta no es válida para esta tienda" |
| 25 | This card can't be use in this store | "Esta tarjeta no puede usarse en esta tienda" |
| 38 | Store/BU not enough credit | "La tienda no tiene crédito suficiente" |
| 39 | Store/BU not enough credit | "La tienda no tiene crédito suficiente" |
| 50 | Unknown reference or terminal | "Referencia o terminal desconocido" |
| 52 | Unknown card | "Tarjeta no encontrada en el sistema Ogloba" |
| 53 | Card balance not enough | "El bono no tiene saldo suficiente para esta compra" |
| 54 | Activation amount missing | "Falta el monto de activación" |
| 56 | Card expired | "Esta tarjeta está vencida" |
| 57 | Card archived | "Esta tarjeta está archivada" |
| 59 | Invalid cashier | "Cajero inválido" |
| 73 | Invalid amount | "Monto inválido" |
| 81 | Invalid card format | "Formato de tarjeta inválido" |
| 85 | Card already activated | "Esta tarjeta ya fue activada previamente" |
| 86 | Member detail not found | "Detalles del miembro no encontrados" |
| 99 | Suspicion of fraud | "Sospecha de fraude. Contacta al emisor" |
| 104 | The card is inactive | "La tarjeta no está activa. Actívela primero" |
| 105 | The card is blocked | "La tarjeta está bloqueada. Contacta al emisor" |
| 106 | The card is not blocked | "La tarjeta no está bloqueada" |
| 109 | Transaction validated, cancellation impossible | "La transacción ya fue validada, no se puede cancelar" |
| 110 | The card is suspended | "La tarjeta está suspendida" |
| 111 | The card has been refunded | "La tarjeta ya fue reembolsada" |
| 112 | The map has been destroyed | "El mapa fue destruido" |
| 151 | Card not available for sale | "Tarjeta no disponible para venta" |
| 152 | Card not activatable | "Tarjeta no activable" |
| 153 | Card not usable to pay | "Tarjeta no se puede usar para pagar" |
| 154 | Non-reloadable card | "Esta tarjeta no permite recargas" |
| 155 | Mnt max recharge reached | "Monto máximo de recarga alcanzado" |
| 156 | Insufficient amount | "Monto insuficiente" |
| 157 | Amount too large | "Monto demasiado grande" |
| 158 | Invalid amount | "Monto inválido" |
| 159 | Invalid amount | "Monto inválido" |
| 160 | Negative amount rejected | "Monto negativo rechazado" |
| 161 | Card already used | "La tarjeta ya fue usada" |
| 162 | Internal processing error | "Error interno de procesamiento" |
| 206-221 | Internal processing error | "Error interno de procesamiento" |
| 222 | Terminal/cash register inactive | "Terminal/caja inactiva" |
| 226-229 | Internal processing error / Blacklisted card | "Error interno / Tarjeta en lista negra" |
| 230 | Transaction already cancelled | "La transacción ya estaba cancelada" |
| 231 | Incorrect pin code | "PIN incorrecto. Verifica con el cliente" |
| 232 | Card already used, cannot be refunded | "La tarjeta ya fue usada, no se puede reembolsar" |
| 233 | Cancellation failed | "La cancelación falló" |
| 234 | Unauthorized partial payment | "Pago parcial no autorizado" |
| 235 | Negative balance | "Saldo negativo" |
| 236 | The card is not active | "La tarjeta no está activa" |
| 237 | The card is not refundable | "La tarjeta no es reembolsable" |
| 238 | Deadline for cancellation | "Plazo de cancelación vencido" |
| 239 | Incorrect terminal/origin reference | "Referencia de terminal/origen incorrecta" |
| 240 | This card is no longer valid | "Esta tarjeta ya no es válida" |
| 241 | Card blocked, too many attempts | "La tarjeta fue bloqueada por intentos fallidos de PIN" |
| 252 | Too many pending transactions | "Hay demasiadas transacciones pendientes. Intenta en un minuto" |
| 322 | Member blocked | "Miembro bloqueado" |
| 401 | Authorization failure | "Error de credenciales. Contacta a soporte técnico" |
| 998 | Unexpected error | "Error inesperado" |
| 999 | Unexpected error | "Error inesperado" |
| 23005 | Order creation failure | "Error al crear la orden" |
| 23010 | Unknown order no. | "Número de orden desconocido" |
| 23013 | Order approval failure | "Error al aprobar la orden" |
| 23014 | Order return failure | "Error al devolver la orden" |
| 23016 | Order already returned | "La orden ya fue devuelta" |
| 230040 | Cashier Id wrong format | "Formato de ID de cajero incorrecto" |

---

## 14. La plantilla UAT que Ogloba te pide diligenciar (8 escenarios)

El XLSX **no es un checklist libre** — es una planilla con 8 escenarios predefinidos, cada uno con columnas `REQUEST (JSON)`, `RESPONSE (JSON)` y `RESULT` que tienes que llenar con lo que tu APK realmente hace. Se conserva como evidencia de la validación interna.

### Hoja 1: Transaction process — 4 escenarios de flujo normal

#### Escenario 1 — Card activation (ecard o physical card)

1. Llamar `POST /activation` con un `cardNumber` (para física) o un `gencode` (para digital).
2. Llamar `POST /confirmTransaction` con el `referenceNumber` recibido.
3. Llamar `POST /reconciliation` con `finalStatus=Y`.
4. **Esperado**: tarjeta activada con el balance correspondiente, en Ogloba queda como transacción confirmada.

#### Escenario 2 — Card redemption

1. Usar la tarjeta activada en el escenario 1.
2. Llamar `POST /redemption` con el `cardNumber` y el monto.
3. Llamar `POST /confirmTransaction` con el `referenceNumber` recibido.
4. Llamar `POST /reconciliation` con `finalStatus=Y`.
5. **Esperado**: el saldo de la tarjeta baja en la cantidad redimida.

#### Escenario 3 — Void Redemption

1. Sobre la transacción de redemption del escenario 2 (ya confirmada).
2. Llamar `POST /voidTransaction` con el `referenceNumber` de la redemption.
3. **Esperado**: la redemption queda anulada en Ogloba, el saldo de la tarjeta vuelve.

#### Escenario 4 — Void Activation

1. Llamar `POST /activation`.
2. Llamar `POST /confirmTransaction`.
3. Llamar `POST /reconciliation`.
4. Llamar `POST /voidTransaction` sobre la activation confirmada.
5. **Esperado**: la activation queda anulada en Ogloba, la tarjeta vuelve a estado "no activada".

### Hoja 2: Error testing — 4 escenarios de error

#### Escenario 1 — Redemption con timeout del servidor Ogloba

1. Llamar `POST /redemption` con un `gencode: 999999999` (valor que induce timeout en Ogloba).
2. El cliente recibe un timeout.
3. Llamar `POST /reversal` con el mismo `transactionNumber` para deshacer.
4. **Esperado**: Ogloba cancela la transacción, el cliente recibe respuesta exitosa de reversal.

#### Escenario 2 — Client abort después de activation request

1. Llamar `POST /activation`.
2. El cliente aborta la operación (cierra la app, cancela en HiPOS, etc.) **antes** de llamar el Step 2.
3. Llamar `POST /cancelTransaction` con el `referenceNumber` de la activation.
4. **Esperado**: la activation queda cancelada en Ogloba, la tarjeta no se activa.

#### Escenario 3 — Error 85: "Card had been activated, can't be activated again" (para tarjetas físicas)

1. Llamar `POST /activation` con un `cardNumber` (tarjeta ya activada previamente).
2. Llamar `POST /activation` con el **mismo** `cardNumber`.
3. **Esperado**: el segundo intento devuelve `errorCode: 85`.
4. Dos opciones para liberar la tarjeta:
   - **Opción 1**: llamar `POST /cancelTransaction` sobre la primera activation request (la que está en Requested) → la tarjeta se puede volver a activar.
   - **Opción 2**: llamar `POST /confirmTransaction` sobre la primera activation request → la tarjeta queda activada (con el monto de la primera activation), y luego `POST /reconciliation`.

#### Escenario 4 — Error 104: "Inactive card" en redemption

1. Llamar `POST /activation` (queda en Requested, no se confirma).
2. Llamar `POST /redemption` con esa misma tarjeta.
3. **Esperado**: redemption falla con `errorCode: 104` (la tarjeta no está activa).
4. Dos opciones:
   - **Opción 1**: `POST /cancelTransaction` sobre la activation → rehacer el flujo de activation.
   - **Opción 2**: `POST /confirmTransaction` sobre la activation + `POST /reconciliation` → la tarjeta queda activa, y luego repetir `POST /redemption` + `POST /confirmTransaction` + `POST /reconciliation`.

> **Implicación directa para tu APK .NET**: tu módulo **debe** logear cada par (request, response) que intercambia con Ogloba, y debe permitir exportarlo a un formato que se pueda pegar en la columna correspondiente del XLSX. Recomendación: una pantalla de "Admin" en la APK con un botón "Exportar log de transacciones" que genera un JSON por cada referenceNumber, listo para pegar en el UAT.

---

## 15. Compromisos de la solicitud HiOSTORE

La página 5 de la Solicitud de Desarrollo de una API para HiOSTORE enumera los compromisos que Permoda (Roger Moreno) firma al solicitar una API. Condicionan el diseño:

- **App libre de fallos, corregir ASAP** → la capa de persistencia local y el retry de Step 2/3 son obligatorios.
- **Info veraz en HiOSTORE** → capturas, descripciones y features 1:1 con lo que la app hace.
- **Sin contenido ofensivo o material con copyright** → no usar logos de Ogloba/Addi/Nequi sin permiso.
- **Seguridad apropiada** → el TokenStore va cifrado con SQLCipher + Android Keystore, **NO** SharedPreferences en plano.
- **Sin funciones ocultas** → toda telemetría documentada en la descripción de HiOSTORE.
- **No usa material de terceros sin permiso** → no usar marcas registradas sin acuerdo.
- **Funciona con hardware HiPOS homologado** → testear en los modelos específicos que Permoda/KOAJ tiene desplegados.
- **No publicidad ni promoción cruzada** → la APK no puede recomendar "instala también X".
- **No obliga a instalar otras apps** → todas las pasarelas en la misma APK, sin dependencias externas.
- **Cumple GDPR y equivalentes** → el campo `datos_cliente` (documento, teléfono) que el flujo operativo menciona necesita base legal clara.
- **No usa datos de salud** → N/A.
- **Respeta permisos del usuario** → no pedir `READ_CONTACTS` si no se necesita. Auditar cada permiso.
- **Servicios de ubicación solo si son relevantes** → N/A para Ogloba.

---

## 16. Checklist de certificación final

### Configuración inicial

- [ ] Tienes un Store ID válido (cualquiera de los K000XX) y la passphrase de testing confirmada.
- [ ] Las tablets HiPOS pueden resolver `co-ts.ogloba.com` por HTTPS.
- [ ] Probaste `GET /test` con Basic Auth desde la tablet → `{"isSuccessful": true}`.
- [ ] `X-WSRG-API-Version: 2.18` configurado en el cliente HTTP.

### Pruebas funcionales con las 10 tarjetas físicas (producto 113817)

```
1138170025515937
1138170025526298
1138170025537105
1138170025549068
1138170025556642
1138170025569488
1138170025570783
1138170025585013
1138170025593603
1138170025606892
```

- [ ] Balance inquiry en cada una → saldo correcto
- [ ] Redemption parcial: pagar $50.000 de una venta de $80.000 con un bono de $100.000
- [ ] Redemption total: pagar el 100% con bono
- [ ] Reversal forzado (apaga WiFi a mitad de Step 1): saldo no queda bloqueado
- [ ] Reconciliación: verificar que cada venta se concilió individualmente (el `BATCH_CLOSE` de HiPOS no dispara trabajo extra)
- [ ] ~~Cancel dentro del flujo~~ · ~~Void de una transacción ya cerrada~~ — **fuera de alcance**: la APK no implementa `/void` ni `/cancelTransaction` y rechaza los intents `REFUND` / `VOID_TRANSACTION`

### Pruebas con tarjetas digitales (producto 113815)

- [ ] Activar una tarjeta digital con `gencode: 113815`
- [ ] Redemption de tarjeta digital por el `barcodeNumber` o `cardNumber`

### Pruebas de borde

- [ ] Tarjeta con saldo exacto (= importe de la venta)
- [ ] Tarjeta con saldo mayor (overpayment)
- [ ] PIN incorrecto 3 veces → tarjeta bloqueada (error 241)
- [ ] Tarjeta ya canjeada (error 161 / 232)
- [ ] Tarjeta de otro comercio (error 24/25)
- [ ] Pagar con varias tarjetas bono en la misma venta (split payment)
- [ ] WiFi intermitente a mitad de la transacción
- [ ] Reinicio de la tablet a mitad del Step 2 (recuperación de estado)
- [ ] Cerrar la app a la fuerza (kill -9) entre Step 1 y Step 2 → al reabrir debe reintentar y completar

### Impresión de comprobantes

- [ ] Voucher comercio imprime limpio, con todos los datos (PAN ofuscado, monto, ref)
- [ ] Voucher cliente imprime idéntico o con copy clara
- [ ] El comprobante se guarda en el documento de venta

### Auditoría y logs

- [ ] Cada operación logueada con timestamp, `transactionNumber`, `referenceNumber`, monto, resultado
- [ ] Tabla local de transacciones permite reintentos/recovery tras crash
- [ ] Exportación de logs a JSON para diligenciar el XLSX UAT
- [ ] Broadcast `icg.actions.externalApi.AUDIT` sale en cada operación

### UAT final (XLSX de Ogloba)

- [ ] Llenar las 2 hojas del XLSX con los 8 escenarios
- [ ] Pegar el JSON de cada REQUEST y RESPONSE en su celda
- [ ] Marcar RESULT (pass/fail) en cada escenario
- [ ] XLSX diligenciado archivado en `artifacts/` como evidencia de la validación

### Cumplimiento HiOSTORE

- [ ] Las capturas y descripciones de HiOSTORE reflejan lo que la app hace
- [ ] No se usan logos de Ogloba sin permiso
- [ ] Permisos de Android justificados y mínimos
- [ ] Probado en los modelos de tablet HiPOS homologados

---

## 17. Contactos clave

| Persona | Rol | Email | Teléfono |
|---|---|---|---|
| Luis Felipe Quintero Mejía | Solicitante original (KOAJ/Permoda) | `luisfqm@permoda.com.co` | — |
| Roger Moreno | Project lead KOAJ/Permoda, firmante HiOSTORE | `rogerm@permoda.com.co` | +57 312 470 7394 |
| _removed_ | _removed_ | _removed_ | _removed_ |
| Marcus Lin | Soporte Ogloba | `team.support@ogloba.com` | — |
| Maria Andrea | Customer Success Ogloba | `maa@ogloba.com` | — |

---

## 18. Anexo: los 10 correos electrónicos completos

### Correo 1 — Ogloba a "estimado equipo de KOAJ" (sábado 11 abril 2026)

> Estimado equipo de KOAJ,
>
> Adjuntamos la documentación POSTMAN online https://documenter.getpostman.com/view/32475087/2s9YymGPrn#intro que deberan seguir para realizar la integración con su nuevo proveedor POS. La documentación está en inglés, adjunto una breve explicación de los endpoints que deben integrar.
>
> **Web service URL ambiente de pruebas:**
> https://co-ts.ogloba.com/gc-restful-gateway/giftCardService
>
> **Autenticación**
> El sistema utiliza Basic Auth para la autenticación de las peticiones.
> Método: Authorization: Basic.
> Parámetros: Se requiere un Username y un Password.
> Funcionamiento: Estas credenciales se envían en el encabezado de la petición. El cliente debe codificar estas credenciales (en Base64) para que el servidor de Ogloba las valide antes de procesar cualquier transacción.
>
> En ambiente de pruebas pueden utilizar cualquiera de las tiendas que se encuentran creadas como usuario para la autenticación. La contraseña por default es `«pedir por el canal de credenciales — no se versiona»`
>
> **Guía de Integración Flujo Transaccional - Ogloba**
>
> **Transaction - Step 1: Solicitud de Operación**
> En esta etapa se inicia la transacción. La operación queda en estado Requested (Solicitada) y el saldo se bloquea temporalmente, pero no se hace efectivo hasta el siguiente paso.
>
> - Activation: Se utiliza para activar una tarjeta regalo nueva.
> - Redemption: Se utiliza para redimir el saldo de una tarjeta existente.
> - Reload: Se utiliza para recargar saldo en tarjetas que permitan esta función.
>
> **Transaction - Step 2: Confirmación o Cancelación**
> Toda operación iniciada en el Step 1 debe ser finalizada en este paso para cambiar su estado de "Solicitada" a "Confirmada" o "Cancelada".
>
> - Confirm: Confirma la transacción. Al invocarlo, la activación, redención o recarga se hace efectiva y el saldo se actualiza permanentemente.
> - Cancel: Se utiliza para cancelar una transacción que aún está en estado "Requested". Esto libera cualquier bloqueo de saldo generado en el Step 1.
>
> **Transaction - Step 3: Reconciliation (Conciliación)**
> Este es el paso de cierre administrativo. Se utiliza para sincronizar la información entre el sistema del cliente y el sistema de Ogloba. Su objetivo es asegurar que ambas plataformas tengan los mismos registros de transacciones exitosas y evitar discrepancias contables.
>
> **Flujos de Reversión y Anulación (Importante)**
>
> - Transaction - Reversal: Se utiliza ante una falla técnica (ej. timeout). Si el sistema del cliente no recibe respuesta de Ogloba tras enviar una petición de activación, redención o recarga, debe enviar un Reversal para deshacer cualquier cambio pendiente y evitar que el cliente sea afectado por una transacción incompleta.
> - Transaction - Void: Se utiliza para anular una transacción que ya fue confirmada (Step 2 finalizado). Es el equivalente a una devolución comercial, donde se revierte el efecto de una redención o activación previa ya cerrada.
>
> **Consulta de Saldo (Carpeta Other)**
>
> - Balance API: Esta función permite consultar en tiempo real el saldo disponible y el estado actual de una tarjeta. Es un paso previo recomendado antes de cualquier flujo de redención para validar la disponibilidad de fondos o estado de la tarjeta.
>
> Importante: en todos los request deben incluir el header (indicado en la documentación)
>
> `X-WSRG-API-Version: 2.18`
>
> Para realizar transacciones en ambiente de pruebas de tarjetas fisicas, adjunto 10 tarjetas del producto 113817 que pueden utilizar para las pruebas:
>
> ```
> 1138170025515937
> 1138170025526298
> 1138170025537105
> 1138170025549068
> 1138170025556642
> 1138170025569488
> 1138170025570783
> 1138170025585013
> 1138170025593603
> 1138170025606892
> ```
>
> Para pruebas de tarjetas digitales pueden usar el producto 113815 que se encuentra disponible en ambiente de pruebas.
>
> Recuerden que a través de sus usuarios back office pueden consultar tiendas, productos, transacciones, tarjetas, etc. Estos accesos le ayudaran a verificar cualquier información que requieran para realizar la integración.
>
> URL Back Office ambiente de pruebas: https://co-ts.ogloba.com/gcap/
>
> Por último, adjunto encontraran el archivo para las pruebas UAT que se requiere diligenciar para realizar la certificación de esta nueva integración, si tienen alguna duda, estaremos atentos para asistirle en lo que sea necesario.
>
> Saludos
>
> _Contacto removido._
> IT Lead Latam
> _contacto removido_
> Mobile: +57 3124390019

### Correo 2 — Luis Felipe Quintero a Ogloba (martes 14 abril 2026, 8:47 AM)

> Buen día,
>
> Muchas gracias por la documentación, te comento que la revisamos, pero nos hace falta el usuario, tu nos lo puedes facilitar.
>
> Gracias.
>
> Cordialmente.
>
> Luis Felipe Quintero Mejía
> luisfqm@permoda.com.co

### Correo 3 — Ogloba a Luis Felipe (viernes 17 abril 2026, 11:37)

> Estimado Sr. Luis Felipe,
>
> Si se refiere al usuario para la autenticación, el usarname es el mismo store ID de la tienda.
>
> En ambiente de pruebas pueden utilizar cualquiera de las tiendas que se encuentran creadas como usuario (store ID) para la autenticación. Le recomiendo que miren como están autenticandose en producción para que utilicen el mismo esquema, desconozco si por tienda usan un usuario diferente o utilizan un mismo user password para todas las tiendas, de ser asi pueden usar cualquiera de las tiendas que estén creadas en test para la autenticación ya que todas tienen asignadas la misma contraseña.
>
> Si tiene alguna otra duda, nos indica para ayudarle.
>
> Saludos
>
> _Contacto removido._
> IT Lead Latam
> _contacto removido_
> Mobile: +57 3124390019

---

## 19. Anexo: cuerpo completo de los 9 endpoints JSON de Ogloba

### Headers de TODOS los requests

| Header | Valor | Comentario |
|---|---|---|
| `Content-Type` | `application/json` | Siempre |
| `X-WSRG-API-Version` | `2.18` | Confirmado por correo de Ogloba (versión actual del Postman público: 2.20) |
| `Authorization` | `Basic <base64(username:password)>` | Username = Store ID, ej `K00002` |
| `Accept-Language` | `es-co` (opcional) | EN por defecto |

### Ejemplo de cálculo del header Authorization

```python
import base64
username = "K00036"
password = "«pedir por el canal de credenciales — no se versiona»"
credentials = f"{username}:{password}"
encoded = base64.b64encode(credentials.encode()).decode()
print(f"Authorization: Basic {encoded}")
# Output: Basic SzM2MDM2OjNaajhtbEExbk5rODdpdUdRT1dUSGx2cTZ0MjEwNjYxMjBramdzMEhOODlOeDZNcVRW
```

### Request / Response completos de los 9 endpoints

> Los campos de la respuesta marcados con `(opcional)` pueden no estar presentes en todos los casos.

#### 1. POST `/activation`

**Request:**
```json
{
  "merchantId": "K00036",
  "terminalId": "caja-5",
  "cashierId": "operador-123",
  "transactionNumber": "1756113295",
  "amount": 50000,
  "currency": "COP",
  "gencode": "3214567008158",
  "cardNumber": "",
  "pinCode": "",
  "note": "Activación caja 5, tienda KOAJ Unicentró",
  "track2Data": ""
}
```

**Response (success):**
```json
{
  "isSuccessful": true,
  "errorCode": null,
  "errorMessage": null,
  "referenceNumber": "00136544706x",
  "previousBalance": 0,
  "gencode": "3214567008158",
  "amount": 50000,
  "balance": 50000,
  "note": "",
  "barcodeNumber": "543486496704423740342",
  "eGiftCardUrl": "https://auchan-ts.ogloba.com/eGiftCard/AUCHAN/tEDEbYGBtEkZRhF",
  "cardType": "4",
  "physicalCardNumber": "F01933040242402772493",
  "issuerId": "AUCHAN",
  "mobileNo": "",
  "internalProductCode": "3214568729533",
  "pinCode": "4529",
  "initialBalance": 0,
  "currency": "COP",
  "expireDate": "20260825",
  "fileUrl": "",
  "cardNumber": "543486496704423740342"
}
```

#### 2. POST `/redemption`

**Request:**
```json
{
  "merchantId": "K00036",
  "terminalId": "caja-5",
  "cashierId": "operador-123",
  "transactionNumber": "1756113296",
  "amount": 50000,
  "currency": "COP",
  "cardNumber": "1138170025515937",
  "pinCode": "",
  "note": "Venta #1234",
  "track2Data": ""
}
```

**Response (success):**
```json
{
  "isSuccessful": true,
  "errorCode": null,
  "errorMessage": null,
  "referenceNumber": "00136544716V",
  "cardNumber": "F09358522316214623609",
  "amount": 50000,
  "previousBalance": 50000,
  "balance": 0,
  "note": "",
  "cardType": "4",
  "mobileNo": "",
  "initialBalance": 50000,
  "currency": "COP",
  "expireDate": "20260825",
  "toBeChargedAmt": 0,
  "redeemShareDet": [
    {
      "sharedAmt": 50000,
      "reloadSource": "0"
    }
  ],
  "internalProductCode": "3214568729533",
  "issuerId": "x",
  "requestedCurrency": null,
  "gencode": "3214567008158"
}
```

#### 3. POST `/reload`

**Request:**
```json
{
  "merchantId": "K00036",
  "terminalId": "caja-5",
  "cashierId": "operador-123",
  "transactionNumber": "1756113297",
  "amount": 50000,
  "currency": "COP",
  "cardNumber": "1138170025515937",
  "note": "Recarga"
}
```

#### 4. POST `/confirmTransaction`

**Request:**
```json
{
  "merchantId": "K00036",
  "terminalId": "caja-5",
  "cashierId": "operador-123",
  "referenceNumber": "00136544716V",
  "transactionNumber": "1756113296",
  "amount": 50000,
  "note": "Confirmado por venta #1234"
}
```

#### 5. POST `/cancelTransaction`

**Request:**
```json
{
  "merchantId": "K00036",
  "terminalId": "caja-5",
  "cashierId": "operador-123",
  "referenceNumber": "00136544716V",
  "transactionNumber": "1756113296",
  "amount": 50000,
  "note": "Cliente se arrepintió"
}
```

#### 6. POST `/reconciliation`

**Request (una transacción):**
```json
{
  "merchantId": "K00036",
  "terminalId": "caja-5",
  "transactionDetails": [
    {
      "referenceNumber": "00136544716V",
      "transactionType": "P",
      "finalStatus": "Y",
      "amount": 50000,
      "transactionNumber": "1756113296"
    }
  ]
}
```

#### 7. POST `/void`

**Request:**
```json
{
  "merchantId": "K00036",
  "terminalId": "caja-5",
  "cashierId": "operador-123",
  "referenceNumber": "00136544716V",
  "transactionNumber": "1756113296",
  "amount": 50000,
  "note": "Devolución solicitada por cliente"
}
```

#### 8. POST `/reversal`

**Request:**
```json
{
  "merchantId": "K00036",
  "terminalId": "caja-5",
  "cashierId": "operador-123",
  "transactionNumber": "1756113296",
  "referenceNumber": "00136544716V",
  "note": "Reversal por timeout"
}
```

#### 9. POST `/balance`

**Request:**
```json
{
  "merchantId": "K00036",
  "cardNumber": "1138170025515937"
}
```

**Response (success):**
```json
{
  "isSuccessful": true,
  "errorCode": null,
  "errorMessage": null,
  "cardNumber": "1138170025515937",
  "balance": 50000,
  "currency": "COP",
  "cardStatus": "ACTIVE",
  "expireDate": "20260825"
}
```

---

## Resumen ejecutivo (TL;DR)

| Dato | Valor |
|---|---|
| Cliente | **KOAJ** (grupo Permoda, Colombia) |
| Tiendas | **512** con Store IDs `K000XX` |
| POS | **HiPOS** (Android, ICG) |
| Pasarela | **Ogloba** GiftCard |
| API base | `https://co-ts.ogloba.com/gc-restful-gateway/giftCardService` |
| Auth | Basic Auth (Username = Store ID, Password = passphrase) |
| Header obligatorio | `X-WSRG-API-Version: 2.18` |
| Passphrase de testing | `«pedir por el canal de credenciales — no se versiona»` |
| Tarjetas de prueba físicas | 10 (producto `113817`) |
| Tarjetas de prueba digitales | producto `113815` |
| Endpoints | 9 (3 step1, 2 step2, 1 step3, 1 void, 1 reversal, 1 balance) + 3 catálogo |
| Acciones HiPOS | 4: `INITIALIZE`, `FINALIZE`, `GET_VERSION`, `TRANSACTION` (con guión bajo en GET_VERSION) |
| Stack .NET recomendado | .NET MAUI / Xamarin.Android, C#, HttpClient, System.Text.Json |
| Plantilla UAT | XLSX con 8 escenarios (4 proceso + 4 error) |
| Validación interna | Roger Moreno (`rogerm@permoda.com.co`) |
| Project lead | Roger Moreno (rogerm@permoda.com.co) |

---

**Documento generado el 16 de julio de 2026.**
**Sin excluir nada. Listo para usar como referencia técnica del proyecto.**
