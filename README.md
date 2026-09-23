# Permoda.Pay Ogloba (TefOgloba)

> **Módulo de Cobro Electrónico (TEF) que permite a las 512 tiendas KOAJ en Colombia aceptar
> bonos Ogloba GiftCard como medio de pago dentro del POS HiPOS de ICG.**

Una sola APK Android escrita en C# con .NET 10 MAUI. HiPOS la invoca por `Intent` cuando el cajero
elige el medio de pago **Ogloba**; el módulo cobra contra la API REST de Ogloba y le devuelve al POS
el resultado y el comprobante para imprimir. Expone además una UI propia para las operaciones que
no caben dentro de un intent (activación de bonos, consulta de saldo, bitácora, configuración de
la caja).

## 1. Identidad y posicionamiento

| Campo | Valor |
| --- | --- |
| Cliente | KOAJ · Permoda Ltda · Colombia |
| POS anfitrión | HiPOS (ICG) — contrato "API de Desarrollo de un Módulo de Cobro Electrónico 4.0" |
| Pasarela de pagos | Ogloba GiftCard REST API · `X-WSRG-API-Version: 2.18` · Basic Auth por tienda |
| Plataforma destino | Android 13+ (`SupportedOSPlatformVersion = 33`), tablets homologadas por HiPOS |
| Stack técnico | .NET 10, .NET MAUI, xUnit, SQLCipher, Android Keystore, SecureStorage |
| Identidad de la app | `ApplicationId = com.permoda.tefogloba` · `AssemblyName = TefOgloba` |
| Llave de enrutamiento HiPOS | `apk_name = permoglobal` (alta de ICG en CloudLicense) |
| Arquitectura | Clean Architecture + Puertos y Adaptadores + DI nativa de MAUI |
| Ambientes | `Debug` → sandbox · `UAT` → sandbox · `Release` → producción |
| Datos sensibles | Cifrado en reposo con SQLCipher + Android Keystore; HTTPS only |

## 2. Capacidades del módulo

| Capacidad | Estado | Verificación |
| --- | --- | --- |
| Redención como medio de pago de HiPOS (`SALE` / `NEGATIVE_SALE` con `TenderType` declarado) | Implementado | Pruebas + terminal |
| Ciclo obligatorio de 3 pasos contra Ogloba con persistencia antes del Step 2 | Implementado | Pruebas de `ProcessSaleHandler` |
| Reverso técnico por timeout/red (`/reversal`) | Implementado | Pruebas |
| Recuperación de pagos pendientes al arrancar | Implementado | Pruebas |
| Activación de bono físico (`/activation` → `/confirm` → `/reconciliation`) | Implementado | Sandbox |
| Activación de bono virtual (`/orderCreation` → `/orderConfirm`) | Implementado | Sandbox, producto `113816` |
| Captura de datos del cliente en activación (cédula + nombre → concatenados `CC-NombreApellido` en `note`/`message`) | Implementado | Pruebas del helper `CustomerMobileNoBuilder` |
| Consulta de saldo (`/balance`) | Implementado | Sandbox |
| Pago parcial cuando el bono no cubre la factura | Implementado | Pruebas |
| Pago único, sinMerchantReceipt (un solo comprobante, el del cliente) | Implementado | Terminal |
| Anulación de un cobro mediante papelera (`TOTALIZATION_CANCELED`) | Implementado, **sin devolución de saldo** | Terminal |
| Cierre de caja (`BATCH_CLOSE`) | Implementado | Terminal |
| Configuración automática por `INITIALIZE` de CloudLicense | Implementado | Pruebas |
| Registro de cajeros con contraseña, activación y desactivación | Implementado | — |
| PIN de administrador | Implementado | — |
| Bitácora del turno + exportador de logs JSON para UAT | Implementado | — |

**Fuera de alcance por decisión de producto (no son deuda pendiente)**: notas de crédito,
anulación de un cobro con bono desde el POS, recarga de bonos, devolución parcial de un cobro,
modo multi-marca o multi-proveedor, devolución automática de saldo al cancelar una totalización.

## 3. Arranque rápido

### 3.1 Compilar y probar

```pwsh
dotnet restore Permoda.Pay.sln
dotnet build   Permoda.Pay.sln -c Debug
dotnet test    Permoda.Pay.sln -c Debug     # 170 pruebas, 5 suites
dotnet format  Permoda.Pay.sln --verify-no-changes
```

### 3.2 Generar la APK firmada

```pwsh
$env:PERMODA_KEYSTORE_PASS = '<contraseña del keystore oglobafresh>'
$env:PERMODA_KEY_PASS      = '<contraseña de la llave>'

pwsh scripts/build-apk.ps1 -Configuration UAT
pwsh scripts/build-apk.ps1 -Configuration Release
```

### 3.3 Instalar en una terminal

```pwsh
adb -s <serial> install -r -t artifacts/com.permoda.tefogloba-Signed.apk
```

Detalle en [`docs/BUILD_Y_DESPLIEGUE.md`](docs/BUILD_Y_DESPLIEGUE.md).

## 4. Cómo está organizado el repositorio

```text
Permoda.Pay.sln          Solución .NET 10 con 5 proyectos + 5 suites de pruebas

src/
├── Permoda.Pay.Domain         Dinero, identificadores, máquina de estados del pago (sin I/O)
├── Permoda.Pay.Application    Casos de uso + puertos (sin detalles de infraestructura)
│                              Incluye CustomerMobileNoBuilder (CC-NombreApellido)
├── Permoda.Pay.Infrastructure Adaptador HTTP de Ogloba + repositorio SQLCipher cifrado
├── Permoda.Pay.Maui.HiPos     Orquestador HiPOS, mappers, comprobantes, saneo DIAN
│                              (sin referencias a tipos de Android — testeable sin emulador)
└── Permoda.Pay.Maui           App MAUI: DI, Activities Android, pantallas, sesión de caja

tests/
├── Permoda.Pay.Domain.Tests         37  Invariantes de la máquina de estados
├── Permoda.Pay.Application.Tests    44  Casos de uso, recuperación, CustomerMobileNoBuilder
├── Permoda.Pay.Infrastructure.Tests  22  Contrato Ogloba y persistencia cifrada
├── Permoda.Pay.Maui.Tests           73  Mapeo y composición de respuestas HiPOS
└── Permoda.Pay.Architecture.Tests    3  Dirección de las dependencias entre capas

docs/                          Documentación técnica (fuente de verdad)
├── 00-COMPENDIO.md             Compendio maestro, índice navegable
├── AMBIENTES.md                Debug / UAT / Release, URLs, productos, timeouts
├── ARQUITECTURA.md             Capas, puertos, adaptadores, dependencias
├── BUILD_Y_DESPLIEGUE.md       Compilar, firmar, instalar, alta en CloudLicense
├── DECISIONES.md               Registro de decisiones técnicas con evidencia
├── ESTADO.md                   Qué está listo, qué falta, qué bloquea el release
├── FLUJOS_DE_OPERACION.md      Ciclo de 3 pasos, activación, redención, recuperación, unidades
├── INTEGRACION_HIPOS.md        Contrato ICG: apk_name, acciones, extras, comprobantes
├── OGLOBA_API_REFERENCE.md     18 endpoints REST, schemas, códigos de error, changelog
├── OPERACION_Y_SOPORTE.md      Runbook: diagnóstico con ADB/logcat, síntomas y causas
├── PRUEBAS_Y_CERTIFICACION.md  Pruebas automatizadas + 12 escenarios manuales UAT
└── SEGURIDAD.md                Cifrado, cumplimiento HiOSTORE, postura de la firma

scripts/                       Empaquetado y firma de la APK
assets/keystore/               Material de firma (fuera de git)
artifacts/                     APKs firmadas generadas (fuera de git)

DESIGN.md                      Sistema de diseño de la UI (paleta azul Koaj, Roboto, Scaffold)
PRODUCT.md                     Definición de producto: usuarios, alcance, principios
CHANGELOG.md                    Bitácora de cambios por versión
AGENTS.md                      Convenciones para contribuir al repositorio
```

## 5. Documentación — por dónde empezar

El índice completo está en **[`docs/00-COMPENDIO.md`](docs/00-COMPENDIO.md)**. Los cuatro documentos
con los que casi siempre se empieza:

| Necesito… | Documento |
| --- | --- |
| Saber contra qué servidor y con qué datos corre cada build | [`docs/AMBIENTES.md`](docs/AMBIENTES.md) |
| Entender cómo HiPOS llama al módulo y qué se le responde | [`docs/INTEGRACION_HIPOS.md`](docs/INTEGRACION_HIPOS.md) |
| Entender el ciclo de vida de un cobro contra Ogloba | [`docs/FLUJOS_DE_OPERACION.md`](docs/FLUJOS_DE_OPERACION.md) |
| Compilar, firmar, instalar y actualizar la APK | [`docs/BUILD_Y_DESPLIEGUE.md`](docs/BUILD_Y_DESPLIEGUE.md) |
| Diagnosticar un problema en una terminal en producción | [`docs/OPERACION_Y_SOPORTE.md`](docs/OPERACION_Y_SOPORTE.md) |
| Saber por qué se hizo algo y qué se descartó | [`docs/DECISIONES.md`](docs/DECISIONES.md) |
| Entender la arquitectura de capas y los puertos | [`docs/ARQUITECTURA.md`](docs/ARQUITECTURA.md) |
| Conocer los pendientes, su riesgo y su dueño | [`docs/ESTADO.md`](docs/ESTADO.md) |
| Ejecutar la batería de pruebas de aceptación | [`docs/PRUEBAS_Y_CERTIFICACION.md`](docs/PRUEBAS_Y_CERTIFICACION.md) |
| Auditar la postura de seguridad y cumplimiento | [`docs/SEGURIDAD.md`](docs/SEGURIDAD.md) |

## 6. Convenciones del repositorio

Para contribuir: [`AGENTS.md`](AGENTS.md). Reglas críticas:

* **Las dependencias apuntan hacia adentro.** Domain no conoce a nadie; Application solo conoce
  Domain; Infrastructure y Maui.HiPos no se conocen entre sí. Hay 3 pruebas en
  `Permoda.Pay.Architecture.Tests` que lo verifican.
* **Los fallos de negocio son valores, no excepciones.** `Result` en dominio, `PortResult` en
  aplicación. Un rechazo de Ogloba es un retorno, no un `throw`.
* **`Permoda.Pay.Maui.HiPos` no referencia tipos de Android.** Eso permite probar el contrato del
  POS sin dispositivo.
* **Ningún dato sensible sale en logs.** PAN enmascarado siempre (`113817******5937`); nunca
  contraseñas, PIN ni passphrase.
* **Las rutas del Shell son constantes** de `Permoda.Pay.Maui/Common/AppRoutes.cs`, nunca literales
  en XAML.
* **Los colores y espaciados salen de los tokens** de `Resources/Styles/`, nunca hex en línea
  ([`DESIGN.md`](DESIGN.md)).
* **Todo handler de un intent responde y cierra.** Un camino que termina sin resultado deja a HiPOS
  esperando indefinidamente y el cajero pierde la venta.

## 7. Estado verificado

Verificado el **2026-09-02** sobre el árbol de trabajo actual:

* **Pruebas**: 170 pruebas automatizadas, todas en verde
  (Domain 37 · Application 44 · HiPOS/MAUI 73 · Infrastructure 22 · Arquitectura 3).
* **Formatos**: `dotnet format --verify-no-changes` (con la salvedad documentada en
  [`docs/ESTADO.md`](docs/ESTADO.md) §P16).
* **Build reproducible**: Debug y UAT verificados. La APK firmada Release se emite con
  `ogloba-fresh.keystore` (CN=Ogloba · RSA 2048 · SHA384withRSA).
* **Hardware certificado**: pendiente de certificación formal por KOAJ en hardware homologado.
* Los pendientes abiertos, con su dueño y su riesgo: [`docs/ESTADO.md`](docs/ESTADO.md).

## 8. Contactos

| Rol | Persona | Email |
| --- | --- | --- |
| Solicitante original (KOAJ / Permoda) | Luis Felipe Quintero Mejía | `luisfqm@permoda.com.co` |
| Project lead KOAJ / Permoda (firmó HiOSTORE) | Roger Moreno | `rogerm@permoda.com.co` |
| Soporte Ogloba | Marcus Lin | `team.support@ogloba.com` |
| Customer Success Ogloba | Maria Andrea | `maa@ogloba.com` |
