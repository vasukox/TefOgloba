<#
.SYNOPSIS
    Compila, firma y VERIFICA un APK de TefOgloba listo para repartir.

.DESCRIPTION
    Existe porque hasta ahora cada release dependía de que alguien se acordara de mirar a mano la
    firma, el versionCode, la ABI y si el AOT había entrado. Un solo olvido en esa lista produce un
    APK que parece bueno y no lo es:

      · Sin firmar        → Android lo rechaza al instalar.
      · Firmado con otra llave → la actualización falla sobre las terminales ya instaladas y toca
                            desinstalar, lo que borra PIN, credenciales y cajeros de esa caja.
      · versionCode != 1  → HiPOS lo da por desactualizado y pide reinstalarlo en CADA arranque.
                            Ese bucle ya costó dos días.
      · Sin imágenes AOT  → el arranque en frío vuelve de ~0,7 s a varios segundos.
      · Con x86_64        → 32 MB de más que ninguna terminal abre, en cada descarga de las 512.

    Y compila SIEMPRE EN LIMPIO. Las compilaciones incrementales de este proyecto han producido
    APK que crashean al arrancar (el registro JNI queda desfasado de los ensamblados que lo
    acompañan), y el síntoma no se distingue de un bug de código: la app muere antes de ejecutar
    una línea propia. Borrar obj/ y bin/ cuesta minutos; entregar un APK muerto cuesta un día.

    Si algo no cuadra, el script FALLA y no deja el archivo. Un release que no se puede verificar
    no se entrega.

.PARAMETER Configuracion
    Release (producción, por el API Management) o UAT (sandbox co-ts). Por defecto Release.

.PARAMETER Destino
    Carpeta donde queda el APK verificado. Por defecto artifacts/.

.PARAMETER SaltarPruebas
    Omite la suite. Solo para iterar; un release para tiendas nunca debería usarlo.

.EXAMPLE
    $env:PERMODA_KEYSTORE_PASS = '...'
    $env:PERMODA_KEY_PASS = '...'
    .\scripts\publicar-release.ps1
#>

[CmdletBinding()]
param(
    [ValidateSet('Release', 'UAT')]
    [string] $Configuracion = 'Release',

    [string] $Destino = 'artifacts',

    [switch] $SaltarPruebas
)

$ErrorActionPreference = 'Stop'

# Lo que TIENE que cumplir un APK para poder repartirse. Cambiar cualquiera de estos valores es
# una decisión de despliegue, no de código: que quede a la vista y en un solo sitio.
$VersionCodeEsperado = '1'
$AbiEsperada         = 'arm64-v8a'
$HuellaSha1Esperada  = '93912916584e2f11ec22c23b192d56f62dbff3c6'
$MinimoImagenesAot   = 50

$raiz = Split-Path -Parent $PSScriptRoot
Set-Location $raiz

$fallos = @()
function Paso([string] $texto) { Write-Host "`n=== $texto ===" -ForegroundColor Cyan }
function Bien([string] $texto) { Write-Host "  OK   $texto" -ForegroundColor Green }
function Mal([string] $texto)  { Write-Host "  MAL  $texto" -ForegroundColor Red; $script:fallos += $texto }

# ---------------------------------------------------------------------------------------------
Paso 'Credenciales de firma'

# Se comprueba ANTES de compilar. El proyecto solo firma si están las DOS variables, y si falta
# una produce un APK sin firmar sin decir nada: se descubre al intentar instalarlo, después de
# haber esperado los diez minutos del AOT.
if (-not $env:PERMODA_KEYSTORE_PASS -or -not $env:PERMODA_KEY_PASS) {
    throw "Faltan PERMODA_KEYSTORE_PASS y/o PERMODA_KEY_PASS. Sin las DOS el APK sale sin firmar."
}
Bien 'Las dos variables de firma están definidas'

$keystore = Join-Path $raiz 'assets\keystore\ogloba-fresh.keystore'
if (-not (Test-Path $keystore)) { throw "No se encuentra el keystore en $keystore" }
Bien 'Keystore presente'

# ---------------------------------------------------------------------------------------------
Paso 'Herramientas de Android'

$buildTools = Get-ChildItem "$env:LOCALAPPDATA\Android\Sdk\build-tools" -Directory -ErrorAction SilentlyContinue |
    Sort-Object Name -Descending | Select-Object -First 1
if (-not $buildTools) { throw "No se encontraron las build-tools del SDK de Android." }

$apksigner = Join-Path $buildTools.FullName 'apksigner.bat'
$aapt2     = Join-Path $buildTools.FullName 'aapt2.exe'
Bien "build-tools $($buildTools.Name)"

# ---------------------------------------------------------------------------------------------
if (-not $SaltarPruebas) {
    Paso 'Pruebas'
    dotnet test Permoda.Pay.sln --nologo --verbosity quiet | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Las pruebas fallaron. No se genera APK con la suite en rojo." }
    Bien 'Suite completa en verde'
}

# ---------------------------------------------------------------------------------------------
Paso 'Limpieza'

# Ver la nota de arriba: las incrementales de este proyecto han producido APK que crashean al
# arrancar, y el síntoma no se distingue de un bug propio.
Get-ChildItem -Path 'src', 'tests' -Include 'obj', 'bin' -Recurse -Directory -ErrorAction SilentlyContinue |
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
Bien 'obj/ y bin/ borrados'

# ---------------------------------------------------------------------------------------------
Paso 'Passphrase de sandbox'

# Solo aplica a UAT: es lo que hace que la pantalla de configuración autocomplete la contraseña de
# co-ts al montar una terminal de pruebas. No está en el código porque el repositorio es público.
#
# En Release ni se mira. El proyecto además ignora la propiedad fuera de UAT/Debug, así que aunque
# alguien tenga la variable definida en su terminal, no puede colarse en el APK productivo.
if ($Configuracion -eq 'UAT') {
    if ($env:PERMODA_SANDBOX_PASSWORD) {
        Bien 'Se inyectará la passphrase de sandbox (autocompletado activo)'
    } else {
        Write-Host "  AVISO  Sin PERMODA_SANDBOX_PASSWORD: el autocompletado dejará la contraseña vacía y habrá que pegarla a mano." -ForegroundColor Yellow
    }
} else {
    Bien 'Producción: la passphrase de sandbox no se incluye'
}

# ---------------------------------------------------------------------------------------------
Paso "Compilación ($Configuracion, con AOT — toma varios minutos)"

dotnet publish src\Permoda.Pay.Maui\Permoda.Pay.Maui.csproj -c $Configuracion -f net10.0-android --verbosity quiet | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "Falló la compilación. Si el error es 'java.exe salió con el código 2', es el fallo transitorio de empaquetado: vuelve a ejecutar."
}

$apk = Get-ChildItem "src\Permoda.Pay.Maui\bin\$Configuracion\net10.0-android\publish\*-Signed.apk" -ErrorAction SilentlyContinue |
    Select-Object -First 1
if (-not $apk) { throw "La compilación terminó pero no se encontró ningún APK firmado." }
Bien ("APK generado ({0:N2} MB)" -f ($apk.Length / 1MB))

# ---------------------------------------------------------------------------------------------
Paso 'Verificación'

$certs = & $apksigner verify --print-certs $apk.FullName 2>&1 | Out-String
$sha1 = if ($certs -match 'SHA-1 digest:\s*([0-9a-f]{40})') { $Matches[1] } else { $null }

if (-not $sha1)                      { Mal 'El APK no está firmado' }
elseif ($sha1 -ne $HuellaSha1Esperada) { Mal "Firmado con OTRA llave ($sha1). Las terminales ya instaladas rechazarán la actualización." }
else                                 { Bien 'Firmado con la llave correcta' }

$badging = & $aapt2 dump badging $apk.FullName 2>$null | Out-String

$versionCode = if ($badging -match "versionCode='(\d+)'") { $Matches[1] } else { '?' }
if ($versionCode -ne $VersionCodeEsperado) {
    Mal "versionCode=$versionCode y HiPOS espera $VersionCodeEsperado. Con otro valor pedirá reinstalar el módulo en cada arranque."
} else { Bien "versionCode=$versionCode" }

$abis = if ($badging -match "native-code: '([^']+)'") { $Matches[1] } else { '' }
if ($abis -ne $AbiEsperada) { Mal "ABI '$abis'; se espera solo '$AbiEsperada'." } else { Bien "ABI $abis" }

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead($apk.FullName)
try {
    $aot = ($zip.Entries | Where-Object { $_.FullName -match 'libaot-' }).Count
    if ($aot -lt $MinimoImagenesAot) {
        Mal "Solo $aot imágenes AOT (se esperan >= $MinimoImagenesAot). El arranque en frío se degrada a varios segundos."
    } else { Bien "$aot imágenes AOT" }

    $otrasAbis = $zip.Entries | Where-Object { $_.FullName -match '^lib/(?!arm64-v8a/)' }
    if ($otrasAbis) { Mal "El APK trae ABIs de más: $(($otrasAbis | ForEach-Object { ($_.FullName -split '/')[1] } | Select-Object -Unique) -join ', ')" }
    else { Bien 'Sin ABIs de más' }
}
finally { $zip.Dispose() }

# ---------------------------------------------------------------------------------------------
if ($fallos.Count -gt 0) {
    Write-Host "`nNO SE ENTREGA. $($fallos.Count) comprobación(es) fallaron:" -ForegroundColor Red
    $fallos | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    throw "El APK no cumple las condiciones para repartirse."
}

Paso 'Publicación'

if (-not (Test-Path $Destino)) { New-Item -ItemType Directory -Path $Destino | Out-Null }

$sufijo  = if ($Configuracion -eq 'Release') { 'produccion' } else { 'uat' }
$destino = Join-Path $Destino "tefogloba-$sufijo-$(Get-Date -Format 'yyyyMMdd-HHmm').apk"
Copy-Item $apk.FullName $destino -Force

$hash = (Get-FileHash $destino -Algorithm SHA256).Hash

Write-Host "`nLISTO PARA REPARTIR" -ForegroundColor Green
Write-Host "  Archivo  : $destino"
Write-Host ("  Tamaño   : {0:N2} MB" -f ((Get-Item $destino).Length / 1MB))
Write-Host "  SHA-256  : $hash"
Write-Host "  Ambiente : $(if ($Configuracion -eq 'Release') { 'PRODUCCIÓN (API Management)' } else { 'SANDBOX (co-ts)' })"
Write-Host "`n  Pruébalo en UNA caja antes de repartirlo a las demás." -ForegroundColor Yellow
