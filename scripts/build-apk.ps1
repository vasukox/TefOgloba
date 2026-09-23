#requires -Version 7.0

[CmdletBinding(SupportsShouldProcess)]
param(
    [ValidateSet('Debug','UAT','Release')]
    [string]$Configuration = 'UAT',

    # ogloba.keystore es el keystore vigente del módulo (alias `ogloba`, CN=Ogloba).
    # Reemplazó a tefogloba.keystore (alias sistecredito), que nombraba al módulo de otro
    # proveedor. pos2pay-release.keystore no abre con ninguna contraseña conocida.
    [string]$KeystorePath = (Join-Path $PSScriptRoot '..\assets\keystore\ogloba.keystore'),
    [string]$KeystoreAlias = 'ogloba',
    [string]$KeystorePassword = $env:PERMODA_KEYSTORE_PASS,
    [string]$KeyPassword = $env:PERMODA_KEY_PASS,

    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\artifacts')
)

$ErrorActionPreference = 'Stop'

function Write-Header {
    Write-Host '== Permoda.Pay Ogloba - APK packaging ==' -ForegroundColor Cyan
    Write-Host ("Configuration: {0}" -f $Configuration)
    Write-Host ("Keystore:       {0}" -f $KeystorePath)
}

function Test-Keystore {
    if (-not (Test-Path -LiteralPath $KeystorePath)) {
        throw "The signing keystore is missing at $KeystorePath. Place the KOAJ release keystore in assets/keystore before running."
    }
    if ([string]::IsNullOrWhiteSpace($KeystorePassword)) {
        throw 'Set the $env:PERMODA_KEYSTORE_PASS environment variable before packaging a Release/UAT APK.'
    }
    if ([string]::IsNullOrWhiteSpace($KeyPassword)) {
        throw 'Set the $env:PERMODA_KEY_PASS environment variable before packaging a Release/UAT APK.'
    }
}

function Invoke-DotnetStep {
    param(
        [string]$StepName,
        [string[]]$Arguments
    )

    Write-Host ("-> {0}" -f $StepName) -ForegroundColor Yellow
    & dotnet @Arguments | Write-Host
    if ($LASTEXITCODE -ne 0) {
        throw "Step '$StepName' failed with exit code $LASTEXITCODE."
    }
}

function Get-SourceRoot { (Resolve-Path (Join-Path $PSScriptRoot '..')).Path }

Write-Header

if ($Configuration -in @('Release', 'UAT')) {
    Test-Keystore
}

Invoke-DotnetStep -StepName 'restore' -Arguments @(
    'restore',
    (Join-Path (Get-SourceRoot) 'Permoda.Pay.sln')
)

Invoke-DotnetStep -StepName ('build ' + $Configuration) -Arguments @(
    'build',
    (Join-Path (Get-SourceRoot) 'Permoda.Pay.sln'),
    '-c', $Configuration
)

if (-not (Test-Path -LiteralPath $OutputDirectory)) {
    New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
}

$projectFile = Join-Path (Get-SourceRoot) 'src\Permoda.Pay.Maui\Permoda.Pay.Maui.csproj'
$packageArguments = @(
    'publish',
    $projectFile,
    '-c', $Configuration,
    '-f', 'net10.0-android',
    '-o', $OutputDirectory,
    '/p:AndroidPackageFormat=apk'
)

if ($Configuration -in @('Release', 'UAT')) {
    $packageArguments += @(
        "/p:AndroidKeyStore=true",
        "/p:AndroidSigningKeyStore=$((Resolve-Path -LiteralPath $KeystorePath).Path)",
        "/p:AndroidSigningKeyAlias=$KeystoreAlias",
        "/p:AndroidSigningKeyPass=$KeyPassword",
        "/p:AndroidSigningStorePass=$KeystorePassword"
    )
}

Invoke-DotnetStep -StepName 'publish' -Arguments $packageArguments

Write-Host ("APK artifacts emitted to {0}" -f $OutputDirectory) -ForegroundColor Green
Get-ChildItem -LiteralPath $OutputDirectory -Filter '*.apk' | Format-Table FullName, Length
