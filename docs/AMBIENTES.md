# Ambientes

Este documento es la **fuente de verdad de los ambientes** del módulo: cuántos hay, contra qué
servidor de Ogloba habla cada uno, con qué datos, cómo se selecciona uno al compilar y cómo se
verifica cuál está corriendo en una terminal.

Regla base: **el ambiente no se configura en runtime.** No hay archivo de configuración, ni
variable de entorno, ni pantalla que lo cambie. Lo fija la **configuración de compilación** de
MSBuild mediante una constante de precompilación, y queda grabado en la APK. Una APK de UAT no
puede apuntarse a producción, y una de producción no puede apuntarse al sandbox.

## 1. Los tres ambientes

| Ambiente | Configuración MSBuild | Constante | Nombre en runtime | Servidor Ogloba |
| --- | --- | --- | --- | --- |
| Desarrollo | `Debug` | `ENVIRONMENT_DEVELOPMENT` | `Development` | `https://co-ts.ogloba.com/gc-restful-gateway/giftCardService` |
| UAT / certificación | `UAT` | `ENVIRONMENT_UAT` | `UAT` | `https://co-ts.ogloba.com/gc-restful-gateway/giftCardService` |
| Producción | `Release` | `ENVIRONMENT_PRODUCTION` | `Production` | `https://co-prod.ogloba.com/gc-restful-gateway/giftCardService` |

Definición: [`src/Permoda.Pay.Maui/Configuration/AppEnvironment.cs`](../src/Permoda.Pay.Maui/Configuration/AppEnvironment.cs).
Declaración de las constantes: [`src/Permoda.Pay.Maui/Permoda.Pay.Maui.csproj`](../src/Permoda.Pay.Maui/Permoda.Pay.Maui.csproj)
(`<Configurations>Debug;UAT;Release</Configurations>`).

Dos advertencias sobre la URL:

* El segmento `/gc-restful-gateway` **es parte del path**, no un prefijo opcional. Quitarlo produce
  404 en todos los endpoints.
* `srl-ts.ogloba.com`, que aparece en la colección Postman pública de Ogloba, es un **tenant demo
  ajeno a KOAJ**. No se usa en ningún ambiente de este módulo.

## 2. Matriz completa por ambiente

| Parámetro | `Debug` (Development) | `UAT` | `Release` (Production) |
| --- | --- | --- | --- |
| Host Ogloba | `co-ts.ogloba.com` | `co-ts.ogloba.com` | `co-prod.ogloba.com` |
| `X-WSRG-API-Version` | `2.18` | `2.18` | `2.18` |
| Producto digital (bono virtual) | `113816` | `113816` | `113811` — **por confirmar** |
| Autocompletado de sandbox | Sí (tienda `K00037`) | Sí (tienda `K00037`) | No (`Sandbox = null`) |
| Distintivo en la UI | "Sandbox de pruebas · co-ts" | "Sandbox de pruebas · co-ts" | "Producción · co-prod" |
| Firma de la APK | Sí, misma llave | Sí, misma llave | Sí, misma llave |
| Símbolos de depuración | Sí | No | No |
| Optimización | No | Sí | Sí |
| Inflado de XAML | `SourceGen` (permite Hot Reload) | Compilado | Compilado |
| Trimming / AOT | No | No (`AndroidLinkMode=None`) | No (`AndroidLinkMode=None`) |

### Por qué la versión de API es `2.18` en los tres ambientes

Ogloba fijó `2.18` para el tenant de KOAJ por correo, y así está verificado en vivo contra `co-ts`
(`/activation`, `/redemption`, `/balance`, `/getProducts`). La colección Postman pública muestra
`2.20`, pero corresponde a otro tenant. **No subir la versión sin confirmación escrita de Ogloba
para `co-prod`.** Valor en
[`OglobaOptions.DefaultApiVersion`](../src/Permoda.Pay.Infrastructure/Ogloba/OglobaOptions.cs).

### Por qué el código de producto digital cambia entre ambientes

El bono **virtual** se emite con el módulo de Order management de Ogloba, que exige un `itemCode`
del catálogo de la tienda:

* Sandbox: `113816` (*TARJETA OBSEQUIO B2C*), verificado contra `co-ts` con la tienda `K00037`.
  Rango aceptado: $30.000 – $500.000.
* Sandbox descartado: `113815` (*BONO REGALO B2B*) rechaza **cualquier** monto con `errorCode 73`,
  incluido el mínimo de su propio catálogo.
* Producción: hoy declarado como `113811` (*KOAJ Dotacion B2B*). Es un producto de dotación
  corporativa, no el bono de regalo B2C que se eligió para el cajero. **Pendiente**: confirmar con
  Ogloba el `itemCode` de `co-prod` equivalente a `113816` antes de salir a producción.

Los bonos **físicos** no usan este código: viajan con su propio PAN (producto `113817` en sandbox).

### Tiempos de espera (iguales en los tres ambientes)

| Tipo de operación | Tope | Motivo |
| --- | --- | --- |
| Operaciones que mueven dinero (`/activation`, `/redemption`, `/confirmTransaction`, `/reversal`, `/reconciliation`) | **90 s** | El manual de Ogloba concede ~90 s a una redención. Cortar antes deja resultados inciertos: la operación puede haberse aplicado del lado de Ogloba y no saberlo acá. **No bajar este valor.** |
| Operaciones de solo lectura (`/balance`, `/getProducts`, `/getBuInfo`, `/test`) | **15 s** | Una consulta que falla no deja nada a medias, y es la latencia que el cajero siente con el cliente en el mostrador. |

Definición: [`OglobaOptions`](../src/Permoda.Pay.Infrastructure/Ogloba/OglobaOptions.cs).

## 3. Credenciales por tienda

La autenticación con Ogloba es **Basic Auth por tienda**, no por aplicación:

* **Usuario** = Store ID de la tienda (formato `K#####`, p. ej. `K00037`).
* **Contraseña** = passphrase que asigna Ogloba.

Se guarda cifrada en `SecureStorage` del dispositivo, nunca en el código ni en el repositorio (ver
[SEGURIDAD.md](SEGURIDAD.md)). Llega a la terminal por dos caminos:

1. **Productivo**: HiPOS la envía en el XML `Parameters` del intent `INITIALIZE`, y el módulo la
   persiste (ver [INTEGRACION_HIPOS.md](INTEGRACION_HIPOS.md)).
2. **Manual**: el administrador la escribe en la pantalla *Configuración* del módulo.

En builds no productivos, `AppEnvironment.Sandbox` expone una pista de autocompletado (tienda de
prueba `K00037` y la passphrase compartida del sandbox) para poder configurar una terminal de un
toque durante las pruebas. En `Release` esa propiedad es `null` y el botón de autocompletado no
existe.

**Pendiente de definición con Ogloba**: si en producción la passphrase es única por tienda o común
a las 512. Cambia el procedimiento de despliegue (ver [ESTADO.md](ESTADO.md)).

## 4. Identidad de la app (igual en los tres ambientes)

| Parámetro | Valor | Quién lo define |
| --- | --- | --- |
| `ApplicationId` (package Android) | `com.permoda.tefogloba` | Permoda |
| `AssemblyName` / `ApplicationTitle` | `TefOgloba` | Permoda |
| `apk_name` (llave de enrutamiento de HiPOS) | `permoglobal` | ICG, en el alta de CloudLicense |
| `ApplicationVersion` (código de versión) | `1` | Debe coincidir con el alta en CloudLicense |
| `ApplicationDisplayVersion` | `1.0.0` | Permoda |
| Android mínimo | API 33 (Android 13) | Hardware homologado por HiPOS |

No hay variantes de package ni sufijos por ambiente: **las tres builds se instalan en el mismo
package**. Instalar una APK de UAT sobre una de producción reemplaza la app y conserva la
configuración (misma llave de firma), pero cambia el servidor contra el que transacciona. Ver
[BUILD_Y_DESPLIEGUE.md](BUILD_Y_DESPLIEGUE.md).

## 5. Cómo verificar qué ambiente corre en una terminal

Tres comprobaciones, de la más rápida a la más concluyente:

1. **En la UI**: el encabezado del módulo muestra el distintivo del ambiente y el host corto
   (`co-ts` o `co-prod`), a partir de `AppEnvironment.DisplayName` y `AppEnvironment.Host`.
2. **En el log**: cada llamada a Ogloba se registra en `logcat` bajo la etiqueta
   `TefOgloba.Ogloba`, con la ruta invocada. El host aparece en la línea de la petición.

   ```pwsh
   adb logcat -d -s TefOgloba.Ogloba
   ```
3. **En la APK**: `aapt2 dump badging <apk>` confirma package y versión, pero **no** el ambiente.
   El ambiente solo se distingue por el host al que llama en runtime (punto 2) o por el
   distintivo de la UI (punto 1). Por eso las APK deben archivarse con el nombre de la
   configuración con la que se compilaron.

## 6. Ambientes de terceros que dependen de esto

| Sistema | Ambiente de sandbox | Ambiente de producción | Notas |
| --- | --- | --- | --- |
| Ogloba GiftCard | `co-ts.ogloba.com` | `co-prod.ogloba.com` | 512 tiendas KOAJ precargadas en el back office (`gcap`) |
| HiPOS / CloudLicense (ICG) | — | Alta única de "Permoda Pay Oglobal" con `apk_name=permoglobal` | No hay separación de ambientes: el alta es una sola y aplica a cualquier APK que se instale en la terminal |
| Backend fiscal DIAN (`hioposreports-co`) | — | Lo consume HiPOS, no el módulo | El módulo solo aporta `AuthorizationId`; los folios y la numeración son de HiPOS |

La consecuencia práctica del alta única de CloudLicense: **una terminal con la APK de UAT
instalada transacciona contra el sandbox de Ogloba pero factura con la configuración fiscal
real de la tienda.** No dejar APK de UAT en cajas productivas.
