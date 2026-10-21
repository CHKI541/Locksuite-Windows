# publish-release.ps1
# Script de automatización de compilación, empaquetado y publicación de releases en GitHub para LockSuite Windows.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$Version,

    [Parameter(Mandatory = $false, Position = 1)]
    [string]$Notes = "Actualización de LockSuite Windows",

    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "  Publicando LockSuite Windows v$Version" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 1. Validar formato de versión (ej. 0.2.1)
if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    Write-Error "El formato de versión debe ser semver numérico (ej: 0.2.1). Recibido: $Version"
    exit 1
}

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

# Resolver comando dotnet
$dotnetCmd = "dotnet"
$dotnetDefault = "$env:USERPROFILE\.dotnet\dotnet.exe"
if (Test-Path $dotnetDefault) {
    $dotnetCmd = $dotnetDefault
}

# 2. Ejecutar tests unitarios
if (-not $SkipTests) {
    Write-Host "[1/5] Ejecutando suite completa de tests..." -ForegroundColor Yellow
    & "$dotnetCmd" test -c Release
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Fallaron los tests. Abortando publicación."
        exit 1
    }
    Write-Host "  -> Tests pasaron al 100%." -ForegroundColor Green
} else {
    Write-Host "[1/5] Tests omitidos por parámetro -SkipTests." -ForegroundColor DarkYellow
}

# 3. Publicación autocontenida
Write-Host "[2/5] Compilando publicación autocontenida (win-x64)..." -ForegroundColor Yellow
& "$dotnetCmd" publish src/LockSuite.Service -c Release -r win-x64 --self-contained true -o installer/publish/service
if ($LASTEXITCODE -ne 0) { exit 1 }

& "$dotnetCmd" publish src/LockSuite.UI -c Release -r win-x64 --self-contained true -o installer/publish/ui
if ($LASTEXITCODE -ne 0) { exit 1 }

# 4. Compilar instalador con Inno Setup
Write-Host "[3/5] Compilando instalador Inno Setup..." -ForegroundColor Yellow
$isccCandidates = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles (x86)\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
)
$iscc = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
    $cmd = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue
    if ($cmd) { $iscc = $cmd.Source }
}

if (-not $iscc) {
    Write-Error "No se encontró ISCC.exe (Inno Setup 6). Verifique la instalación."
    exit 1
}

# Actualizar versión en LockSuiteSetup.iss
$issPath = "$repoRoot\installer\LockSuiteSetup.iss"
(Get-Content $issPath) -replace '#define MyAppVersion ".*"', "#define MyAppVersion ""$Version""" | Set-Content $issPath

# Actualizar versión en Program.cs
$progPath = "$repoRoot\src\LockSuite.Service\Program.cs"
(Get-Content $progPath) -replace 'public const string Version = ".*";', "public const string Version = ""$Version"";" | Set-Content $progPath

& "$iscc" "/DMyAppVersion=$Version" "$issPath"
if ($LASTEXITCODE -ne 0) { exit 1 }

$installerPath = "$repoRoot\bin\installer\LockSuiteSetup-$Version.exe"
if (-not (Test-Path $installerPath)) {
    Write-Error "El instalador esperado en $installerPath no fue generado."
    exit 1
}
Write-Host "  -> Instalador generado: $installerPath ($([math]::Round((Get-Item $installerPath).Length / 1MB, 2)) MB)" -ForegroundColor Green

# 5. Git commit y push
Write-Host "[4/5] Actualizando repositorio Git y subiendo cambios..." -ForegroundColor Yellow
git add installer/LockSuiteSetup.iss src/LockSuite.Service/Program.cs
git commit -m "release: bump version to v$Version"
git push origin main
if ($LASTEXITCODE -ne 0) { exit 1 }

# 6. Crear GitHub Release con el ejecutable adjunto
Write-Host "[5/5] Creando Release en GitHub y subiendo instalador..." -ForegroundColor Yellow
& gh release create "v$Version" "$installerPath" --title "LockSuite Windows v$Version" --notes "$Notes"
if ($LASTEXITCODE -ne 0) { exit 1 }

Write-Host "==========================================================" -ForegroundColor Green
Write-Host "  Publicación completada exitosamente!" -ForegroundColor Green
Write-Host "  URL: https://github.com/CHKI541/Locksuite-Windows/releases/tag/v$Version" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
