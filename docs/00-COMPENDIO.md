# Compendio maestro — Permoda.Pay Ogloba (TefOgloba)

> **Esta página es el mapa navegable de toda la documentación técnica del módulo.**
> Si un dato aparece en dos documentos, uno es la fuente de verdad y el otro enlaza acá.
> La regla de "un dato, un lugar" se aplica también al compendio: lo que vive en otro
> documento no se repite acá.

**Última verificación**: 2026-09-02 contra el árbol de trabajo actual.

---

## 1. Descripción de una línea

APK Android escrita en C#/.NET MAUI que conecta el POS **HiPOS** de ICG con la pasarela de bonos
**Ogloba GiftCard** de las 512 tiendas KOAJ (Permoda Ltda, Colombia), para aceptar bonos como
medio de pago en caja. No se abre sola en el flujo productivo: HiPOS la invoca por `Intent` en el
momento del cobro. Expone además una UI propia para activar bonos, consultar saldo, ver bitácora
y configurar la caja.

## 2. Glosario de seis renglones

| Concepto | Significado |
| --- | --- |
| **TEF** | Terminal Electrónica de Pago: módulo de cobro que vive dentro del POS |
| **apk_name** | Llave de enrutamiento que ICG registra en CloudLicense. Nuestro valor: `permoglobal` |
| **HiPOS** | POS de ICG que corre en las tablets de las tiendas KOAJ |
| **Ogloba** | Pasarela de bonos GiftCard (REST + Basic Auth por tienda) |
| **Step 1 / Step 2 / Step 3** | Las tres llamadas obligatorias para mover plata: solicitar → confirmar → conciliar |
| **CC-NombreApellido** | Formato `Cédula-GarcíaLopez` en que viajan los datos del cliente a Ogloba (campo `note` / `message`) |

## 3. Identidad e invariantes técnicas

| | |
| --- | --- |
| Cliente | KOAJ · Permoda Ltda · Colombia |
| POS anfitrión | HiPOS (ICG) — contrato v4.0 de Módulo de Cobro Electrónico |
| Pasarela | Ogloba GiftCard REST API · `X-WSRG-API-Version: 2.18` · Basic Auth por tienda |
| Stack | .NET 10, .NET MAUI, xUnit, SQLCipher, Android Keystore, SecureStorage |
| Plataforma | Android 13+ (`SupportedOSPlatformVersion = 33`), tablets homologadas por HiPOS |
| `ApplicationId` | `com.permoda.tefogloba` |
| `AssemblyName` / `ApplicationTitle` | `TefOgloba` |
| `apk_name` | `permoglobal` (registrado en CloudLicense de ICG) |
| `ApplicationVersion` (código) | `1` |
| `ApplicationDisplayVersion` | `1.0.0` |
| Arquitectura | Clean Architecture + Puertos y Adaptadores |
| Capas de dominio | `Domain` → `Application` → `Infrastructure` + `Maui.HiPos` (← `Maui`) |
| Pruebas | 170 en 5 suites · xUnit + dobles |
| Firmas | `Debug` · `UAT` · `Release` se firman con la misma llave (`ogloba-fresh.keystore`) |
| Ambientes | `Debug` → `co-ts.ogloba.com` · `UAT` → `co-ts.ogloba.com` · `Release` → `co-prod.ogloba.com` |

## 4. Índice navegable de la documentación

### 4.1 Documentos de la raíz del repositorio

| Documento | Qué encontrar |
| --- | --- |
| [`README.md`](../../README.md) | Tarjeta resumen del módulo: identidad, capacidades, arranque rápido, estructura |
| [`AGENTS.md`](../../AGENTS.md) | Manual operativo para contribuir: comandos, reglas de código, checklist pre-merge |
| [`PRODUCT.md`](../../PRODUCT.md) | Definición formal del producto: usuarios, propósito, posicionamiento, alcance, principios |
| [`DESIGN.md`](../../DESIGN.md) | Sistema de diseño Koaj: paleta, tipografía, componentes, layout, reglas |
| `CHANGELOG.md` | Bitácora de cambios por versión |

### 4.2 Documentos en `docs/` (fuente de verdad técnica)

| Documento | Tema | Responsable |
| --- | --- | --- |
| [`AMBIENTES.md`](AMBIENTES.md) | Los tres ambientes (Debug/UAT/Release), URLs, productos, timeouts, credenciales por tienda, alta en CloudLicense, verificación de ambiente | — |
| [`ARQUITECTURA.md`](ARQUITECTURA.md) | Capas, dirección de dependencias, puertos/adaptadores, componentes clave, ciclo de vida del proceso | — |
| [`BUILD_Y_DESPLIEGUE.md`](BUILD_Y_DESPLIEGUE.md) | Compilar, firmar, instalar, actualizar, alta de CloudLicense, convivencia con otros módulos TEF | — |
| [`DECISIONES.md`](DECISIONES.md) | 20 decisiones técnicas con contexto, decisión, consecuencia y evidencia medida | — |
| [`ESTADO.md`](ESTADO.md) | Qué está listo, qué bloquea release, qué depende de terceros, riesgos operativos, deuda | — |
| [`FLUJOS_DE_OPERACION.md`](FLUJOS_DE_OPERACION.md) | Ciclo de 3 pasos de Ogloba, redención, activación (física + virtual + cliente), recuperación, unidades monetarias, pantallas | — |
| [`INTEGRACION_HIPOS.md`](INTEGRACION_HIPOS.md) | Contrato ICG: `apk_name`, 11 acciones, banderas de `GET_BEHAVIOR`, mapeo de extras, comprobantes, `TOTALIZATION_CANCELED` | — |
| [`OGLOBA_API_REFERENCE.md`](OGLOBA_API_REFERENCE.md) | 18 endpoints REST: schemas, headers, códigos de error, changelog, discrepancias vs Postman público | — |
| [`OPERACION_Y_SOPORTE.md`](OPERACION_Y_SOPORTE.md) | Runbook de diagnóstico: síntomas en terminal con causa raíz medida y verificación | — |
| [`PRUEBAS_Y_CERTIFICACION.md`](PRUEBAS_Y_CERTIFICACION.md) | Pruebas automatizadas + 12 escenarios manuales UAT + captura de evidencia + tabla de errores frecuentes | — |
| [`SEGURIDAD.md`](SEGURIDAD.md) | Cifrado, credenciales, superficie de ataque de intents, manifiesto, firma, cumplimiento HiOSTORE | — |
| `archivo/` | Documentos históricos: **no son fuente de verdad** | — |

### 4.3 Reglas de mantenibilidad de la documentación

1. **Un dato, un lugar.** Antes de agregar un dato, buscar si ya vive en otro documento y enlazarlo.
2. **Lo que se afirma se verifica contra el código o contra la terminal.** Cada afirmación operativa
   debe poder rastrearse a un archivo del repositorio o a una medición fechada.
3. **Los porqués van a [`DECISIONES.md`](DECISIONES.md).** Los documentos temáticos describen el
   comportamiento actual; el registro de decisiones explica por qué es así y qué se descartó.
4. **La documentación describe el sistema, no el proceso con el que se construyó.** Un lector nuevo
   no tiene acceso al backlog: un identificador de ticket no explica nada por sí solo.
5. **Sin secretos.** Contraseñas, keystores y credenciales de producción no van en Markdown; van en
   `SecureStorage`, en variables de entorno o en el gestor de secretos del equipo.

## 5. Escenarios cubiertos por el módulo

### 5.1 Escenarios productivos (cajeros + HiPOS)

| # | Escenario | Vía | Estado |
| --- | --- | --- | --- |
| S1 | Cobrar una factura con saldo de bono (`SALE` con `TenderType=CREDIT`) | Intent de HiPOS | ✅ |
| S2 | Hacer una entrada de caja con saldo de bono (`SALE` sin `TenderType`) | Intent de HiPOS | ✅ Informativo (la activación es manual) |
| S3 | Activar un bono físico nuevo (PAN + monto en rango) | UI del módulo | ✅ |
| S4 | Activar un bono virtual con entrega por correo (correo + monto) | UI del módulo | ✅ |
| S5 | Consultar el saldo de un bono | UI del módulo | ✅ |
| S6 | Activación de bono físico desde el POS (entrada de caja HiPOS) | Intent de HiPOS | ⚠️ Informa al cajero que lo active desde el módulo |
| S7 | Pago parcial cuando el bono no alcanza | Intent de HiPOS | ✅ (`FixedPaymentMeanId`) |
| S8 | Soltar la línea de pago (papelera) cuando la DIAN no integró | Intent HiPOS + `TOTALIZATION_CANCELED` | ⚠️ Aceptado, **sin devolución automática de saldo** |
| S9 | Cierre de caja (`BATCH_CLOSE`) | Intent HiPOS | ✅ `ACCEPTED` sin trabajo extra |
| S10 | Nota de crédito sobre un cobro con bono (`REFUND`) | Intent HiPOS | ❌ `FAILED` (fuera de alcance) |
| S11 | Anulación desde el POS (`VOID_TRANSACTION`) | Intent HiPOS | ⚠️ Llama a `/voidTransaction` para devolver el saldo |
| S12 | Recarga de bono (`/reload`) | — | ❌ Sin flujo (adaptador existe) |
| S13 | Facturar cuando la tienda no tiene rango DIAN (folio 109) | Intent HiPOS | ⚠️ No es del módulo — escalar al admin fiscal |

### 5.2 Escenarios del ciclo de vida

| # | Escenario | Estado |
| --- | --- | --- |
| L1 | Instalación limpia (`pm install -r`) | ✅ |
| L2 | Actualización con misma llave (`adb install -r`) | ✅ |
| L3 | Actualización con llave distinta | ⚠️ `INSTALL_FAILED_UPDATE_INCOMPATIBLE` — desinstalar (pierde config local) |
| L4 | Crash entre Step 1 y Step 2 (matar el proceso) | ✅ Recuperación al rearrancar |
| L5 | Caída de red entre Step 1 y Step 2 | ✅ Reverso automático (`/reversal`) |
| L6 | Borrado manual del APK | Pierde PIN, credenciales y cajeros — reconfigurar |
| L7 | Cambio de ambiente (Debug → Release) en la misma terminal | ✅ Conserva config (mismo package, misma firma) |

### 5.3 Escenarios administrativos

| # | Escenario | Vía |
| --- | --- | --- |
| A1 | Alta inicial de la caja (PIN, tienda, caja, contraseña Ogloba) | UI · *Configuración* |
| A2 | Registrar cajeros con contraseña | UI · *Usuarios* |
| A3 | Activar / desactivar cajero (conserva historial) | UI · *Usuarios* |
| A4 | Cambiar contraseña de Ogloba sin reinstalar | UI · *Configuración* |
| A5 | Exportar logs JSON para UAT | UI · *Admin / Exportar log* |
| A6 | Diagnóstico en terminal | ADB + `adb logcat -s TefOgloba TefOgloba.Ogloba TefOgloba.HiPos` |

## 6. Comandos habituales

### 6.1 Desarrollo

```pwsh
# Restaurar, compilar, probar
dotnet restore Permoda.Pay.sln
dotnet build   Permoda.Pay.sln -c Debug
dotnet build   Permoda.Pay.sln -c UAT
dotnet test    Permoda.Pay.sln -c Debug            # 170 pruebas, 5 suites
dotnet format  Permoda.Pay.sln                    # auto-fix
dotnet format  Permoda.Pay.sln --verify-no-changes
```

### 6.2 Empaquetado firmado

```pwsh
$env:PERMODA_KEYSTORE_PASS = '<contraseña del keystore oglobafresh>'
$env:PERMODA_KEY_PASS      = '<contraseña de la llave>'

pwsh scripts/build-apk.ps1 -Configuration UAT
pwsh scripts/build-apk.ps1 -Configuration Release
```

### 6.3 Instalación y diagnóstico

```pwsh
# Instalar o actualizar
adb -s <serial> install -r -t artifacts/com.permoda.tefogloba-Signed.apk

# Log en vivo del módulo
adb -s <serial> logcat -v threadtime -s TefOgloba TefOgloba.Ogloba TefOgloba.HiPos

# Volcado completo a archivo
adb -s <serial> logcat -d > artifacts/logcat.txt

# Qué apps responden a la acción HiPOS
adb -s <serial> shell cmd package query-activities -a icg.actions.electronicpayment.permoglobal.TRANSACTION

# Verificar versión instalada
adb -s <serial> shell dumpsys package com.permoda.tefogloba | Select-String "versionCode|versionName"
```

## 7. Arquitectura en una capa

```
                ┌─────────────────────────┐
   HiPOS ────▶  │ Permoda.Pay.Maui        │  App MAUI: Activities Android, DI,
   (Intents)    │  (net10.0-android)      │  pantallas, sesión de caja
                └───────────┬─────────────┘
                            │ referencia
         ┌──────────────────┼──────────────────────┐
         ▼                  ▼                      ▼
┌─────────────────┐  ┌──────────────────┐  ┌─────────────────────┐
│ Maui.HiPos      │  │ Infrastructure   │  │ (DI compone ambos)  │
│ Orquestador     │  │ Ogloba HTTP +     │  └─────────────────────┘
│ del contrato    │  │ SQLCipher cifrado │
└────────┬────────┘  └────────┬─────────┘
         │                    │
         └─────────┬──────────┘
                   ▼
         ┌──────────────────┐
         │ Application      │  Casos de uso + puertos
         │ (con Customer-   │  (incluye CustomerMobileNoBuilder
         │  MobileNoBuilder)│   para CC-NombreApellido)
         └────────┬─────────┘
                  ▼
         ┌──────────────────┐
         │ Domain           │  Money, identificadores, máquina de estados
         └──────────────────┘
```

Tres reglas duras de dependencia — verificadas por 3 pruebas en `Permoda.Pay.Architecture.Tests`:

1. `Domain` no referencia nada.
2. `Application` solo referencia `Domain`.
3. `Infrastructure` y `Maui.HiPos` no se conocen entre sí.

Detalle componente por componente: [`ARQUITECTURA.md`](ARQUITECTURA.md).

## 8. Ciclo de 3 pasos contra Ogloba — una imagen

```
   Step 1                    Step 2                          Step 3
┌──────────────┐    ┌────────────────────────┐    ┌──────────────────┐
│ /activation  │    │ /confirmTransaction     │    │ /reconciliation │
│ /redemption  │──▶ │   (o /cancelTransaction│──▶ │                  │
│ /reload      │    │    para descartarla)    │    │                  │
└──────┬───────┘    └────────────────────────┘    └──────────────────┘
   Requested                Confirmed                  Conciliada
       │
       └── resultado incierto (timeout / red) ──▶ /reversal ──▶ Reversed
```

Regla dura: **persistir ANTES del Step 2.** Es lo que hace que la tablet pueda morirse a mitad
de un cobro sin dejar dinero atrapado. Detalle: [`FLUJOS_DE_OPERACION.md §2`](FLUJOS_DE_OPERACION.md).

## 9. Mapa de campos del cliente en activación

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

Detalle: [`FLUJOS_DE_OPERACION.md §4`](FLUJOS_DE_OPERACION.md).

## 10. Reglas de oro (las cosas que NUNCA se tocan sin avisar)

| Valor | Quién lo controla | Por qué |
| --- | --- | --- |
| `apk_name` = `permoglobal` | ICG / CloudLicense | Cambiarlo sin actualizar el alta hace que HiPOS no encuentre el módulo, sin dejar rastro en logs |
| `ApplicationId` = `com.permoda.tefogloba` | ICG / CloudLicense | Renombrarlo desinstala y reinstala en las 512 terminales |
| `ApplicationVersion` = `1` | ICG / CloudLicense | Si no coincide con el alta, HiPOS pide reinstalar el módulo en cada arranque |
| Llave de firma | Permoda.Pay | Una rotación obliga a desinstalar en cada terminal y borra su configuración |
| `AndroidEnableMarshalMethods` = `false` | — | Apagado por fallo medido en terminal (ver D-12) |
| `PublishTrimmed` / `RunAOTCompilation` = `false` | — | Apagados por fallo medido en terminal (ver D-12) |
| Tope de 90 s para operaciones que mueven dinero | — | Cortar antes que Ogloba produce resultados inciertos (ver D-16) |
| Mensajes de error traducidos por Ogloba (`Accept-Language: es-co`) | — | Mantener una tabla local de 80+ códigos es trabajo sin valor agregado (ver D-17) |

Detalle: [`AGENTS.md` §"Qué no se toca sin coordinar"](../../AGENTS.md).

## 11. Resiliencia — qué pasa cuando algo se rompe

| Escenario | Mitigación implementada |
| --- | --- |
| App crashea entre Step 1 y Step 3 | Persistencia cifrada antes del Step 2; `RecoverPendingPaymentsHandler` retoma el paso faltante al rearrancar |
| Red cae en Step 1 | `/reversal` con el mismo `transactionNumber` |
| Red cae en Step 2 | La transacción queda `ConfirmationPending` y se reintenta en el próximo arranque |
| Step 3 falla | El pago ya es válido; la conciliación queda pendiente y se reintenta |
| Activity translúcida queda colgada | `OnDestroy` llama `HiPosFlow.Complete(null)` para que HiPOS reciba `CANCELED` |
| Tema de arranque causa flash blanco | `Ogloba.Launch` en `AndroidManifest.xml` con `windowDisablePreview=true` + fondo `#F2F2F2` |
| `Marshal methods` deja `libxamarin-app.so` inconsistente | `AndroidEnableMarshalMethods=false` (D-12) + borrar `obj/`/`bin/` si pasa |
| `AppShell` singleton entre cobros | `AppShell` y páginas son `Transient`; ViewModels son `Singleton` (D-10) |

## 12. Privacidad y cumplimiento

| Aspecto | Implementación |
| --- | --- |
| PAN en logs | Enmascarado siempre como `113817******5937` |
| Contraseña Ogloba | `SecureStorage` + Android Keystore, por tienda |
| PIN administrador | Hash SHA-256 en `SecureStorage` (no se puede recuperar) |
| Contraseñas cajeros | Hash SHA-256 en `SecureStorage`, por tienda y cajero |
| Base local | SQLite cifrado con SQLCipher (clave del Keystore) |
| Tráfico | HTTPS only — `OglobaOptions` rechaza cualquier URL no HTTPS |
| Permisos Android | Solo `INTERNET` + `ACCESS_NETWORK_STATE` |
| Marcas de terceros | Ninguna: ni en UI ni en comprobantes (cumplimiento HiOSTORE, ver D-04) |
| Broadcast de auditoría | `icg.actions.externalApi.AUDIT` mejor esfuerzo (esquema pendiente con ICG) |

Detalle: [`SEGURIDAD.md`](SEGURIDAD.md).
