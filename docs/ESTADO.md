# Estado y pendientes

Foto del módulo verificada contra el código y las pruebas el **2026-09-02**. Este documento se
actualiza; si una afirmación de otro documento contradice a esta, gana esta.

## 1. Qué está listo

| Capacidad | Estado | Verificación |
| --- | --- | --- |
| Redención como medio de pago de HiPOS (`SALE` / `NEGATIVE_SALE` con tender) | Implementado | Pruebas + terminal |
| Ciclo de 3 pasos con persistencia antes del Step 2 | Implementado | Pruebas de `ProcessSaleHandler` |
| Reverso de resultados inciertos (`/reversal`) | Implementado | Pruebas |
| Recuperación de pagos pendientes al arranque | Implementado | Pruebas |
| Activación de bono físico (`/activation` → confirmar → conciliar) | Implementado | Sandbox |
| Activación de bono virtual con entrega por correo (`/orderCreation` → `/orderConfirm`) | Implementado | Sandbox, producto `113816` |
| **Captura de datos del cliente** (cédula + nombre → `CC-NombreApellido` en `note` / `message`) | Implementado | 9 pruebas del helper `CustomerMobileNoBuilder` |
| Consulta de saldo (`/balance`) | Implementado | Sandbox |
| Pago parcial cuando el bono no cubre la factura | Implementado | Pruebas |
| Rechazo de notas de crédito y anulaciones | Implementado | Terminal |
| Cierre de caja (`BATCH_CLOSE`) | Implementado | Terminal |
| Cancelación de totalización (soltar la línea de pago) | Implementado, **sin devolución de saldo** | Terminal |
| Configuración automática de la tienda por `INITIALIZE` | Implementado | Pruebas |
| Registro de cajeros con contraseña, activación y desactivación | Implementado | — |
| PIN de administrador | Implementado | — |
| Bitácora del turno, con historial de Ogloba | Implementado | — |
| Cifrado de transacciones pendientes y credenciales | Implementado | Pruebas de infraestructura |
| Captura por lector de código de barras en los formularios | Implementado | Terminal |
| 170 pruebas automatizadas | En verde | `dotnet test` |

## 2. Fuera de alcance por decisión de producto

No son deuda técnica. Confundirlos con pendientes genera compromisos que nadie va a cumplir.

| Operación | Decisión |
| --- | --- |
| Anulación de un cobro con bono desde el POS | KOAJ no la procesa (2026-09-02) |
| Nota de crédito sobre un cobro con bono | Misma decisión |
| Recarga de bonos | No hay flujo definido para el cajero |
| Devolución parcial de un cobro | Deshacer suelta la línea completa |
| Multi-marca o multi-proveedor | El módulo es específico de KOAJ y Ogloba. Cualquier extensión es otro proyecto |
| Modo quiosco de la UI en producción | La UI propia es para activación, consulta y soporte; el camino de cobro es siempre el intent de HiPOS |

## 3. Pendientes abiertos

Ordenados por lo que bloquean.

### Bloquean el paso a producción

| # | Pendiente | Riesgo si no se resuelve | Dueño |
| --- | --- | --- | --- |
| P1 | **Discrepancia del material de firma.** El `.csproj` apunta a `ogloba-fresh.keystore`; `scripts/build-apk.ps1` y `assets/keystore/Hashes.txt` apuntan a `ogloba.keystore`, y el `.csproj` afirma que la contraseña de este último se perdió | Empaquetar con el keystore equivocado, o firmar con una llave cuya huella no es la registrada en ICG. Una rotación no anunciada obliga a desinstalar en las 512 terminales, borrando su configuración | Equipo Permoda.Pay |
| P2 | **Código de producto digital de producción sin confirmar.** Hoy `Release` declara `113811` (*KOAJ Dotacion B2B*), que no es el bono de regalo B2C elegido para el cajero | La activación virtual en producción emite el producto equivocado o falla con `errorCode 73` | Ogloba |
| P3 | **Contraseña de Basic Auth en producción**: sin definir si es única por tienda o común a las 512 | Cambia el procedimiento de despliegue y el alta de las tiendas | Ogloba / Permoda |
| P4 | **Certificación en hardware homologado por KOAJ** | Sin ella no hay aprobación de despliegue | KOAJ |
| P5 | **Huella SHA-1 de la llave vigente entregada a ICG** para el alta en CloudLicense. Depende de P1 | HiPOS puede no reconocer el módulo | Permoda / ICG |

### Convendría cerrar antes de escalar a las 512 tiendas

| # | Pendiente | Detalle | Dueño |
| --- | --- | --- | --- |
| P6 | **Devolución de saldo al cancelar una totalización.** Hoy el POS suelta la línea y el saldo del bono no regresa; solo queda una advertencia en el log con la referencia y el importe | Requiere invocar `/voidTransaction` (el adaptador existe, ningún caso de uso lo llama) y una decisión de producto: ¿se devuelve el saldo o se cuadra manualmente? | Permoda / KOAJ |
| P7 | **Esquema del broadcast de auditoría** `icg.actions.externalApi.AUDIT` sin especificar en el contrato. Se envía el mismo resultado y extras más un timestamp, a mejor esfuerzo | El payload podría no calzar con lo que ICG espera. Un receptor inexistente no rompe nada | ICG |
| P8 | **Versión de la API de Ogloba en producción**: `2.18` está verificado en sandbox; falta confirmación escrita para `co-prod` | La diferencia con `2.20` es aditiva, así que el riesgo es bajo | Ogloba |
| P9 | **Rango de importes del producto de producción** (depende de P2): la UI valida contra el rango del catálogo, y si no coincide el cajero tipea un importe que la pantalla acepta y Ogloba rechaza | Rechazos con `errorCode 73` en caja | Ogloba |
| P10 | **Extras esperados en `GET_PRINT_INFO`** sin confirmar. Hoy se responde `OK` sin información adicional, porque los comprobantes ya viajan en la respuesta de `TRANSACTION` | Bajo: no bloquea el arranque | ICG |

### Deuda conocida, sin urgencia

| # | Detalle |
| --- | --- |
| P11 | `Money.MinorUnits` y `AmountMinorUnits` se llaman así por la suposición original de centavos y contienen pesos. Renombrarlos toca muchas firmas; mientras no se haga, el nombre engaña y hay que leer [FLUJOS_DE_OPERACION.md](FLUJOS_DE_OPERACION.md#1-unidades-monetarias) |
| P12 | Adaptadores implementados sin caso de uso que los invoque: `ReloadAsync`, `VoidAsync`, `ReturnOrderAsync`. Están cubiertos por pruebas de contrato, pero son código muerto en producción hasta que exista un flujo |
| P13 | Receptor de webhooks de Ogloba (estado de orden y catálogo) sin implementar. Requiere un endpoint HTTPS acordado con Ogloba |
| P14 | `QUERY_TRANSACTION` implementado como consulta de saldo, con la bandera del contrato apagada. Implementar la semántica real de recuperación de venta permitiría habilitarla (ver [DECISIONES.md](DECISIONES.md#d-08)) |
| P15 | Soportar los dos nombres (`transactionRecord` / `transactionRecords`) que Ogloba usa inconsistentemente en `/queryTransactionsHistory` |
| P16 | **`dotnet format --verify-no-changes` falla hoy** con errores de espaciado en `OglobaGiftCardProvider.cs` (líneas largas que el formateador quiere partir). No afecta al comportamiento —las 170 pruebas pasan—, pero contradice la regla de "0 errores, 0 advertencias" antes de hacer merge: correr `dotnet format` y revisar el resultado |

## 4. Riesgos operativos que hay que conocer

| Riesgo | Mitigación actual |
| --- | --- |
| Una APK de UAT instalada en una caja productiva transacciona contra el sandbox pero **factura con la configuración fiscal real** de la tienda: el alta de CloudLicense es única, sin separación de ambientes | El distintivo del encabezado muestra el ambiente. Procedimiento: no dejar APK de UAT en cajas productivas ([AMBIENTES.md](AMBIENTES.md#6-ambientes-de-terceros-que-dependen-de-esto)) |
| Si Sistecrédito (`com.pos2pay`) se reactiva en una terminal, hay que verificar que no colisione ningún `apk_name` declarado | `permoda` no se declara. Verificar con `cmd package query-activities` |
| Cambiar la llave de firma obliga a desinstalar en cada terminal, perdiendo PIN, credenciales y cajeros | Las tres configuraciones comparten llave; una rotación se planifica, no se improvisa |
| El PIN de administrador no se puede recuperar | Reinstalar y reconfigurar la caja |
| Los registros de negocio viven en memoria: se pierden al terminar el proceso | Solo las transacciones pendientes se persisten. La evidencia de certificación se captura de `logcat` en el momento |

## 5. Cómo mantener este documento

Se actualiza cuando cambia el estado, no cuando cambia el código. Cada pendiente debe tener número,
riesgo y dueño; un pendiente sin dueño no es un pendiente, es una queja. Cuando algo se cierra, se
mueve a §1 con su verificación, y si la decisión detrás fue no trivial, se registra en
[DECISIONES.md](DECISIONES.md).
