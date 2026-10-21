# publish-release.ps1
# Script de automatizacion de compilacion, empaquetado y publicacion de releases en GitHub para LockSuite Windows.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$Version,

    [Parameter(Mandatory = $false, Position = 1)]
    [string]$Notes = "Actualizacion de LockSuite Windows",

    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "  Publicando LockSuite Windows v$Version" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 1. Validar formato de version (ej. 0.3.1)
if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    Write-Error "El formato de version debe ser semver numerico (ej: 0.3.1). Recibido: $Version"
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
    Write-Host "[1/6] Ejecutando suite completa de tests..." -ForegroundColor Yellow
    & "$dotnetCmd" test -c Release
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Fallaron los tests. Abortando publicacion."
        exit 1
    }
    Write-Host "  -> Tests pasaron al 100%." -ForegroundColor Green
} else {
    Write-Host "[1/6] Tests omitidos por parametro -SkipTests." -ForegroundColor DarkYellow
}

# 3. Fijar la version en las fuentes (unica fuente de verdad para ambos caminos de release)
Write-Host "[2/6] Fijando version en Program.cs y LockSuiteSetup.iss..." -ForegroundColor Yellow
$issPath = "$repoRoot\installer\LockSuiteSetup.iss"
(Get-Content $issPath) -replace '#define MyAppVersion ".*"', "#define MyAppVersion ""$Version""" | Set-Content $issPath

$progPath = "$repoRoot\src\LockSuite.Service\Program.cs"
(Get-Content $progPath) -replace 'public const string Version = ".*";', "public const string Version = ""$Version"";" | Set-Content $progPath

# 4. Publicacion autocontenida
Write-Host "[3/6] Compilando publicacion autocontenida (win-x64)..." -ForegroundColor Yellow
& "$dotnetCmd" publish src/LockSuite.Service -c Release -r win-x64 --self-contained true -o installer/publish/service
if ($LASTEXITCODE -ne 0) { exit 1 }

& "$dotnetCmd" publish src/LockSuite.UI -c Release -r win-x64 --self-contained true -o installer/publish/ui
if ($LASTEXITCODE -ne 0) { exit 1 }

# 5. Compilar instalador con Inno Setup
Write-Host "[4/6] Compilando instalador Inno Setup..." -ForegroundColor Yellow
$isccCandidates = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
)
$iscc = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
    $cmd = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue
    if ($cmd) { $iscc = $cmd.Source }
}

if (-not $iscc) {
    Write-Error "No se encontro ISCC.exe (Inno Setup 6). Verifique la instalacion."
    exit 1
}

& "$iscc" "/DMyAppVersion=$Version" "$issPath"
if ($LASTEXITCODE -ne 0) { exit 1 }

$installerPath = "$repoRoot\bin\installer\LockSuiteSetup-$Version.exe"
if (-not (Test-Path $installerPath)) {
    Write-Error "El instalador esperado en $installerPath no fue generado."
    exit 1
}
Write-Host "  -> Instalador generado: $installerPath ($([math]::Round((Get-Item $installerPath).Length / 1MB, 2)) MB)" -ForegroundColor Green

# 6. Generar SHA256SUMS.txt (lo verifica el auto-updater antes de ejecutar el .exe como SYSTEM)
Write-Host "[5/6] Generando SHA256SUMS.txt..." -ForegroundColor Yellow
$sha = (Get-FileHash $installerPath -Algorithm SHA256).Hash.ToLower()
$sumsPath = "$repoRoot\bin\installer\SHA256SUMS.txt"
# Formato clasico: "<hash>  <nombre>" (dos espacios), sin BOM.
"$sha  LockSuiteSetup-$Version.exe" | Set-Content -Encoding ascii $sumsPath
Write-Host "  -> SHA256: $sha" -ForegroundColor Green

# 7. Git commit y push
Write-Host "[6/6] Commit, push y Release en GitHub..." -ForegroundColor Yellow
git add installer/LockSuiteSetup.iss src/LockSuite.Service/Program.cs
git commit -m "release: bump version to v$Version"
git push origin main
if ($LASTEXITCODE -ne 0) { exit 1 }

# 8. Crear GitHub Release con el instalador y el SHA256SUMS adjuntos
& gh release create "v$Version" "$installerPath" "$sumsPath" --title "LockSuite Windows v$Version" --notes "$Notes"
if ($LASTEXITCODE -ne 0) { exit 1 }

Write-Host "==========================================================" -ForegroundColor Green
Write-Host "  Publicacion completada exitosamente!" -ForegroundColor Green
Write-Host "  URL: https://github.com/CHKI541/Locksuite-Windows/releases/tag/v$Version" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
