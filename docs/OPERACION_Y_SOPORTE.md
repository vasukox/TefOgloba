# Operación y soporte

Runbook de diagnóstico. Cada síntoma está asociado a su causa raíz **medida**, no supuesta.

## 1. Herramientas de diagnóstico

### Etiquetas de log

| Etiqueta | Contenido |
| --- | --- |
| `TefOgloba` | Arranque de la app y marcadores de fase |
| `TefOgloba.Ogloba` | Cada llamada a Ogloba: ruta, tienda, caja, importe, PAN enmascarado, resultado |
| `TefOgloba.HiPos` | Intents recibidos, `apk_name` con el que llegaron, extras y respuesta enviada |
| `AndroidRuntime` | Excepciones no capturadas |
| `ActivityTaskManager` | Enrutamiento de Activities y tareas |

### Comandos habituales

```pwsh
# Log en vivo, solo lo relevante
adb -s <serial> logcat -v threadtime -s TefOgloba TefOgloba.Ogloba TefOgloba.HiPos AndroidRuntime

# Volcado completo a archivo (para adjuntar a un reporte)
adb -s <serial> logcat -d > artifacts/logcat.txt

# Solo errores
adb -s <serial> logcat -d *:E | Select-String AndroidRuntime

# Qué apps responden a una acción del contrato (todas, no solo la primera)
adb -s <serial> shell cmd package query-activities -a icg.actions.electronicpayment.permoglobal.TRANSACTION

# Estado del paquete y versión instalada
adb -s <serial> shell dumpsys package com.permoda.tefogloba | Select-String "versionCode|versionName|firstInstallTime"

# Forzar cierre (para probar recuperación)
adb -s <serial> shell am force-stop com.permoda.tefogloba

# Base de transacciones pendientes (existe, no se puede leer: está cifrada)
adb -s <serial> shell run-as com.permoda.tefogloba ls files/
```

## 2. El módulo no aparece o no se lo llama

| Síntoma | Causa raíz | Verificación / solución |
| --- | --- | --- |
| HiPOS ofrece **instalar el módulo en cada arranque**, y la factura sale sin cobrar | El `apk_name` del alta de CloudLicense no coincide con ninguna acción del manifiesto. Android no resuelve nada, **no hay línea en logcat**: "no nos llamaron" se ve idéntico a "nos llamaron con otro nombre" | Leer el `apk_name` real con `aapt2 dump badging` del APK que distribuye CloudLicense. Ver [BUILD_Y_DESPLIEGUE.md](BUILD_Y_DESPLIEGUE.md#cómo-verificar-el-apk_name-real) |
| Igual al anterior, pero el `apk_name` sí coincide | El código de versión del APK no coincide con el registrado en HioPosCloud: HiPOS da el módulo por desactualizado | Alinear `ApplicationVersion`, `HiPosPaymentOrchestrator.ModuleVersionCode` y el alta en CloudLicense |
| Igual, y ambos coinciden | El extra `Version` de `GET_VERSION` se envió como texto: HiPOS lee `-1` | Debe ir como entero (`PutIntExtra`) |
| **La forma de pago no aparece** en la lista de medios de HiPOS, y ningún `TRANSACTION` llega nunca | Las banderas de `GET_BEHAVIOR` se enviaron como texto `"true"`/`"false"`: HiPOS las lee con `getBooleanExtra`, lanza `ClassCastException` y descarta el módulo **en silencio** | Buscar `ClassCastException` en logcat. Las banderas van como booleanos nativos |
| Igual al anterior, sin excepción en el log | `SupportsCredit` en `false`: un TEF que no declara crédito ni débito no tiene nada que ofrecer | Ver [INTEGRACION_HIPOS.md](INTEGRACION_HIPOS.md#3-banderas-de-get_behavior) |
| Android muestra **"Completar acción usando…"** en mitad de una venta | Dos módulos declaran el mismo `apk_name`. Ocurría al declarar `permoda`, que es de Sistecrédito (`com.pos2pay`) | No declarar `apk_name` ajenos. Comprobar con `query-activities` |
| **HiPOS se queda esperando indefinidamente** y el cajero pierde la venta | Un handler terminó sin devolver resultado | Todo camino debe cerrar con resultado + `Finish()` |

## 3. El módulo arranca mal o se cae

| Síntoma | Causa raíz | Solución |
| --- | --- | --- |
| Crash al arrancar con `UnsatisfiedLinkError: No implementation found for … n_onCreate()`, antes de ejecutar código propio. Desde HiPOS se ve como "el módulo no se puede iniciar" | Compilación **incremental** con *marshal methods*: `libxamarin-app.so` quedó con el registro JNI de la build anterior | Borrar `obj/` y `bin/` y recompilar. `AndroidEnableMarshalMethods` está en `false` para prevenirlo |
| `Arg_NoDefCTor` al abrir la app | El *linker* eliminó el constructor de una vista que se infla por reflexión | `PublishTrimmed=false` y `AndroidLinkMode=None` están así por esto |
| `ObjectDisposedException: ShellToolbarTracker` o `ArgumentException: A resource with the key 'Microsoft.Maui.Controls.Entry' is already present` | El `AppShell` o una página estaban registrados como singleton y se reutilizaban con su handler de plataforma ya desconectado | Páginas y `AppShell` son **transient**; los ViewModels, singleton |
| La app "a veces no levanta" y el cajero no ve nada | El crash ocurre en `OnCreate`: el proceso muere antes de dibujar | Buscar la excepción en `AndroidRuntime` — el síntoma no distingue causas |
| **Pantalla en blanco** con el nombre del módulo encima antes de llegar al TEF | El tema de arranque no estaba declarado en el manifiesto (el atributo `[Activity(Theme=…)]` del C# **no gana** sobre la declaración manual) | El tema va en `AndroidManifest.xml`: `Ogloba.Launch` para la app y `Ogloba.Invisible` para las Activities de intents |
| El comprobante deja de imprimirse, u `ObjectDisposedException` a mitad del cobro, y nadie lo relaciona con nada | Android **recreó la Activity** por un cambio de configuración (abrir o cerrar el teclado en cada escaneo manual) y el estado en memoria se perdió | La lista de `configChanges` del manifiesto cubre orientación, teclado, idioma, densidad y escala de fuente. No recortarla |
| `IllegalArgumentException: end should be < than charSequence length` al teclear un importe | El `TextWatcher` de emoji2 procesaba el texto con la longitud anterior mientras el separador de miles lo reescribía | El procesamiento de emoji está desactivado en todos los `Entry` |
| "Volver a HiPOS" devuelve al **launcher** en lugar de al POS | Se respondió `RESULT_OK` donde correspondía `RESULT_CANCELED`, o se cerró la tarea en lugar de mandarla al fondo | `RESULT_CANCELED` = "el cajero salió"; la tarea del módulo se manda al fondo con `MoveTaskToBack` |

## 4. El cobro no se refleja bien en el POS

| Síntoma | Causa raíz | Solución |
| --- | --- | --- |
| "Error de módulo externo" y HiPOS **relanza el intent** | No se hizo eco del `TransactionType` que mandó HiPOS | Devolver el tipo literal recibido |
| **No sale nada por la impresora** | Falta el extra `CustomerReceipt` | Se envía siempre, incluso cuando la operación falla |
| Salen **dos tirillas** y las dos dicen "COPIA COMERCIO" | Se enviaban `MerchantReceipt` y `CustomerReceipt` con el mismo contenido | Un solo comprobante, el del cliente |
| El medio de pago del documento queda **sin identificar** y el módulo fiscal falla | Se enviaba el número de línea pelado, o `ModifyDocumentResult` solo cuando HiPOS mandaba `PaymentMeanLineNumber` (que no manda) | `ModifyDocumentResult` va **siempre** que la operación se acepte, como XML completo |
| "La forma de pago tarjeta de crédito no tiene equivalencia" | Se puso un `PaymentMeanId` por defecto tomado de otro módulo: HiPOS aplicó el pago sobre **ese** medio, que no tiene equivalencia DIAN configurada | Nunca inventar el id del medio de pago. Ver [DECISIONES.md](DECISIONES.md#d-05) |
| **"No cuenta con folios asociados"** (código 109) al facturar | **No es del módulo.** Es respuesta del backend fiscal (`hioposreports-co`): la tienda se quedó sin rango de folios DIAN | Escalar a quien administra la resolución DIAN de la tienda |
| Se aceptan abonos "en silencio" sin mover saldo | Una sola de las dos puertas de deshacer estaba cerrada, y un cambio de configuración del POS hizo que los abonos entraran por la otra | `REFUND` y `VOID_TRANSACTION` se rechazan **los dos**. Ver [DECISIONES.md](DECISIONES.md#d-04) |
| La **papelera no aparece** sobre la línea de pago del bono | Falta `CallOnTotalizationCanceled=true` en `GET_BEHAVIOR` | Es la única bandera que lo desbloquea. Confirmada por ICG el 2026-09-01, no está en la revisión 3.8 del PDF |

## 5. Ogloba rechaza la operación

| Síntoma | Causa raíz | Solución |
| --- | --- | --- |
| `errorCode 73` "Monto de activación no válido" | El importe se envió multiplicado por 100, o está fuera del rango del producto | Ogloba recibe **pesos enteros**. Verificar también el rango del catálogo ([FLUJOS_DE_OPERACION.md](FLUJOS_DE_OPERACION.md#1-unidades-monetarias)) |
| `cashier_id.required` al escanear el bono | No había cajero en turno y tampoco último cajero registrado en esa caja | `PosSession.OperatingCashierId` cubre el caso; si falla, dar de alta un cajero en *Configuración* |
| `errorCode 230040` "formato de cajero inválido" | El identificador del cajero no está registrado o no cumple el formato | Registrar el cajero en la tienda |
| `errorCode 23` "Tienda desconocida" **solo en activación virtual**, mientras el resto de endpoints funciona con la misma tienda | Era el **producto**, no la tienda: el `itemCode` B2B rechazaba el flujo de Order management | Usar el código de producto del ambiente ([AMBIENTES.md](AMBIENTES.md)) |
| Mensajes de error en inglés | Falta el header `Accept-Language: es-co` | Viaja en todas las llamadas; Ogloba devuelve `errorMessage` ya traducido |
| Timeouts frecuentes en consultas | El tope de solo lectura es 15 s, a propósito | Revisar la red de la tienda. **No subir** el tope de las operaciones que mueven dinero (90 s) |

## 6. Estado incierto: qué hacer

Cuando una operación termina en `UNKNOWN_RESULT` o el cajero reporta "no sé si se cobró":

1. **No reintentar a ciegas.** Puede estar aplicada del lado de Ogloba.
2. Consultar el saldo del bono desde *Consultar saldo*: si bajó, la operación se aplicó.
3. Buscar la referencia en `logcat` (`TefOgloba.Ogloba`) o en *Bitácora*.
4. Reabrir el módulo: la recuperación al arranque retoma lo que quedó pendiente y lo cierra o lo
   reversa ([FLUJOS_DE_OPERACION.md](FLUJOS_DE_OPERACION.md#5-recuperación-de-pagos-pendientes)).
5. Si el saldo bajó y la venta no se facturó, escalar con la referencia: **no hay flujo automático de
   devolución de saldo**.

### Caso conocido que exige seguimiento manual

Si se canceló una totalización que ya tenía un cobro con bono, el POS suelta la línea pero **el
saldo no regresa**. En el log queda una advertencia con la referencia y el importe. Ese es el rastro
con el que se cuadra después. Ver
[INTEGRACION_HIPOS.md](INTEGRACION_HIPOS.md#6-totalization_canceled).

## 7. Reconfigurar una caja

| Situación | Procedimiento |
| --- | --- |
| Cambió la contraseña de Ogloba | *Configuración* → nueva contraseña → guardar. No requiere reinstalar |
| Cambió la tienda o la caja | *Configuración*. El nombre del comercio se refresca desde `/getBuInfo` |
| Se perdió el PIN de administrador | No se puede recuperar: hay que **desinstalar y reinstalar**, lo que borra la configuración de la caja |
| Un cajero se va o cambia de tienda | *Usuarios* → **desactivar** (conserva su contraseña y su historial). Borrarlo pierde el historial |
| Actualización rechazada por firma distinta | Desinstalar y reinstalar. Se pierde la configuración de la caja ([BUILD_Y_DESPLIEGUE.md](BUILD_Y_DESPLIEGUE.md#cuando-la-actualización-se-rechaza)) |

## 8. Qué adjuntar en un reporte de incidente

1. Serial de la tablet, tienda y caja.
2. Ambiente y código de versión de la APK instalada (`dumpsys package`).
3. `logcat` completo del episodio (`adb logcat -d > logcat.txt`), sin filtrar.
4. Hora exacta del incidente y qué estaba haciendo el cajero.
5. Serial del bono **enmascarado** y referencia de la operación, si existen.
6. Foto de la pantalla del POS con el mensaje, si hubo mensaje.

> Un `logcat` filtrado por una sola etiqueta suele ocultar la causa: los fallos de este módulo se
> manifiestan en capas distintas (`AndroidRuntime`, `ActivityTaskManager`, el propio HiPOS). Volcar
> todo.
