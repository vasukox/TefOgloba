# Build y despliegue

Del código fuente a una APK firmada corriendo dentro de HiPOS en una terminal.

## 1. Requisitos

| Herramienta | Versión | Para qué |
| --- | --- | --- |
| .NET SDK | 10.x, con la carga de trabajo `maui-android` | Compilar |
| PowerShell | 7.0 o superior | `scripts/build-apk.ps1` lo exige |
| Android SDK | Plataforma API 33 o superior, con `adb` y `aapt2` | Instalar y diagnosticar |
| JDK | El que trae el workload de Android | `apksigner` |

Material de firma: el keystore en `assets/keystore/` (fuera de git) y las dos contraseñas en
variables de entorno. Ver [SEGURIDAD.md](SEGURIDAD.md#5-firma-de-la-apk).

## 2. Comandos de desarrollo

Todo se ejecuta desde la raíz del repositorio.

```pwsh
# Restaurar y compilar
dotnet restore Permoda.Pay.sln
dotnet build   Permoda.Pay.sln -c Debug
dotnet build   Permoda.Pay.sln -c UAT

# Pruebas (170 pruebas en 5 suites)
dotnet test Permoda.Pay.sln -c Debug

# Formato y estilo — obligatorio antes de hacer commit
dotnet format Permoda.Pay.sln
dotnet format Permoda.Pay.sln --verify-no-changes
```

Las tres configuraciones válidas son `Debug`, `UAT` y `Release`. Cada una fija el ambiente en tiempo
de compilación: ver [AMBIENTES.md](AMBIENTES.md).

## 3. Empaquetado de la APK firmada

```pwsh
$env:PERMODA_KEYSTORE_PASS = '<contraseña del keystore>'
$env:PERMODA_KEY_PASS      = '<contraseña de la llave>'

pwsh scripts/build-apk.ps1 -Configuration UAT
pwsh scripts/build-apk.ps1 -Configuration Release
```

El script restaura, compila la solución, publica el proyecto MAUI para `net10.0-android` y deja las
APK en `artifacts/`. Para `UAT` y `Release` valida **antes de empezar** que el keystore exista y que
las dos contraseñas estén definidas: falla de inmediato en lugar de producir un APK sin firmar.

> Verificar qué keystore se está usando antes de empaquetar: hoy el `.csproj` y el script apuntan a
> archivos distintos. Discrepancia documentada en
> [SEGURIDAD.md](SEGURIDAD.md#estado-del-material-de-firma).

Parámetros del script que se pueden sobreescribir: `-KeystorePath`, `-KeystoreAlias`,
`-KeystorePassword`, `-KeyPassword`, `-OutputDirectory`.

### Ajustes de compilación que no se deben "optimizar"

Están apagados a propósito, y cada uno tiene una falla concreta detrás:

| Ajuste | Valor | Qué pasa si se cambia |
| --- | --- | --- |
| `AndroidEnableMarshalMethods` | `false` | Con `true`, las compilaciones **incrementales** dejan `libxamarin-app.so` con el registro JNI de la compilación anterior. La app muere en el arranque con `UnsatisfiedLinkError: No implementation found for … n_onCreate()`, antes de ejecutar una línea propia, y desde HiPOS se ve como "el módulo no se puede iniciar". Medido el 2026-08-28: tres builds incrementales seguidas produjeron APK que crashean; borrar `obj/` y `bin/` produjo una sana en cada caso |
| `PublishTrimmed` / `AndroidLinkMode` | `false` / `None` | El inflado de XAML crea vistas por reflexión (p. ej. `StatusBannerView`) y el *linker* eliminaba su constructor: `Arg_NoDefCTor` al abrir la app |
| `RunAOTCompilation` | `false` | `dotnet publish` para Android lo activa por defecto en Release, pero exige `PublishTrimmed=true` (`XA1030`) — que está apagado arriba. Sin esto, `publish -c Release` no llega ni a firmar |
| `MauiXamlInflator` | `SourceGen` solo en `Debug` | Es lo que permite XAML Hot Reload. En `UAT`/`Release` se usa el inflador compilado, seguro frente al trimming |

Si aparece un crash de arranque inexplicable tras varias compilaciones, **borrar `obj/` y `bin/` y
recompilar** antes de buscar la causa en el código.

## 4. Instalación en una terminal

```pwsh
# Ver los dispositivos conectados
adb devices

# Instalar o actualizar
adb -s <serial> install -r -t artifacts/com.permoda.tefogloba-Signed.apk

# Confirmar que quedó instalado
adb -s <serial> shell pm list packages | Select-String tefogloba
```

### Cuando la actualización se rechaza

| Error | Causa | Solución |
| --- | --- | --- |
| `INSTALL_FAILED_UPDATE_INCOMPATIBLE` | El APK está firmado con una llave distinta a la instalada | Desinstalar y volver a instalar. **Se pierden PIN, credenciales de Ogloba y cajeros de esa caja**: hay que reconfigurarla |
| `INSTALL_FAILED_VERSION_DOWNGRADE` | `ApplicationVersion` menor que el instalado | Subir la versión o desinstalar |

```pwsh
# Desinstalación completa (borra toda la configuración de la caja)
adb -s <serial> uninstall com.permoda.tefogloba
```

## 5. Alta en CloudLicense (ICG)

El módulo **no funciona solo por estar instalado**. HiPOS lo invoca por una acción que contiene el
`apk_name` registrado por ICG en CloudLicense; si el alta y el manifiesto no coinciden, HiPOS lo da
por no instalado y entra en un bucle de reinstalación en cada arranque
([INTEGRACION_HIPOS.md](INTEGRACION_HIPOS.md#1-cómo-encuentra-hipos-al-módulo)).

Tres valores tienen que coincidir con el alta, y los tres se mueven juntos o no se mueven:

| Valor | Dónde está en el repositorio | Dónde está del lado de ICG |
| --- | --- | --- |
| `apk_name` = `permoglobal` | `HiPosApkName` en el `.csproj`, las acciones del `AndroidManifest.xml`, `HiPosPaymentActivity.PrimaryApkName` | Alta de CloudLicense |
| Código de versión = `1` | `ApplicationVersion` en el `.csproj` y `HiPosPaymentOrchestrator.ModuleVersionCode` | Versión registrada en HioPosCloud |
| Huella SHA-1 de la llave de firma | `assets/keystore/` (fuera de git) | Se entrega a ICG para el alta |

**Si el código de versión del APK y el registrado en CloudLicense no coinciden exactamente**, HiPOS
da el módulo por desactualizado y pide reinstalarlo en cada arranque. Al subir la versión, hay que
actualizar los dos sitios del repositorio **y** el alta en CloudLicense.

### Cómo verificar el `apk_name` real

No se adivina: se lee del APK que la propia nube distribuye.

```pwsh
aapt2 dump badging <apk-descargado-de-cloudlicense> | Select-String "electronicpayment"
```

Para ver qué apps responden a una acción en la terminal (todas, no solo la primera):

```pwsh
adb -s <serial> shell cmd package query-activities -a icg.actions.electronicpayment.permoglobal.TRANSACTION
```

## 6. Convivencia con otros módulos TEF

En estas terminales puede existir el módulo de Sistecrédito (`com.pos2pay`), con `apk_name`
`permoda`:

* **Los packages no colisionan**: `com.permoda.tefogloba` y `com.pos2pay` son distintos. Dos APK con
  el mismo package no pueden coexistir — instalar uno desinstala el otro.
* **`permoda` no se declara en nuestro manifiesto**, a propósito: declararlo hacía que Android
  mostrara el selector "Completar acción usando…" en mitad de una venta.
* Los demás `apk_name` candidatos declarados (`oglobapay`, `ogloba`, `Permoda Pay Oglobal`,
  `PermodaPayOglobal`) no colisionan con ningún módulo conocido.

## 7. Antes de hacer merge

```pwsh
dotnet format Permoda.Pay.sln --verify-no-changes
dotnet test   Permoda.Pay.sln -c Debug
```

Los dos deben pasar con **0 errores y 0 advertencias**. Si el cambio toca el contrato con HiPOS o
con Ogloba, además hay que validarlo en una terminal real: ver
[PRUEBAS_Y_CERTIFICACION.md](PRUEBAS_Y_CERTIFICACION.md).
