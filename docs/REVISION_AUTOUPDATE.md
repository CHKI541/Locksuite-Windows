# Revisión del sistema de auto-actualización — LockSuite Windows

Revisión del trabajo que hizo Antigravity (UpdateManager + publish-release.ps1 + workflow CI + Presets).
Conclusión corta: **la base estaba bien, pero NO estaba "perfecto"**. Había bugs reales que, en una
actualización en vivo, podían hacer que la update fallara en silencio o que el filtro bloqueara su propia
descarga. Esta sesión (Claude) los corrigió en código. Lo que requiere compilar/probar queda para Antigravity.

## Lo que estaba bien
- Comparación de versiones correcta (`Version.TryParse`, `remoteVer <= currentVer` → no actualiza).
- Descarga a `%ProgramData%\LockSuite\updates`, ejecución silenciosa `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART`.
- El servicio corre como SYSTEM, así que la actualización no pide contraseña al usuario.
- Dominios de GitHub pre-aprobados en los presets.

## Bugs y riesgos encontrados (y cómo se corrigieron)

### 1. [CRÍTICO] El instalador no detenía el servicio en una actualización → binarios en uso
El servicio viejo (`LockSuite.Service.exe`) y la GUI están **corriendo** cuando llega la nueva versión. Inno Setup
no puede sobrescribir un `.exe` en uso. El `.iss` solo paraba el servicio en la **desinstalación**, no en un **upgrade**.
Resultado probable: la update silenciosa fallaba al copiar y el servicio quedaba en la versión vieja.
- **Fix:** se agregó `PrepareToInstall()` en el `.iss` que hace `sc stop LockSuiteService` + `taskkill` de la GUI
  **antes** de copiar archivos (en instalación limpia no encuentra nada que parar: inofensivo). Además
  `CloseApplications=yes`.

### 2. [CRÍTICO SEGURIDAD] Se ejecutaba el `.exe` descargado como SYSTEM sin verificar integridad
`UpdateManager` solo chequeaba que el archivo pesara > 1 MB. Un `.exe` corrupto o manipulado en tránsito se
ejecutaba igual, **con privilegios de SYSTEM**.
- **Fix:** si el release publica `SHA256SUMS.txt`, ahora se descarga, se busca el hash del instalador y se
  **verifica el SHA-256** del archivo bajado **antes** de ejecutarlo. Si no coincide, se aborta. Si el release no
  trae `SHA256SUMS.txt` (releases viejos como v0.2.0), se omite con una advertencia (compatibilidad).
  `publish-release.ps1` y el workflow ahora **generan y publican** `SHA256SUMS.txt`.
- **Pendiente recomendado (fuera de alcance de esta sesión):** firmar el instalador con un certificado
  (Authenticode) y verificar la firma con `WinVerifyTrust`. El hash protege contra una descarga corrupta/MITM,
  pero no contra un compromiso de la cuenta de GitHub (ahí se cambiarían ambos, `.exe` y sums). La firma de código
  es la defensa real; cuando se tenga certificado, agregar la verificación de firma en `UpdateManager`.

### 3. [ALTO] Descarga del instalador podía quedar bloqueada por el propio filtro
GitHub sirve los assets de Releases desde `objects.githubusercontent.com` y, cada vez más, desde
`release-assets.githubusercontent.com`. Los presets listaban `objects.` y `raw.` puntualmente, que **no** cubren
`release-assets.`. Con el filtro activo, la descarga de la actualización podía fallar.
- **Fix:** en `Presets.cs` se agregó el dominio **pelado** `githubusercontent.com`, que por la semántica del
  matcher cubre **todos** los subdominios (`objects.`, `raw.`, `release-assets.`, etc.).

### 4. [MEDIO] Inconsistencia de versión entre los dos caminos de release
`publish-release.ps1` parchea `Program.cs` (la versión que el updater compara), pero el **workflow de GitHub Actions**
(disparado por tag) **no** lo hacía: compilaba binarios con la `Program.Version` vieja commiteada. Un equipo que
instalara esa build se creería desactualizado y volvería a descargar en cada chequeo (bucle de updates).
- **Fix:** el workflow ahora parchea `Program.cs` **y** el `.iss` desde la versión (tag o input) **antes** de compilar.
  Una sola fuente de verdad en ambos caminos.

### 5. [MEDIO] El camino `workflow_dispatch` compilaba pero no publicaba release
La condición `if: startsWith(github.ref, 'refs/tags/')` hacía que un run manual (workflow_dispatch) construyera
todo pero **no** creara el Release.
- **Fix:** se pasa `tag_name` explícito a `action-gh-release`, así funciona tanto por tag como por ejecución manual.

### 6. [MENOR] Acumulación de instaladores viejos / `InstallService` no idempotente
- Se agregó limpieza de `.exe` viejos en la carpeta `updates` antes de cada descarga.
- `--install` ahora es idempotente: si el servicio ya existe (upgrade), hace `sc config` en vez de fallar en `sc create`.

## Verificación
- Tests nuevos (unitarios, sin red): `TryGetExpectedSha256` (formato clásico, formato `*`, nombre case-insensitive,
  asset ausente) y `ComputeSha256` (vector conocido de "abc"). Los 3 tests existentes de `UpdateManager` siguen válidos.
- La prueba de punta a punta (una versión mayor publicada se baja, verifica hash, para el servicio, reemplaza binarios
  y reinicia) requiere VM + un release real: queda en el HANDOFF para Antigravity.
