# Integración con HiPOS (contrato de ICG)

Cómo HiPOS invoca al módulo y qué se le responde. Implementa el contrato *API de Desarrollo de un
Módulo de Cobro Electrónico 4.0* de ICG.

Código de referencia:
[`HiPosPaymentActivity`](../src/Permoda.Pay.Maui/Platforms/Android/HiPos/HiPosPaymentActivity.cs) ·
[`HiPosPaymentOrchestrator`](../src/Permoda.Pay.Maui.HiPos/HiPos/HiPosPaymentOrchestrator.cs) ·
[`HiPosRequestMapper`](../src/Permoda.Pay.Maui.HiPos/HiPos/Requests/HiPosRequestMapper.cs) ·
[`AndroidManifest.xml`](../src/Permoda.Pay.Maui/Platforms/Android/AndroidManifest.xml).

## 1. Cómo encuentra HiPOS al módulo

HiPOS **no busca la app por su package**. Construye un `Intent` implícito cuya acción tiene la
forma:

```
icg.actions.electronicpayment.{apk_name}.{OPERACIÓN}
```

y le pregunta a Android si alguien la atiende. La llave de enrutamiento es el `apk_name` que **ICG
registró en CloudLicense**, no el package, ni la firma, ni el nombre del archivo APK.

| Concepto | Valor | Quién lo controla |
| --- | --- | --- |
| `apk_name` (enrutamiento) | `permoglobal` | ICG / CloudLicense |
| `ApplicationId` (instalación) | `com.permoda.tefogloba` | Permoda |
| `ApplicationVersion` (código de versión) | `1` | Debe coincidir con el alta en CloudLicense |
| Nombre que ve el cajero en el POS | `Ogloba` | Lo responde el módulo en `GET_CUSTOM_PARAMS` |

Si el `apk_name` no coincide, **el fallo no deja rastro**: Android no resuelve nada, no hay línea
en `logcat`, y para HiPOS es indistinguible de "el módulo no está instalado" — ofrece instalarlo
en cada arranque y la factura sale sin cobrar. El valor correcto no se adivina: se lee con `aapt2`
del APK que CloudLicense distribuye. Ver [DECISIONES.md](DECISIONES.md#d-01).

El manifiesto declara además cuatro `apk_name` candidatos (`oglobapay`, `Permoda Pay Oglobal`,
`PermodaPayOglobal`, `ogloba`) como red de seguridad ante un cambio de alta. **`permoda` no se
declara a propósito**: pertenece al módulo de Sistecrédito (`com.pos2pay`) y declararlo hacía que
Android mostrara el selector "Completar acción usando…" en mitad de una venta.

El módulo despacha por la **operación** (el último segmento de la acción), no por el `apk_name`,
así que cualquiera de los nombres declarados funciona. Cada intent registra en `logcat` con qué
`apk_name` llegó.

## 2. Operaciones soportadas

Once acciones en el namespace `icg.actions.electronicpayment.*`, más una en
`icg.actions.document.*`:

| Operación | Qué hace el módulo | Respuesta |
| --- | --- | --- |
| `INITIALIZE` | Parsea el XML `Parameters` (`StoreId`, `Passphrase`, `BaseUrl`, `ApiVersion`) y **persiste la configuración de la tienda**. El `TerminalId` no viaja en este intent; se conserva el ya configurado. | `RESULT_OK` + `INITIALIZED` |
| `FINALIZE` | Cierra la sesión con el POS. | `RESULT_OK` + `FINALIZED` |
| `GET_VERSION` | Devuelve el código de versión del módulo. | `RESULT_OK` + extra `Version` **entero** |
| `GET_BEHAVIOR` | Declara las capacidades del módulo (§3). | `RESULT_OK` + banderas **booleanas** |
| `GET_CUSTOM_PARAMS` | Nombre (`Ogloba`) y logo PNG del botón de medio de pago. | `RESULT_OK` + `Name` + `Logo` (`byte[]`) |
| `GET_PRINT_INFO` | Sin información adicional: los comprobantes ya viajan en la respuesta de `TRANSACTION`. | `RESULT_OK` |
| `SHOW_SETUP_SCREEN` | Sin pantalla que abrir: la configuración llega por `INITIALIZE` o se hace en la UI del módulo. | `RESULT_OK` |
| `TRANSACTION` | La operación de cobro. Detalle en §4. | Según el tipo |
| `READ_CARD` | No soportada: el módulo no opera tarjetas bancarias. | `RESULT_CANCELED` |
| `CHARGE_CARD` | No soportada. | `RESULT_CANCELED` |
| `GET_CARD_DATA` | No soportada. | `RESULT_CANCELED` |
| `TOTALIZATION_CANCELED` (namespace `icg.actions.document.*`) | El cajero canceló una totalización que ya tenía un cobro con bono. Detalle en §6. | `RESULT_OK` |

Tres reglas duras del contrato, todas aprendidas en terminal:

* **Siempre hay que responder.** Un handler que termina sin devolver resultado deja a HiPOS
  esperando indefinidamente y el cajero pierde la venta.
* **El tipo del extra importa.** `Version` debe ir como `int`: enviado como `String`, HiPOS lee
  `-1` y pide reinstalar el módulo en cada arranque. Las banderas de `GET_BEHAVIOR` deben ir como
  `boolean`: enviadas como `"true"`/`"false"` producen `ClassCastException` en HiPOS, que descarta
  el módulo en silencio — la forma de pago no aparece y ningún `TRANSACTION` llega nunca.
* **`RESULT_CANCELED` y `RESULT_OK + FAILED` no son lo mismo.** `RESULT_CANCELED` significa "el
  cajero salió, vuelve a tu pantalla de medios de pago"; `RESULT_OK` con `TransactionResult=FAILED`
  significa "el módulo falló, aborta el flujo". Confundirlos manda al cajero al launcher.

`TOTALIZATION_CANCELED` vive en una Activity aparte
([`TotalizationCanceledActivity`](../src/Permoda.Pay.Maui/Platforms/Android/HiPos/TotalizationCanceledActivity.cs))
porque su namespace es distinto y la Activity de pagos despacha solo acciones de
`electronicpayment`.

## 3. Banderas de `GET_BEHAVIOR`

Lo que el módulo declara que sabe hacer. Cada valor está elegido, no heredado:

| Bandera | Valor | Por qué |
| --- | --- | --- |
| `SupportsCredit` | `true` | **Es la bandera que habilita la forma de pago.** Un TEF que no declara crédito ni débito no tiene nada que ofrecer y no aparece en la lista. |
| `HasCustomParams` | `true` | Hace que HiPOS pida el nombre y el logo del botón por `GET_CUSTOM_PARAMS`. |
| `canAudit` | `true` | Literal del contrato, con minúscula inicial mientras el resto es PascalCase. |
| `SupportsBatchClose` | `true` | El cierre de caja se acepta sin trabajo extra: cada venta ya se concilió individualmente. |
| `SupportsNegativeSales` | `true` | "Entrada de caja" en HiPOS llega como `NEGATIVE_SALE`. |
| `SupportsTransactionVoid` | `true` | Habilita que HiPOS pida anular un cobro que terminó en `UNKNOWN_RESULT`. En `false`, el POS reintentaba sobre una transacción de estado desconocido. |
| `CallOnTotalizationCanceled` | `true` | **Es lo que desbloquea la papelera sobre la línea de pago del bono.** Confirmado por ICG el 2026-09-01; no aparece en la revisión 3.8 del PDF. |
| `SupportsTransactionQuery` | `false` | `QUERY_TRANSACTION` en el contrato significa "recuperar una venta que terminó en `UNKNOWN_RESULT`", no "consultar saldo". Declararlo en `true` haría que HiPOS diera por cobradas ventas inciertas. Ver §5. |
| `ExecuteVoidWhenAvailable` | `false` | En `true`, HiPOS manda `VOID_TRANSACTION` en lugar de `REFUND` para abonos del terminal y la Z actuales. Se probó y se revirtió: rompió el rechazo de notas de crédito. Ver [DECISIONES.md](DECISIONES.md#d-04). |
| `SupportsPartialRefund` | `false` | Reintegrar parte de una venta cerrada es otra operación y KOAJ no la maneja. |
| `CanPrint` | `false` | Imprime HiPOS, con el XML `<Receipt>` que devuelve el módulo. |
| `OnlyUseDocumentPath` | `false` | En `true`, HiPOS escribe el XML de la venta a disco y pasa la ruta; el *scoped storage* de Android 13+ impide leerla y el documento llega nulo. |
| `SupportsDebit`, `SupportsEBTFoodstamp`, `SupportsTipAdjustment`, `SaveLoyaltyCardNum`, `ReadCardFromApi` | `false` | No aplican a un medio de pago de bonos regalo. |

`SupportsTransactionQuery` y `SupportsTransactionVoid` son excluyentes según el contrato (si van
las dos, el query se ignora). Con query en `false` no hay conflicto.

## 4. El intent `TRANSACTION`

### Entradas que se leen

| Extra | Uso |
| --- | --- |
| `TransactionType` | Obligatorio. Enruta la operación. Se conserva el texto literal para devolverlo como eco. |
| `MerchantId` | Tienda. Si no llega, se usa la de la configuración persistida. |
| `TerminalId` | Caja. **HiPOS no lo envía en este intent**; se resuelve desde la configuración de la terminal. |
| `CashierId` | Cajero. **HiPOS no lo envía en este intent**; se resuelve desde la sesión de caja. |
| `Amount` | Importe **multiplicado por 100** (los dos últimos dígitos son decimales). El mapper lo divide entre 100 antes de pasarlo a Ogloba. Ver [FLUJOS_DE_OPERACION.md](FLUJOS_DE_OPERACION.md#unidades-monetarias). |
| `Currency` | Moneda; por defecto `COP`. |
| `TenderType` | Diagnóstico y ruteo de "entrada de caja" (§4.1). |
| `TransactionData` | Referencia de la operación original. Solo trazabilidad. |
| `CardNumber` | **Nunca llega**: el contrato no incluye el serial del bono entre los campos de entrada de un `SALE`. El módulo lo captura en su propia pantalla. Si algún día llegara, se respeta y se cobra sin abrir pantalla. |
| `PaymentMeanId` / `FixedPaymentMeanId` | Medio de pago del documento. Casi nunca llega: cuando HiPOS lanza el intent, la lista `PaymentMeans` del documento aún está vacía. |
| `PaymentMeanLineNumber` | Línea del medio de pago; por defecto `1`. |
| `TransactionId` | Respaldo del `AuthorizationId` cuando Ogloba no devolvió referencia. |
| `IsAdvancedPayment` | Informativo. |

Los requisitos de validación dependen del tipo: `BATCH_CLOSE` y `QUERY_TRANSACTION` no exigen
importe; ningún tipo exige serial ni referencia.

### 4.1 Enrutamiento por tipo

| `TransactionType` | Condición | Qué hace el módulo | Respuesta |
| --- | --- | --- | --- |
| `SALE` / `NEGATIVE_SALE` | Con `TenderType` declarado | Abre la pantalla del módulo, captura el bono y **redime** (`ProcessSaleHandler`) | `ACCEPTED` · `UNKNOWN_RESULT` · `FAILED` |
| `SALE` / `NEGATIVE_SALE` | Sin `TenderType` (entrada de caja) | No opera. Indica al cajero que los bonos se activan desde el módulo | `FAILED` con motivo |
| `REFUND` | — | Rechaza: los bonos no admiten notas de crédito | `FAILED` con título y motivo |
| `VOID_TRANSACTION` | — | Rechaza: los bonos no se anulan desde el POS | `FAILED` con título y motivo |
| `BATCH_CLOSE` | — | Acepta sin trabajo: la conciliación ya corrió por transacción | `ACCEPTED` |
| `QUERY_TRANSACTION` | — | Consulta `/balance` y devuelve el saldo (§5) | `ACCEPTED` / `FAILED` |
| Cualquier otro | — | `hipos.transaction.unsupported` | `FAILED` |

Dos precisiones que no son obvias:

* **Lo que distingue una venta de una entrada de caja es el `TenderType`, no el
  `TransactionType`.** Medido en terminal el 2026-08-26: las dos llegan como `SALE` con
  `IsAdvancedPayment=false`; la venta trae `TenderType=CREDIT` y la entrada de caja lo trae vacío.
* **La activación de bonos ya no entra por HiPOS.** Es un flujo manual con su propia autenticación
  de cajero (usuario y contraseña), y meterlo dentro de un intent significaría activar sin esa
  autenticación. El cajero cobra en el POS por el medio que corresponda y luego entra al módulo a
  activar.

### 4.2 Respuesta de un cobro

Cinco elementos que hacen que HiPOS cierre el documento e imprima. Los cinco costaron errores en
terminal:

1. **Eco del `TransactionType` que mandó HiPOS.** Si se responde un tipo distinto, el POS no da la
   operación por cerrada y relanza el intent — el síntoma era "error de módulo externo".
2. **`CustomerReceipt` siempre.** Es lo que HiPOS imprime. Se envía **un solo** comprobante, el del
   cliente: enviar también `MerchantReceipt` sacaba dos tirillas y las dos decían "COPIA COMERCIO".
   El respaldo del comercio es la factura que imprime HiPOS.
3. **`ModifyDocumentResult` como XML, siempre, con `PaymentMeanId` no vacío.** Enriquece el medio de
   pago del documento con el `AuthorizationId`. Enviar el número de línea pelado no sirve: HiPOS no
   lo parsea.
4. **Nada de numeración fiscal.** El consecutivo, el rango y la resolución DIAN son de HiPOS y de
   `icg.hioposapifiscal`. El único campo que viaja al fiscal es `AuthorizationId`, saneado según el
   manual de ICG ([`DianFieldSanitizer`](../src/Permoda.Pay.Maui.HiPos/HiPos/Results/DianFieldSanitizer.cs)).
5. **El extra `Amount` lleva lo que realmente se aplicó**, que puede ser menos que lo pedido si el
   bono no alcanzaba (§4.3).

Extras de salida:
`TransactionResult` · `TransactionType` · `AuthorizationId` · `TransactionData` · `CardNum` ·
`CardHolder` · `CardType` (`GIFTCARD`) · `CustomerReceipt` · `Amount` · `ModifyDocumentResult` ·
`ErrorMessage` y `ErrorMessageTitle` cuando falla.

> Los rechazos se responden con `TransactionResult=FAILED`, **no** con un extra `Result`.
> `Result` no es un campo de salida del contrato: con ese nombre HiPOS no lee la transacción como
> fallida y el cajero no ve ningún aviso — la operación no pasa y nadie dice por qué.

### 4.3 Pago parcial

Si el saldo del bono no cubre la factura completa, el módulo aplica lo que hay y lo declara con el
par `FixedPaymentMeanId` + `FixedPaymentMeanAmount`. Con eso el POS baja el medio Ogloba a lo que
el bono cubrió y le pide el resto al cajero por otro medio, en lugar de rechazar el cobro entero.
El par solo se envía si HiPOS indicó **cuál** medio corregir: inventar un id ajeno rompe la
equivalencia DIAN. Ver [DECISIONES.md](DECISIONES.md#d-05).

## 5. `QUERY_TRANSACTION`

Implementado como consulta de saldo: llama a `/balance` y devuelve el saldo formateado en
`AuthorizationId`, con un comprobante de una línea. **No inicia ninguna venta** — no llama a
`/redemption`, `/confirmTransaction` ni `/reconciliation`.

Pero `SupportsTransactionQuery` está en `false`, así que **HiPOS no envía este intent en
producción**. La razón es semántica: para el contrato, `QUERY_TRANSACTION` es "recuperar una venta
que terminó en `UNKNOWN_RESULT`", y responder `ACCEPTED` haría que el POS diera esa venta por
cobrada. El handler queda disponible para pruebas manuales por ADB; se habilitará cuando implemente
la semántica real de recuperación.

## 6. `TOTALIZATION_CANCELED`

Llega en **toda** totalización cancelada, se haya pagado con bono o no. Es el intent que permite
soltar la línea de pago del bono cuando, por ejemplo, se facturó y la DIAN no integró.

Comportamiento actual:

1. Lee el documento de venta que manda HiPOS y busca la línea de pago del bono
   ([`TotalizationDocumentReader`](../src/Permoda.Pay.Maui.HiPos/HiPos/Requests/TotalizationDocumentReader.cs)).
2. Si no hay pago con bono, responde `OK` y no hace nada más.
3. Si hay pago con bono, **responde `OK` sin devolver el saldo en Ogloba**, y deja en `logcat` una
   advertencia con la referencia y el importe.

> **Consecuencia operativa que hay que conocer**: el POS suelta la línea, pero el saldo del bono
> **no** regresa. El rastro para cuadrarlo después queda en el log, con la referencia y el importe.
> Devolver el saldo requeriría `/voidTransaction` — el adaptador existe pero ningún caso de uso lo
> invoca. Ver [ESTADO.md](ESTADO.md).

Nunca se responde error por no haber encontrado nada que deshacer: eso trabaría la caja en el caso
mayoritario, en el que no hubo bono.

## 7. Seguridad del canal de intents

* La Activity **valida el paquete llamante** antes de procesar: solo `icg.android.start` y
  `com.icg.hiopos` (en `Debug` también `com.android.shell`, para poder probar con `adb shell am
  start`). Un APK malicioso no puede suplantar al POS para extraer datos de recibos ni disparar
  cobros.
* El manifiesto declara esos paquetes en `<queries>`: sin eso, en Android 11+ `PackageManager`
  no puede resolver `getCallingPackage()` y la validación no tendría con qué comparar.
* Cada respuesta se replica como broadcast `icg.actions.externalApi.AUDIT` (mejor esfuerzo). El
  esquema exacto de extras **no está especificado** en el contrato — es un ítem de checklist
  HiOSTORE. Se reenvía el mismo resultado y extras más un timestamp. Pendiente de confirmar con
  ICG.

## 8. Comprobantes

El módulo devuelve el XML que HiPOS traduce a comandos ESC/POS:

```xml
<Receipt numCols="42">
  <ReceiptLine type="TEXT">…</ReceiptLine>
  <ReceiptLine type="QR_CODE">…</ReceiptLine>
  <ReceiptLine type="CUT_PAPER"/>
</Receipt>
```

42 columnas, el estándar de la térmica de HiPOS. Reglas de contenido:

* **El PAN va enmascarado** en todo lo visible… **salvo el serial de un bono recién activado**, que
  va completo: en un bono virtual es la única prueba física que se lleva el cliente para poder
  redimirlo, y no hay tarjeta plástica de respaldo.
* El QR del `eGiftCardUrl`, cuando Ogloba lo devuelve, va solo en la copia del cliente.
* Se emite comprobante **también cuando la operación falla**: el cliente se lleva constancia de que
  el cobro se intentó y de por qué no pasó.
* Sin marcas de terceros (ver [SEGURIDAD.md](SEGURIDAD.md#cumplimiento-hiostore)).

## 9. Configuración por `INITIALIZE`

El XML `Parameters` es el mecanismo por el que las 512 tiendas se configuran sin intervención
manual:

```xml
<Configuration>
  <StoreId>K00037</StoreId>
  <Passphrase>…</Passphrase>
  <BaseUrl>https://co-ts.ogloba.com/gc-restful-gateway/giftCardService</BaseUrl>
  <ApiVersion>2.18</ApiVersion>
</Configuration>
```

Solo `StoreId`, `BaseUrl` y `ApiVersion` son obligatorios. La `Passphrase` se persiste cifrada en
`SecureStorage`. El `TerminalId` **no viaja en este intent**: se conserva el que ya tuviera
configurado la caja. Un XML mal formado responde `FAILED` con motivo, no un crash.
