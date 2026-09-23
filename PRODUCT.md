# Product

<!-- impeccable:product-schema 1 -->

## Platform

android

## Users

Primary: cajero KOAJ operando una caja POS HiPOS en una de las 512 tiendas Permoda en Colombia. Situación: turno de venta, total acumulado en HiPOS, selecciona "Bono Ogloba" como medio de pago, escanea o tipea el PAN del bono (físico o digital), confirma el monto. Job: aceptar pago con bono regalo sin interrumpir el flujo de la venta.

Secondary: cliente final KOAJ con un bono Ogloba (físico producto `113817` o digital `113816` en sandbox) que quiere canjearlo en tienda. No interactúa con el módulo directamente: el cajero opera la UI y el cliente entrega el bono.

Tertiary (no interactivo): el líder de proyecto de Permoda, firmante de HiOSTORE, como contraparte técnica durante la validación interna.

## Product Purpose

Permoda.Pay Ogloba (`TefOgloba`) es el Módulo de Cobro Electrónico que las 512 tiendas KOAJ necesitan para aceptar bonos Ogloba GiftCard dentro de su POS HiPOS. Antes de este módulo KOAJ no tenía ninguna pasarela de gift cards integrada; ahora puede activar bonos (físicos y virtuales con envío por correo), redimirlos y consultar su saldo sin salir de HiPOS. KOAJ **no** procesa anulaciones ni devoluciones de bonos: es una decisión de producto, no una funcionalidad pendiente.

Success means: el cajero completa la venta con bono en el mismo flujo que cualquier otro medio de pago, el saldo del bono baja correctamente en Ogloba, el comprobante se imprime en la térmica de HiPOS, y los 8 escenarios de validación interna se cierran sin hallazgos materiales.

## Positioning

Primer y único módulo de cobro electrónico de bonos para KOAJ en HiPOS. La propuesta no es "otra integración":

- Implementa literalmente el contrato HiOSTORE de ICG (las 11 acciones de `icg.actions.electronicpayment.permoglobal.*` más `TOTALIZATION_CANCELED`), por lo que el cajero lo activa desde el POS sin desviarse del flujo HiPOS estándar.
- Implementa literalmente el flujo obligatorio de 3 pasos de Ogloba (Step 1 Requested → Step 2 Confirmed/Cancelled → Step 3 Reconciliation) con persistencia cifrada antes del Step 2, así nunca deja una transacción colgada ni siquiera si la tablet se apaga a mitad del proceso.
- Cifra credenciales y transacciones con SQLCipher + Android Keystore, cumple los compromisos HiOSTORE (sin secretos en código, HTTPS only, `allowBackup=false`).
- Exporta el log request/response en JSON listo para pegar en el XLSX de certificación de Ogloba — el primer integrador en Latam que entrega esa trazabilidad completa.

Una pasarela competidora no podría copiar esto sin reimplementar el adaptador HiPOS, la máquina de 3 pasos con persistencia intermedia y la exportación UAT exacta.

## Operating Context

- **Hardware**: tablets Android 13+ (`SupportedOSPlatformVersion = 33`) homologadas HiPOS, con impresora térmica integrada (42 columnas típicas).
- **Entorno de red**: LAN de tienda con salida a Internet HTTPS hacia `co-ts.ogloba.com` (testing) o `co-prod.ogloba.com` (producción). Sin VPN ni proxy especial.
- **Catálogo**: 512 tiendas KOAJ precargadas en el back office Ogloba (`gcap`) con Store IDs de la forma `K#####`. Username de Basic Auth = Store ID. Password de testing común a todas; en producción está pendiente definir si es única por tienda.
- **Catálogo de productos**: 10 tarjetas físicas de prueba (producto `113817`) más el producto digital `113816` en sandbox.
- **Jornada**: el cajero hace ventas continuas durante el día; al cierre hace batch reconciliation. La APK puede ser "matada" por Ogloba (timeout) o por el usuario (kill -9) en cualquier momento entre Step 1 y Step 3 — el módulo debe recuperarse en el próximo arranque.
- **Idioma**: mensajes al cajero en español (`es-co`); errores mapeados a texto claro en español; comprobantes imprimibles en español.
- **Auditoría externa**: el XLSX UAT de Ogloba exige capturar cada par request/response de los 8 escenarios de certificación (4 proceso + 4 error) — el log exportado del módulo se pega literalmente en la plantilla.

## Capabilities and Constraints

Capacidades confirmadas:

- Recepción de las 11 acciones del contrato de ICG (`INITIALIZE`, `FINALIZE`, `GET_VERSION`, `GET_BEHAVIOR`, `GET_CUSTOM_PARAMS`, `GET_PRINT_INFO`, `SHOW_SETUP_SCREEN`, `TRANSACTION`, `READ_CARD`, `CHARGE_CARD`, `GET_CARD_DATA`) con namespace `icg.actions.electronicpayment.permoglobal.*`, más `TOTALIZATION_CANCELED` en `icg.actions.document.*`.
- Activar bonos físicos (`/activation` → `/confirmTransaction` → `/reconciliation`) y virtuales con entrega por correo (`/orderCreation` → `/orderConfirm`).
- Redimir (`/balance` previo → `/redemption` → `/confirmTransaction` → `/reconciliation`) y consultar saldo (`/balance`).
- Reversar por timeout (`/reversal`) y drenar pendientes al arranque.
- Flujo estricto de 3 pasos con persistencia SQLCipher antes del Step 2 para garantizar recuperación tras crash.
- Montos en **pesos completos**, no en centavos: confirmado contra el sandbox de Ogloba el 2026-07-28 (multiplicar ×100 devuelve `errorCode 73`).
- Mensajes de error en español directamente desde Ogloba vía el header `Accept-Language: es-co`, más el mapeo local de códigos.
- Persistencia cifrada (clave derivada de Android Keystore, `busy_timeout=5000`, `cipher_memory_security=ON`).
- SecureStorage por tienda para credencial Ogloba (Basic Auth password en Base64).
- Tres ambientes: `Debug` (ENVIRONMENT_DEVELOPMENT), `UAT` (ENVIRONMENT_UAT), `Release` (ENVIRONMENT_PRODUCTION) con URLs `co-ts.ogloba.com` y `co-prod.ogloba.com`. Detalle en `docs/AMBIENTES.md`.
- 170 pruebas automatizadas (xUnit + dobles de prueba + aserciones de arquitectura) en 5 suites.
- UI propia del módulo para las operaciones que no caben en un intent del POS: Inicio, Consultar saldo, Bitácora, Usuarios y Configuración en el menú, más Activar bono y Redimir saldo alcanzables por flujo. **No habilitada como modo quiosco de producción.** El feedback al cajero es un toast nativo de Android.
- Autenticación de cajero por usuario y contraseña, PIN de administrador y registro de cajeros con activación/desactivación.
- Exportador de la bitácora de operaciones para el paquete de evidencia de certificación (los cuerpos JSON literales se capturan de `logcat`).

Constraints explícitos:

- Android API 33 mínimo, .NET 10 / .NET MAUI, namespace `com.permoda.tefogloba` (no renombrar — registrado en `CloudLicense` de ICG).
- Solo KOAJ + Ogloba como PSP y marca; no es plataforma multi-marca ni multi-PSP. Cualquier extensión futura es un proyecto separado.
- La UI autónoma es solo para UAT/soporte; el camino de producción es siempre vía Intents HiPOS.
- Sin marcas de terceros en UI ni comprobantes: no aparecen logos ni nombres de Ogloba, ICG, HiPOS, Sistecrédito. La regla HiOSTORE de "no third-party brands" se respeta literalmente.
- El header `X-WSRG-API-Version: 2.18` es obligatorio en TODAS las llamadas; el `Accept-Language: es-co` es opcional pero recomendado.
- Permisos Android mínimos: solo `INTERNET` y `ACCESS_NETWORK_STATE`. `usesCleartextTraffic="false"`, `allowBackup=false`.
- Keystore de release firmado lo entrega el equipo de seguridad de KOAJ — no commitear nunca a git.

Fuera de alcance hoy (no confundir con deuda técnica):

- **Recarga de bonos**: el endpoint `/reload` está implementado en el adaptador de Ogloba, pero ningún caso de uso ni pantalla lo invoca. No existe flujo de recarga para el cajero.
- **Anulación y devolución**: el adaptador de `/voidTransaction` existe pero ningún caso de uso lo invoca, y el orquestador rechaza `REFUND` y `VOID_TRANSACTION` por decisión de KOAJ. Los escenarios de anulación de las plantillas de certificación no tienen camino en la app.
- **Devolución del saldo al cancelar una totalización**: el POS suelta la línea de pago, pero el saldo del bono no regresa; queda el rastro en el log para cuadrarlo.

Pendientes por confirmar antes de release (detalle con dueño y riesgo en `docs/ESTADO.md`):

- ¿La password de Basic Auth en producción es única por tienda o común a todas las 512?
- Certificación final en hardware HiPOS homologado por KOAJ.
- El código de producto digital de producción: `Release` declara `113811` (dotación B2B), que no es el equivalente del bono B2C `113816` verificado en sandbox.
- El material de firma vigente y su huella SHA-1 entregada a ICG: el `.csproj`, el script de empaquetado y el inventario de keystores apuntan a archivos distintos.

## Brand Commitments

- Nombre del producto: **Permoda.Pay Ogloba** (interno) / **TefOgloba** (nombre del ensamblado). El `apk_name` registrado en CloudLicense de ICG es `permoglobal`, y el nombre que ve el cajero en el POS es `Ogloba`.
- ApplicationId: `com.permoda.tefogloba` — no renombrar.
- Branding visible: solo Permoda (empresa integradora) y KOAJ (marca retail). Prohibido usar logos o nombres de Ogloba, ICG, HiPOS, Sistecrédito, Addi o Nequi en UI o comprobantes — regla HiOSTORE + respuesta del usuario en init.
- Voz: técnica y sobria en mensajes de error (cajero necesita resolver rápido, no leer marketing).
- Compromisos firmados en HiOSTORE (ver `docs/SEGURIDAD.md §7`): app libre de fallos, info veraz, seguridad apropiada, sin funciones ocultas, sin publicidad cruzada, sin obligar a instalar otras apps, cumple GDPR y equivalentes.
- Certificación interna: el líder de proyecto de Permoda es el interlocutor válido; cualquier desviación del contrato se coordina internamente antes de tocar la APK.

## Evidence on Hand

- `docs/` — documentación vigente del módulo: ambientes, arquitectura, contrato de HiPOS, referencia de la API de Ogloba, flujos, seguridad, build, pruebas, operación, decisiones y estado.
- `docs/archivo/DOSSIER_INTEGRACION_2026.md` — dossier de integración original (julio de 2026), archivado: describe el contrato tal como se acordó y quedó superado por la implementación.
- `Permoda.Pay.sln` — solución .NET 10 con 5 proyectos (`Domain`, `Application`, `Infrastructure`, `Maui.HiPos`, `Maui`) y 5 suites de pruebas (170 pruebas).
- 10 tarjetas físicas de prueba Ogloba (producto `113817`).
- 1 producto digital de prueba en sandbox (`113816`, rango $30.000 – $500.000).
- 512 tiendas KOAJ precargadas en el back office Ogloba.
- Contrato técnico de ICG y manuales de Ogloba en PDF, en la raíz del repositorio (fuera de git).

Inventario de ausencias que trabajo futuro no debe fabricar:

- No hay testimonios de clientes, métricas de conversión, logos de Ogloba/ICG ni capturas de prensa.
- No hay datos de producción reales (solo los 10+1 PANs de testing).
- No hay precios, descuentos, ni campañas.

## Product Principles

1. **Crash-free por diseño.** Persistir siempre antes del Step 2; `RecoverPendingPaymentsHandler` drena pendientes en cada arranque; cualquier fallo entre Step 1 y Step 3 es recuperable, nunca deja dinero bloqueado.
2. **Contrato literal, no interpretación.** Implementar HiPOS y Ogloba exactamente como sus documentos lo describen; cualquier ambigüedad se consulta con el líder de proyecto antes de codificar.
3. **Seguridad por defecto.** Sin secretos en código ni en logs; SQLCipher + Keystore + SecureStorage; HTTPS only; `allowBackup=false`; PAN enmascarado (`113817******5937`) en todo lo visible.
4. **Trazabilidad UAT primero.** Cada par request/response se loguea con timestamp, storeId, terminalId, referenceNumber, transactionNumber. El JSON exportado se pega directamente en el XLSX sin reformatear.
5. **KISS y DRY en la UI POS.** `StatusBanner` único, `GiftCardRequestFactory` único, `BootstrapConfigAsync` único, estilos Koaj centralizados en `Resources/Styles/` — sin redefinir tokens ni componentes.

## Accessibility & Inclusion

- **Cajero**: usuario de tablet POS en entorno retail con prisa; necesita tipografía legible a 1m, contraste alto, áreas táctiles ≥ 44dp, mensajes de error cortos y accionables, e importes siempre en pesos completos (se digitan sin comas ni puntos: `50000` = $50.000 COP).
- **Idioma**: español colombiano (`es-co`) en toda la UI y en los mensajes de error mapeados desde los 80+ códigos Ogloba.
- **Impresión térmica**: comprobantes en 42 columnas (estándar HiPOS), texto plano sin dependencias de fuentes externas.
- **Visibilidad de datos sensibles**: nunca se imprime el PAN completo; siempre enmascarado con prefijo `113817******`.
- **Recuperación asistida**: si el cajero mata la app a mitad de una transacción, al reabrir la APK le muestra qué quedó pendiente y le permite cerrar o cancelar — no necesita llamar a soporte para recover.
