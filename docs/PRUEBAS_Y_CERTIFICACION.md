# Pruebas y certificación

Dos niveles: las pruebas automatizadas que corren en cada build, y los escenarios manuales que se
ejecutan en una terminal real para certificar el módulo ante Ogloba e ICG.

## 1. Pruebas automatizadas

```pwsh
dotnet test Permoda.Pay.sln -c Debug
```

Estado verificado el 2026-09-02: **170 pruebas, todas en verde.**

| Suite | Pruebas | Qué cubre |
| --- | --- | --- |
| `Permoda.Pay.Domain.Tests` | 37 | Invariantes de la máquina de estados, `Money`, `CardIdentifier`, identificadores, `ReferenceNumber` |
| `Permoda.Pay.Application.Tests` | 35 | `ProcessSaleHandler` (incluidas las rutas de reverso y de fallo de persistencia), activación virtual con captura de cliente, recuperación, ciclo de vida, `CustomerMobileNoBuilder` (concatenación `CC-NombreApellido` con limpieza de tildes) |
| `Permoda.Pay.Maui.Tests` | 73 | Contrato HiPOS: mapeo de intents, composición de respuestas, comprobantes, `ModifyDocumentResult`, saneo de campos DIAN, lectura del documento de totalización |
| `Permoda.Pay.Infrastructure.Tests` | 22 | Contrato HTTP con Ogloba (payloads, headers, mapeo de errores) y persistencia cifrada |
| `Permoda.Pay.Architecture.Tests` | 3 | Dirección de las dependencias entre capas |

Las suites no requieren dispositivo ni emulador: todas compilan a `net10.0` y usan dobles de
prueba. Por eso el proyecto `Permoda.Pay.Maui.HiPos` no referencia tipos de Android — ver
[ARQUITECTURA.md](ARQUITECTURA.md#por-qué-mauihipos-está-separado-de-maui).

Lo que **no** cubren, y por eso existen los escenarios manuales: el enrutamiento real de intents de
HiPOS, la impresión en la térmica, el lector de tarjetas, la entrega de correos de Ogloba y el
comportamiento del módulo fiscal.

## 2. Preparación de la terminal

1. Tablet homologada por HiPOS, Android 13 o superior, encendida y con conectividad HTTPS al host de
   Ogloba del ambiente correspondiente ([AMBIENTES.md](AMBIENTES.md)).
2. `adb` disponible en el equipo del tester y la tablet autorizada.
3. Cliente HiPOS instalado y abierto.
4. APK del ambiente correcto instalada
   ([BUILD_Y_DESPLIEGUE.md](BUILD_Y_DESPLIEGUE.md)). **No usar una APK de UAT en una caja
   productiva**: transaccionaría contra el sandbox pero facturaría con la configuración fiscal real.
5. Caja configurada (escenario 0).

### Escenario 0 — Alta inicial de la caja

Solo la primera vez, o después de una desinstalación.

1. Abrir el módulo desde el launcher. Al no estar configurado, abre en *Configuración*.
2. Crear el **PIN de administrador** (mínimo 4 dígitos). Anotarlo: no se puede recuperar.
3. Completar **tienda** (`K#####`), **caja** y **contraseña de Ogloba**. En builds no productivos hay
   un botón de autocompletado con los datos del sandbox.
4. Guardar y continuar. Registrar al menos un **cajero con su contraseña**.
5. Verificar en el encabezado: nombre de la tienda, cajero, distintivo del ambiente y estado de
   conexión.

```pwsh
adb -s <serial> shell pm list packages | Select-String tefogloba
```

## 3. Escenarios de certificación

Cada escenario indica su vía (UI del módulo o intent de HiPOS) y qué evidencia se captura.

### E1 · Activación de bono virtual con entrega por correo

**Vía**: UI del módulo. *No existe por HiPOS* — el contrato de ICG no define un
`TransactionType=ACTIVATION`, y la activación requiere autenticación de cajero.

1. Menú → *Activar bono* → tipo **Virtual**.
2. Completar **datos del cliente**: tipo de documento, número de documento (6-15 dígitos) y nombre
   y apellidos. El preview debe mostrar `Se envía como: 1011086580-AndresFelipeDiazBernal`.
3. Correo del cliente + importe dentro del rango del catálogo ($30.000 – $500.000 en sandbox).
4. Procesar.
5. Verificar en `logcat` la secuencia `/orderCreation` → `/orderConfirm`, con `isSuccessful=true` y
   `orderStatus=042` (o `041`/`043` si Ogloba sigue procesando). El `message` del `orderItems[0]`
   debe incluir el string concatenado `CC-NombreApellido`.
6. Verificar que el correo llegue al cliente con el enlace del bono.
7. Verificar el registro en *Bitácora* — debe contener `cliente {nombre} · {TipoDoc} {número}`.

### E2 · Activación de bono físico

**Vía**: UI del módulo.

1. Menú → *Activar bono* → tipo **Física**.
2. Completar **datos del cliente**: tipo de documento, número de documento y nombre y apellidos.
3. Escanear o digitar el serial del bono e ingresar el importe.
4. Verificar en `logcat` la secuencia `/activation` → `/confirmTransaction` → `/reconciliation`. El
   `note` del request a `/activation` debe incluir el string concatenado `CC-NombreApellido`.
5. Consultar el saldo del bono y confirmar que sea el importe activado.
6. Verificar el registro en *Bitácora* — debe contener `cliente {nombre} · {TipoDoc} {número}`.

### E3 · Redención desde la UI

**Vía**: UI del módulo.

1. Menú → *Redimir saldo* → escanear el serial + importe → agregar al lote → procesar.
2. Verificar en `logcat`: `/balance` (validación previa) → `/redemption` → `/confirmTransaction` →
   `/reconciliation`.
3. Consultar el saldo y confirmar el descuento exacto.

### E4 · Redención como medio de pago de HiPOS

**Vía**: intent. Es el flujo productivo.

1. En HiPOS, totalizar una venta y elegir el medio de pago **Ogloba**.
2. El módulo abre su pantalla; escanear el bono y confirmar.
3. Verificar que HiPOS **cierre el documento e imprima** el comprobante del bono (una sola tirilla,
   la del cliente) además de la factura.
4. Verificar que el importe descontado del bono coincida con el de la venta —
   **este escenario es el que valida la conversión de escala del importe**
   ([FLUJOS_DE_OPERACION.md](FLUJOS_DE_OPERACION.md#1-unidades-monetarias)).

### E5 · Pago parcial

**Vía**: intent.

1. Totalizar una venta por un importe **mayor** al saldo del bono.
2. Pagar con Ogloba.
3. Verificar que HiPOS baje el medio Ogloba a lo que el bono cubrió y pida el resto por otro medio,
   en lugar de rechazar el cobro entero.

### E6 · Resultado incierto y reverso

**Vía**: UI del módulo.

1. Iniciar una redención y cortar la red (WiFi en avión) inmediatamente después de confirmar.
2. Verificar en `logcat` que se dispara `/reversal` con el mismo `transactionNumber`.
3. Consultar el saldo del bono: debe estar intacto.

### E7 · Recuperación tras cierre abrupto

**Vía**: UI del módulo + ADB.

1. Iniciar una redención y matar el proceso antes de que termine
   (`adb shell am force-stop com.permoda.tefogloba`).
2. Reabrir el módulo.
3. Verificar en `logcat` que la recuperación retoma la transacción y la cierra o la reversa.

### E8 · Errores esperados de Ogloba

Con datos preparados en el sandbox, verificar que el cajero ve un mensaje **en español, corto y
accionable** en cada caso:

| Caso | Código esperado |
| --- | --- |
| Activar un bono ya activado | `85` |
| Redimir un bono cuya activación no se confirmó | `104` |
| Redimir más que el saldo | `53` |
| Importe fuera del rango del producto | `73` |

### E9 · Rechazo de deshacer

**Vía**: intent.

1. Intentar una nota de crédito sobre una venta pagada con bono → el módulo responde `FAILED` con
   título y motivo, y **el cajero debe ver el aviso en el POS**.
2. Idem para una anulación.
3. Verificar que **el saldo del bono no se mueva** en ninguno de los dos casos.

### E10 · Cancelación de totalización

**Vía**: intent.

1. Pagar una venta con bono y cancelar la totalización.
2. Verificar que HiPOS **permita soltar la línea** de pago del bono.
3. Verificar en `logcat` la advertencia con la referencia y el importe.
4. **Confirmar explícitamente que el saldo del bono NO regresó**: es el comportamiento actual, y
   quien opere debe saberlo ([INTEGRACION_HIPOS.md](INTEGRACION_HIPOS.md#6-totalization_canceled)).

### E11 · Cierre de caja

**Vía**: intent.

1. Ejecutar el cierre de caja en HiPOS.
2. Verificar en `logcat`: `HiPOS BATCH_CLOSE accepted: per-tx reconciliation already done.`
3. Verificar que HiPOS no reporte error.

### E12 · Consulta de saldo

**Vía**: UI del módulo.

1. Menú → *Consultar saldo* → serial → consultar.
2. Verificar en `logcat` que **solo** se llama a `/balance`.

> El intent `QUERY_TRANSACTION` está implementado pero **HiPOS no lo envía**, porque
> `SupportsTransactionQuery` está en `false`. Solo se puede ejercitar por ADB, y no es parte de la
> certificación mientras la bandera siga apagada.

## 4. Escenarios sin camino en la aplicación

Estos escenarios aparecen en plantillas de certificación de Ogloba y **no tienen implementación**,
por decisión de alcance, no por deuda pendiente:

| Escenario de la plantilla | Estado | Evidencia alternativa |
| --- | --- | --- |
| Void redemption | Sin camino: los bonos no se anulan desde el POS | Documentar como fuera de alcance, o evidenciar `/voidTransaction` directamente contra el sandbox con un cliente REST |
| Void activation | Sin camino, misma razón | Igual |
| Recarga de bono (`/reload`) | El adaptador existe; ninguna pantalla lo invoca | Evidenciar `/reload` directamente contra el sandbox |

Acordar la vía con el responsable del proyecto antes de diligenciar la plantilla.

## 5. Captura de evidencia

Para cada escenario se registra: **petición**, **respuesta**, **resultado** (aprobado/rechazado) y
notas de cualquier desviación.

Los cuerpos literales de petición y respuesta salen de `logcat`, bajo la etiqueta
`TefOgloba.Ogloba`:

```pwsh
# Consola 1 — log en vivo mientras se ejecuta el escenario
adb -s <serial> logcat -v threadtime -s TefOgloba TefOgloba.Ogloba TefOgloba.HiPos

# Consola 2 — ejecutar el escenario en la terminal

# Al terminar, volcar todo a un archivo y adjuntarlo al paquete de evidencia
adb -s <serial> logcat -d > artifacts/logcat-<escenario>.txt
```

La pantalla *Bitácora* del módulo y el exportador de logs de operación
(`AdminLogExportPage`) sirven para el resumen de metadatos —tienda, caja, referencia, número de
transacción, resultado—, **no** para los cuerpos JSON: esos vienen de `logcat`.

El paquete de evidencia de cada ronda de certificación debería contener:

1. La APK exacta que se probó, con su configuración de compilación.
2. Un `logcat` por escenario.
3. La plantilla de certificación diligenciada.
4. La lista de desviaciones y su decisión.

## 6. Errores frecuentes de Ogloba durante las pruebas

Tabla de referencia rápida. La lista completa está en
[OGLOBA_API_REFERENCE.md §5](OGLOBA_API_REFERENCE.md#5-códigos-de-error-completos).

| Código | Significado | Qué revisar |
| --- | --- | --- |
| 22 | Producto desconocido | El código de producto del ambiente ([AMBIENTES.md](AMBIENTES.md)) |
| 23 | Tienda desconocida | La tienda en *Configuración*, y que esté habilitada en el back office de Ogloba para ese módulo |
| 50 | Referencia o terminal desconocida | El `transactionNumber` enviado |
| 52 | Tarjeta desconocida | Que el bono exista; verificar con *Consultar saldo* |
| 53 | Saldo insuficiente | Es el comportamiento esperado; validar que el módulo ofrezca pago parcial |
| 56 | Tarjeta vencida | Usar otro bono |
| 73 | Importe de activación inválido | El importe está fuera del rango del producto |
| 85 | Tarjeta ya activada | Es el comportamiento esperado (E8) |
| 99 | Sospecha de fraude | Ogloba bloqueó la operación; escalar internamente |
| 104 | Tarjeta inactiva | La activación no se confirmó |
| 161 | Tarjeta ya usada | Saldo en cero |
| 222 | Terminal o caja inactiva | La caja está deshabilitada en Ogloba |
| 239 | Referencia de origen incorrecta | Los campos `original*` no coinciden |
| 230040 | Formato de cajero inválido | El cajero no está registrado o su identificador no cumple el formato |

## 7. Responsables

| Rol | Responsabilidad |
| --- | --- |
| Líder de proyecto (Permoda) | Aprobación de la certificación y autorización del despliegue a producción |
| Equipo Permoda.Pay | Mantenimiento del módulo y corrección de defectos |
| Soporte de Ogloba | Defectos de la plataforma Ogloba (no del módulo) |
| ICG / CloudLicense | Alta del módulo, `apk_name`, código de versión y huella de firma |
