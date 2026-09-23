# Seguridad y cumplimiento

Postura de seguridad del módulo: qué se protege, con qué mecanismo y dónde está implementado.

## 1. Principios

1. **Sin secretos en el repositorio.** Ni keystores, ni contraseñas, ni credenciales de producción.
   `.gitignore` excluye `assets/keystore/`, `*.keystore`, `*.jks`, `*.pfx`, `*.p12`, `*.env` y
   `secrets/`.
2. **Sin datos de tarjeta en claro.** Ni en pantalla, ni en logs, ni en la APK.
3. **Cifrado en reposo por defecto.** Toda transacción pendiente y toda credencial viven cifradas.
4. **Solo HTTPS.** El tráfico en claro está deshabilitado a nivel de manifiesto.
5. **Permisos mínimos.** Dos, y ambos justificables.

## 2. Datos sensibles y su protección

| Dato | Dónde vive | Protección |
| --- | --- | --- |
| Contraseña de Ogloba (Basic Auth por tienda) | `SecureStorage` de Android | Cifrado respaldado por el Keystore del dispositivo. Se guarda en Base64, por tienda |
| Clave de cifrado de la base local | `SecureStorage` (`tefogloba.database-key.v1`) | Generada con `RandomNumberGenerator` en el primer arranque; el buffer en memoria se limpia con `CryptographicOperations.ZeroMemory` |
| Transacciones pendientes | SQLite cifrado con SQLCipher | `PRAGMA key` con la clave anterior en hexadecimal |
| PIN de administrador | `SecureStorage`, solo el hash | No se puede recuperar; si se pierde, hay que reinstalar |
| Contraseñas de cajeros | `SecureStorage`, por tienda | Un cajero desactivado conserva su contraseña sin poder entrar |
| Configuración de la tienda | `SecureStorage` (XML en Base64) | Tienda, caja, URL, versión de API, nombre del comercio |
| PAN del bono | Nunca completo en logs ni en pantalla | `CardIdentifier.MaskedValue` → `113817******5937` |

**Única excepción al enmascaramiento**: el serial de un bono **recién activado** se imprime completo
en el comprobante del cliente. En un bono virtual es la única prueba física con la que el cliente
puede redimirlo después; enmascararlo lo dejaría inservible. Ver
[INTEGRACION_HIPOS.md](INTEGRACION_HIPOS.md#8-comprobantes).

### Configuración de SQLCipher

[`SqlCipherPendingPaymentRepository`](../src/Permoda.Pay.Infrastructure/Persistence/SqlCipherPendingPaymentRepository.cs)
abre cada conexión con:

| PRAGMA | Valor | Por qué |
| --- | --- | --- |
| `key` | clave en hex desde `SecureStorage` | Cifrado de la base |
| `cipher_version` | se verifica que responda | Si SQLCipher no está disponible, se falla explícito en lugar de escribir una base en claro |
| `cipher_memory_security` | `ON` | Limpia la memoria de trabajo del cifrado |
| `busy_timeout` | `5000` | Evita `SQLITE_BUSY` cuando la Activity y la recuperación tocan la base a la vez |
| `journal_mode` | `WAL` | Escrituras concurrentes y recuperación tras corte abrupto |

## 3. Superficie de ataque de los intents

El módulo es una app **exportada**: cualquier app del dispositivo podría intentar invocarla. Las
defensas:

* **Validación del paquete llamante.** `HiPosPaymentActivity` solo acepta intents de
  `icg.android.start` y `com.icg.hiopos`. En `Debug` también acepta `com.android.shell` para poder
  probar con `adb shell am start`. En `Release` cualquier otro llamante se rechaza.
* **`<queries>` en el manifiesto.** Declara esos dos paquetes; sin eso, en Android 11+
  `PackageManager` devuelve listas vacías y `getCallingPackage()` no se puede resolver — la
  validación se quedaría sin nada con lo que comparar.
* **Nada de datos sensibles en la respuesta.** El extra `CardNum` va enmascarado; el
  `AuthorizationId` es la referencia de Ogloba, no un dato de tarjeta.

## 4. Configuración del manifiesto

| Ajuste | Valor | Efecto |
| --- | --- | --- |
| `usesCleartextTraffic` | `false` | Ningún HTTP en claro, ni por error de configuración |
| `allowBackup` | `false` | Sin extracción automática de datos por ADB o por copia de seguridad de Google |
| `uses-permission` | `INTERNET`, `ACCESS_NETWORK_STATE` | Los dos únicos permisos: llamar a Ogloba y detectar si hay red |

Además, `OglobaOptions` **rechaza en el constructor** cualquier URL base que no sea HTTPS absoluta:
la restricción está en el código, no solo en la configuración de red de Android.

## 5. Firma de la APK

Las **tres** configuraciones se firman con la **misma llave**. Android exige que toda actualización
venga firmada con la misma llave que la instalación previa: si `Debug` usara la llave de
depuración, pasar de una build de prueba a una de producción en una terminal obligaría a
desinstalar, perdiendo configuración de tienda, PIN y cajeros.

Las contraseñas se leen de las variables de entorno `PERMODA_KEYSTORE_PASS` y `PERMODA_KEY_PASS`.
Si faltan, el proyecto **no firma con esa llave** en lugar de romper el build de quien clone el
repositorio sin acceso a ella.

### Estado del material de firma

`assets/keystore/` contiene cinco archivos, resultado de tres rotaciones. **Solo uno es válido**, y
hoy las tres fuentes del repositorio no coinciden entre sí:

| Fuente | Keystore que apunta |
| --- | --- |
| `src/Permoda.Pay.Maui/Permoda.Pay.Maui.csproj` (el que usa el build) | `ogloba-fresh.keystore`, alias `ogloba` |
| `scripts/build-apk.ps1` (parámetro por defecto) | `ogloba.keystore`, alias `ogloba` |
| `assets/keystore/Hashes.txt` | `ogloba.keystore` como "vigente" |

> **Discrepancia abierta — resolver antes del próximo empaquetado firmado.** El comentario del
> `.csproj` indica que la contraseña de `ogloba.keystore` se perdió y que por eso se creó
> `ogloba-fresh.keystore`; si eso es correcto, el valor por defecto del script y `Hashes.txt` están
> obsoletos y hay que actualizarlos, además de entregar a ICG la huella SHA-1 de la llave nueva.
> Registrado en [ESTADO.md](ESTADO.md).

Keystores retirados y por qué:

| Archivo | Motivo del retiro |
| --- | --- |
| `tefogloba.keystore` | Alias `sistecredito` y DN `CN=SistecreditoTEF`: nombraba al módulo TEF de otro proveedor y no había constancia de quién más custodiaba la llave. Quien tenga una llave puede firmar un APK con nuestro package y suplantar el módulo |
| `pos2pay-release.keystore` (y su `.bak`) | No abre con ninguna contraseña conocida. Hacía fallar `apksigner` |
| `ogloba.keystore` | Según el `.csproj`, contraseña perdida: sin poder firmar no hay forma de actualizar las terminales |

**Consecuencia de cualquier rotación de llave**: Android rechaza una actualización firmada con
llave distinta (`INSTALL_FAILED_UPDATE_INCOMPATIBLE`). La primera instalación tras un cambio de
llave exige **desinstalar**, lo que borra PIN, credenciales de Ogloba y cajeros de esa caja. Ver
[BUILD_Y_DESPLIEGUE.md](BUILD_Y_DESPLIEGUE.md).

## 6. Registro y auditoría

| Registro | Contenido | Dónde |
| --- | --- | --- |
| Tráfico con Ogloba | Ruta, tienda, caja, importe, PAN **enmascarado**, resultado | `logcat`, etiqueta `TefOgloba.Ogloba`, y `IOglobaTrafficLog` en memoria |
| Auditoría de transacciones | Eventos de la máquina de estados con referencia y número de transacción | `ITransactionAudit` |
| Actividad del turno | Operaciones que hizo el cajero | `CashierActivityLog`, visible en la pantalla *Bitácora* |
| Arranque de la app | Marcadores de fases | `logcat`, etiqueta `TefOgloba` |

Ningún registro contiene contraseñas, PIN, PAN completo ni la passphrase de Ogloba. Los registros
en memoria se pierden al terminar el proceso: **no hay persistencia de logs de negocio**, solo la
base de transacciones pendientes.

## 7. Cumplimiento HiOSTORE

Compromisos firmados en la solicitud HiOSTORE de ICG y dónde se cumplen:

| Regla | Dónde se cumple | Estado |
| --- | --- | --- |
| La app no debe fallar; los defectos se corrigen a la brevedad | Persistencia antes del Step 2, handler de recuperación, resultados explícitos en lugar de excepciones, 170 pruebas automatizadas | Cumplido |
| Metadatos claros y veraces | `ApplicationId = com.permoda.tefogloba`, nombre del módulo `Ogloba` respondido en `GET_CUSTOM_PARAMS` | Cumplido |
| Sin marcas de terceros | Ni logos ni nombres de Ogloba, ICG, HiPOS o Sistecrédito en la UI ni en los comprobantes | Cumplido |
| Almacenamiento cifrado | SQLCipher + `SecureStorage`/Keystore | Cumplido |
| Permisos mínimos justificados | `INTERNET` + `ACCESS_NETWORK_STATE` | Cumplido |
| Sin funciones ocultas ni publicidad cruzada | No hay código que instale, promocione ni invoque otras apps | Cumplido |
| Broadcast de auditoría en cada operación | `icg.actions.externalApi.AUDIT` (mejor esfuerzo; el esquema de extras no está especificado en el contrato) | Pendiente de confirmar con ICG |
| Funciona en hardware certificado por HiPOS | Android API 33, probado en tablet C9H | Pendiente de certificación formal de KOAJ |

### Sobre la regla de marcas de terceros

Se aplica literalmente: el nombre del botón en el POS es `Ogloba` porque es el medio de pago que el
cajero necesita reconocer, pero **no se usa el logotipo de Ogloba** ni el de ICG/HiPOS en pantallas
ni comprobantes. El espacio de marca del encabezado está reservado para Permoda/KOAJ. Ver
[DESIGN.md](../DESIGN.md).

## 8. Lo que este módulo no hace

Aclaraciones para evitar suposiciones en una auditoría:

* **No almacena datos de tarjetas de crédito ni débito.** No opera tarjetas bancarias: `READ_CARD`,
  `CHARGE_CARD` y `GET_CARD_DATA` se responden `RESULT_CANCELED`. No hay alcance PCI-DSS por esta
  vía.
* **No emite numeración fiscal.** El consecutivo, el rango y la resolución DIAN son de HiPOS y de
  `icg.hioposapifiscal`. El módulo solo aporta el `AuthorizationId`.
* **No expone servicios de red entrantes.** No hay servidor HTTP, ni receptor de webhooks, ni
  puerto abierto. Todo el tráfico es saliente hacia Ogloba.
* **No sincroniza con ningún backend de Permoda.** La única integración remota es Ogloba.
