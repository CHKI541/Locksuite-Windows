# Handoff para Antigravity — LockSuite Windows (tras Sesión 5, Claude)

Pegá este archivo (o pedile a Antigravity que lo lea) al abrir una conversación en Antigravity para ejecutar lo que requiere terminal real en Windows.

> **Qué pasó en la sesión 5 (Claude, entorno Linux sin `dotnet`):** se hizo una revisión a fondo y se aplicaron correcciones **en código** (no se pudo compilar ni probar acá). El detalle completo está en `docs/REVISION_PRODUCCION.md`. Esta sesión **no** hizo commit: lo hace Antigravity tras verificar en verde.

---

## 0. Archivos modificados/creados en la sesión 5

**Core:** `DomainMatcher.cs` (+`IsValidPattern`), `IpcMessages.cs` (+`IsDnsListening`), `LockSuiteClient.cs` (+`SetLearningModeAsync`).
**Service:** `Config/ConfigManager.cs` (snapshot de patrones, lock, `SetLearningMode`, validación), `Dns/DnsResolver.cs` (Interlocked, back-pressure, bind tolerante, `IsListening`), `Dns/DnsConfigurator.cs` (1 sola llamada a PowerShell, sin deadlock), `Learning/LearningManager.cs` (throttle de disco + `IDisposable`), `Security/SecurityManager.cs` (reset de lockout), `Wfp/WfpManager.cs` (claves fijas + transacción idempotente + `HardReset`), `Ipc/NamedPipeServer.cs` (`SetLearningMode`, accesores con lock), `Worker.cs` (rollback en parada ordenada), `Program.cs` (`--set-password-file`, `--reset-wfp`, `sc failure`, descripción).
**UI:** `Converters/AuthConverters.cs` (+3 convertidores), `App.xaml` (registro), `MainWindow.xaml` (banners + fila DNS + toggle learning), `MainWindow.xaml.cs` (handler), `ViewModels/MainViewModel.cs` (`IsDnsListening`, `SetLearningModeAsync`).
**Instalador:** `installer/LockSuiteSetup.iss` (v0.2.0, self-contained, página de contraseña, reset en uninstall).
**Tests:** nuevos casos en `DomainMatcherTests`, `ConfigManagerTests`, `LearningManagerTests`.

---

## 1. Compilar y testear la solución

> Correr los tests como usuario **estándar (sin elevación)**: así `WfpManagerTests` usa el modo simulación y no toca la WFP real de la máquina de desarrollo. (Elevado, `Initialize(failClosed:false)` crearía filtros reales momentáneamente.)

```powershell
cd "C:\Locksuite windows"
dotnet build LockSuite.sln -c Release
dotnet test  LockSuite.sln -c Release
```
(Si `dotnet` no está en PATH: `& "$env:USERPROFILE\.dotnet\dotnet.exe" ...`.)

Deben pasar los tests existentes **más** los nuevos (validación de patrones, rechazo de dominios inválidos, `SetLearningMode`, `ActivePatternCount`, `Flush` del modo aprendizaje). Si algo no compila, es por un cambio de esta sesión: reportar el archivo/línea.

---

## 2. Publicar (self-contained, sin depender de .NET en el equipo destino)

```powershell
cd "C:\Locksuite windows"
dotnet publish src\LockSuite.Service\LockSuite.Service.csproj -c Release -r win-x64 --self-contained true -o installer\publish\service
dotnet publish src\LockSuite.UI\LockSuite.UI.csproj          -c Release -r win-x64 --self-contained true -o installer\publish\ui
```
El `.iss` toma los binarios de `installer\publish\service` y `installer\publish\ui` (ver `[Files]`).

---

## 3. Empaquetar el instalador (Inno Setup 6)

```powershell
# Si falta ISCC:
winget install JRSoftware.InnoSetup --accept-source-agreements --accept-package-agreements
# Compilar:
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" "installer\LockSuiteSetup.iss"
```
Genera `bin\installer\LockSuiteSetup-0.2.0.exe`.

---

## 4. Prueba en VM limpia de Windows 11 (aislada del host)

### 4.1 Instalación y contraseña del tutor
1. Instalar `LockSuiteSetup-0.2.0.exe` **desde una cuenta de administrador (tutor)**.
2. En el asistente debe aparecer la **página de contraseña del tutor** (con confirmación). Definirla.
3. Verificar que el servicio `LockSuiteService` quedó **en ejecución** (as SYSTEM) y en `start=auto`:
   ```powershell
   sc.exe query LockSuiteService
   sc.exe qc LockSuiteService
   sc.exe qfailure LockSuiteService   # debe mostrar acciones de reinicio
   ```
4. Crear/usar una cuenta **estándar** y confirmar que, desde la GUI, al intentar "Desbloquear tutor" y fijar contraseña, el servicio responde que **ya está configurada** (el usuario estándar NO puede fijarla).

### 4.2 Filtrado en vivo (cuenta estándar)
- Navegar a un permitido (`google.com`, `mercadopago.com.ar`) → **debe cargar**.
- Navegar a uno no listado (`facebook.com`, `instagram.com`) → **no debe cargar** (NXDOMAIN / sin ruta).
- En la GUI, "Bloqueados recientes" debe listar el intento; con la contraseña del tutor, "Permitir" en 1 clic debe habilitarlo.
- Verificar el banner: con WFP activo NO debe verse "Modo simulación"; "Resolutor DNS escuchando: Sí".

### 4.3 WFP: idempotencia, fail-closed y rollback
- **Idempotencia:** reiniciar el servicio varias veces (`sc stop`/`sc start`) y comprobar que NO se acumulan filtros duplicados:
  ```powershell
  netsh wfp show filters file=wfp.xml   # revisar que los filtros "LockSuite ..." no se multiplican
  ```
- **Parada ordenada restaura la red:** `sc stop LockSuiteService` → la navegación general debe volver (WFP revertido, NRPT removido).
- **Fail-closed en caída:** matar el proceso del servicio (Task Manager → End task sobre el proceso, NO `sc stop`) y verificar que la red queda **bloqueada** (fail-closed) hasta que `sc failure` lo reinicie o se reinicie el equipo.
- **Reset/recuperación:** `"...\service\LockSuite.Service.exe" --reset-wfp` debe remover filtros + NRPT (red restaurada).

### 4.4 Desinstalación limpia
- Desinstalar y verificar: servicio removido, SIN filtros WFP "LockSuite" remanentes, SIN regla NRPT `LockSuiteDNS`, y la red del equipo funciona normal.
  ```powershell
  Get-DnsClientNrptRule | Where-Object { $_.Comment -eq 'LockSuiteDNS' }   # no debe devolver nada
  ```

### 4.5 Matriz de evasión (DEFENSIVA — todo debe quedar bloqueado desde la cuenta estándar)
- Configurar manualmente otro DNS en el adaptador (requiere admin → debe **fallar** para el usuario estándar).
- Forzar DoH en el navegador (Firefox/Chrome) → la navegación a sitios no permitidos **no** debe funcionar.
- Editar `C:\Windows\System32\drivers\etc\hosts` como usuario estándar → debe **fallar** (sin permiso); aun con entrada, WFP bloquea la IP.
- `sc stop LockSuiteService` como usuario estándar → debe **fallar** (acceso denegado).
- Cambiar NRPT como usuario estándar → debe **fallar**.

---

## 5. Al terminar
1. Actualizar `ESTADO.md`: resultados reales de build/test/VM, nuevo renglón en "Log de sesiones", y la sección "Próximo paso".
2. Si todo pasa en verde, hacer **commit** con mensaje descriptivo (p. ej. `fix+hardening: idempotencia WFP, fail-closed en caída, contraseña en instalación, perf DNS/learning, UX avisos`).
3. Reportar cualquier hallazgo de la §2.6 de `docs/REVISION_PRODUCCION.md` (hardening fase 2: restricción de puertos en reglas dinámicas, SNI, segunda sesión dinámica para reglas por IP, TCP/53).

---

# SESIÓN 8 (Claude) — build / test / empaquetado v0.3.0

> Claude corrió en un entorno Linux (carpeta montada), **sin** `dotnet`/WPF/PowerShell, así que **no pudo compilar ni probar**. Todo lo de abajo es lo que falta ejecutar en Windows. Si algo no compila, es un cambio de esta sesión: reportar archivo/línea.

## 0. Archivos creados/modificados en la sesión 8

**Core:** `TrialConfig.cs` (NUEVO), `IpcMessages.cs` (+`TrialStatusDto`/`TrialDecisionDto`/`SetTrialDto`), `LockSuiteClient.cs` (+`GetTrialStatusAsync`/`DecideTrialAsync`/`SetTrialAsync`), `Presets.cs` (+`githubusercontent.com`).
**Service:** `Trial/TrialManager.cs` (NUEVO), `Ipc/NamedPipeServer.cs` (+TrialManager opcional y comandos `GetTrialStatus`/`DecideTrial`/`SetTrial`), `Program.cs` (DI de TrialManager, CLI `--set-trial`/`--trial-status`, `--install` idempotente, `Version="0.3.0"`), `Worker.cs` (+TrialManager y `CheckAutoRemove()` en el loop), `Updates/UpdateManager.cs` (verificación SHA-256 + limpieza + helpers).
**UI:** `MainWindow.xaml` (nav Ayuda + vista FAQ + banners de prueba), `MainWindow.xaml.cs` (nav + diálogo de fin de prueba + handlers), `ViewModels/MainViewModel.cs` (estado del trial + evento), `Views/TrialEndedDialog.xaml(.cs)` (NUEVO).
**Instalador:** `installer/LockSuiteSetup.iss` (v0.3.0, `PrepareToInstall` para parar servicio en upgrade, página de modo de prueba, `[Registry]` RunAsInvoker como tarea, `[UninstallDelete]`).
**Scripts/CI:** `scripts/publish-release.ps1` (+SHA256SUMS), `.github/workflows/build-and-release.yml` (fija versión antes de compilar, SHA256SUMS, release por dispatch).
**Tests:** `tests/LockSuite.Core.Tests/TrialConfigTests.cs` (NUEVO), `tests/LockSuite.Service.Tests/TrialManagerTests.cs` (NUEVO), `tests/LockSuite.Service.Tests/TrialIpcTests.cs` (NUEVO), `tests/LockSuite.Service.Tests/UpdateManagerTests.cs` (+hash).
**Docs:** `docs/REVISION_AUTOUPDATE.md` (NUEVO), `ESTADO.md` actualizado.

## 1. Build + tests (como usuario estándar, sin elevación → WFP en simulación)

```powershell
cd "C:\Locksuite windows"
del .ls_write_test   # archivo vacío que dejó la sesión de Claude (ya está en .gitignore)
dotnet build LockSuite.sln -c Release
dotnet test  LockSuite.sln -c Release
```
Esperado: los 73 tests previos **más** los nuevos. Grupos nuevos:
- `TrialConfigTests` (Core): estados/expiración/serialización del trial.
- `TrialManagerTests` (Service): persistencia, Decide(conservar/quitar), CheckAutoRemove.
- `TrialIpcTests` (Service): `GetTrialStatus`, `DecideTrial` (conservar sin auth; quitar con `tutorlock` exige auth; quitar sin `tutorlock` no exige), `SetTrial` exige auth.
- `UpdateManagerTests` (+): `TryGetExpectedSha256` (3 casos) y `ComputeSha256` (vector "abc").

## 2. Publicar + empaquetar v0.3.0

Opción simple (un comando, hace todo incl. SHA256SUMS, commit, push y release):
```powershell
.\scripts\publish-release.ps1 -Version "0.3.0" -Notes "Modo de prueba, Instalar en mi usuario, Ayuda/FAQ, hardening del auto-update"
```
O a mano:
```powershell
dotnet publish src\LockSuite.Service -c Release -r win-x64 --self-contained true -o installer\publish\service
dotnet publish src\LockSuite.UI      -c Release -r win-x64 --self-contained true -o installer\publish\ui
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" "/DMyAppVersion=0.3.0" "installer\LockSuiteSetup.iss"
# Generar el sums (lo verifica el auto-updater):
$h = (Get-FileHash "bin\installer\LockSuiteSetup-0.3.0.exe" -Algorithm SHA256).Hash.ToLower()
"$h  LockSuiteSetup-0.3.0.exe" | Set-Content -Encoding ascii "bin\installer\SHA256SUMS.txt"
```

## 3. Validación en VM limpia de Windows 11 (lo nuevo de la sesión 8)

### 3.1 Instalar programas como usuario estándar (Tarea 1)
- Instalar LockSuite como admin dejando marcada la tarea "Instalar en mi usuario...".
- En cuenta **estándar**: clic derecho sobre un instalador `.exe` **per-user** (p. ej. el setup de un navegador que soporta instalación por usuario) → elegir "Instalar en mi usuario (sin administrador)". Debe instalar en `%LOCALAPPDATA%` **sin** cartel de UAC. (Un instalador que realmente necesita admin va a fallar: es lo esperado.)
- Abrir la app LockSuite → pestaña **Ayuda / FAQ**: debe verse el texto explicativo.
- Confirmar que, pase lo que pase, navegar a un dominio NO permitido sigue bloqueado (el filtro es a nivel de red).

### 3.2 Modo de prueba (Tarea 3)
- **Prompt (por defecto):** instalar marcando "Activar modo de prueba" con, p. ej., 1 hora (para no esperar, se puede usar CLI: `...\service\LockSuite.Service.exe --set-trial 2 prompt` = 2 min). En "Estado" debe verse la cuenta regresiva. Al vencer, debe aparecer la ventana Conservar/Quitar:
  - "Conservar" → `--trial-status` debe decir `State=None`/`Enabled=False` (permanente).
  - "Quitar" → se desinstala todo; verificar SIN filtros WFP "LockSuite", SIN regla NRPT `LockSuiteDNS`, y red normal.
- **Autoremove (prueba descartable 10 min):** `...\service\LockSuite.Service.exe --set-trial 10 autoremove`. A los 10 min el servicio lanza el desinstalador solo; verificar que queda todo limpio.
- **Tutorlock:** `--set-trial 2 prompt tutorlock` → al vencer, "Quitar" debe **pedir la contraseña del tutor**; "Conservar" no.
- **Fail-closed:** con `prompt`, si no se decide, LockSuite sigue filtrando (no se borra solo).

### 3.3 Auto-update con verificación de integridad (Tarea 2)
- Publicar un release de prueba **mayor** (p. ej. 0.3.1) con su `SHA256SUMS.txt` adjunto.
- En un equipo con 0.3.0 instalado, forzar un chequeo (reiniciar el servicio) y verificar en el Event Log: "Integridad verificada (SHA-256 OK)", servicio detenido (PrepareToInstall), binarios reemplazados, servicio reiniciado, versión nueva corriendo.
- Negativo: subir un `SHA256SUMS.txt` con un hash incorrecto → el updater debe **abortar** y NO ejecutar el instalador.

## 4. Al terminar
Actualizar `ESTADO.md` (resultados reales de build/test/VM, nueva línea de log, próximo paso) y `git commit` descriptivo. Si se publica, `git tag v0.3.0` dispara el workflow (o usar el script).

---

# SESIÓN 9 (Claude) — botones "Permitir Gmail / Chat / Drive / Fotos / prog.co.il / mitmachim.top" + bloqueo fijo del buscador y Gemini → v0.4.0

> Claude corrió sin `dotnet` (entorno Linux, sin red a NuGet): **no compiló ni corrió los tests**. Sí verificó la lógica nueva con una simulación en Python (matcher, listas y migración: todas las verificaciones en verde) y que los XAML sean XML válido. Si algo no compila, es de esta sesión: reportar archivo/línea.
> **v0.4.0 incluye todo lo de v0.3.0** (sesión 8). Si v0.3.0 todavía no se publicó, se puede publicar directamente v0.4.0 y validar ambas listas (§3 de la sesión 8 + §3 de abajo).

## 0. Archivos creados/modificados en la sesión 9

**Core:** `DomainMatcher.cs` (sintaxis `=host` exacto y comodín de una etiqueta `lh*.x.com`; `MatchesAny`; `IsAllowed(lista, host, bloqueos)` con prioridad de bloqueo), `WhitelistConfig.cs` (`DomainPack.Id`/`Blocks`/`Excluded`, `ActiveBlockPatterns`, `IsHardBlocked`, `version` 2), `Presets.cs` (reescrito: catálogo con un paquete por servicio + "Bloqueo fijo: buscador de Google y Gemini" + notas para "Bloqueados recientes"), `PresetSync.cs` (NUEVO: migración de configs v0.2/v0.3 y sincronización de presets).
**Service:** `Config/ConfigManager.cs` (snapshot permitidos+bloqueos, `PresetSync` al cargar y en `SaveConfig`, `AddDomain`/`RemoveDomain` con paquetes de fábrica y lista `excluded`), `Program.cs` (`Version = "0.4.0"`).
**UI:** `MainWindow.xaml` (Lista Blanca: tarjetas "Permitir X" + "Abrir", estilo de paquetes de bloqueo, nota en Bloqueados Recientes, 3 FAQ nuevas), `MainWindow.xaml.cs` (handlers de tarjetas, avisos de bloqueo fijo y de dominios amplios), `ViewModels/PackViewModel.cs`, `ViewModels/MainViewModel.cs` (`ServicePacks`, `IsHardBlocked`, `CustomPackNames`), `Converters/BlockedDomainNoteConverter.cs` (NUEVO), `App.xaml`.
**Instalador:** `installer/LockSuiteSetup.iss` (`MyAppVersion` 0.4.0).
**Tests:** `tests/LockSuite.Core.Tests/DomainMatcherTests.cs` (+casos), `PresetsTests.cs` (NUEVO), `PresetSyncTests.cs` (NUEVO), `tests/LockSuite.Service.Tests/ConfigManagerTests.cs` (ajustado: `google.com` ya NO debe estar permitido; +7 tests).
**Docs:** `docs/ANALISIS_DOMINIOS_SERVICIOS.md` (NUEVO, análisis host por host), `docs/ANALISIS_VIABILIDAD.md` (§3 y §3.1), `docs/ANALISIS_INCOMODIDADES_Y_FALLAS.md` (§2.6), `ESTADO.md`.

## 1. Build + tests (usuario estándar, sin elevación)

```powershell
cd "C:\Locksuite windows"
dotnet build LockSuite.sln -c Release
dotnet test  LockSuite.sln -c Release
```
Esperado: los 96 previos (con `ConfigManagerTests.Constructor_CreatesDefaultConfig_WhenFileDoesNotExist` ajustado) **más** los nuevos: `DomainMatcherTests` (sintaxis nueva y prioridad de bloqueo), `PresetsTests` (nada de la lista "nunca" queda permitido con todos los paquetes activos; cada botón habilita lo que su servicio necesita; botones independientes), `PresetSyncTests` (migración, idempotencia, `excluded`, renombres) y los nuevos de `ConfigManagerTests` (migración desde un `whitelist.json` de v0.3.0, quitar/restituir dominios de fábrica, bloqueo fijo gana sobre `google.com`).

## 2. Publicar + empaquetar v0.4.0

```powershell
.\scripts\publish-release.ps1 -Version "0.4.0" -Notes "Botones Permitir Gmail/Chat/Drive/Fotos/prog.co.il/mitmachim.top; bloqueo fijo del buscador de Google y Gemini; migración automática de la lista blanca"
```
(o a mano como en la sesión 8 §2, cambiando 0.3.0 por 0.4.0).

## 3. Validación en VM de Windows 11 (con una cuenta de Google **de prueba**)

Preparación: instalar como admin; abrir LockSuite → Configuración → dejar **Modo Aprendizaje** activo. Antes de probar cada servicio: Bloqueados Recientes → **Vaciar historial**. Al terminar cada uno, anotar lo que quedó en Bloqueados Recientes (cada dominio conocido muestra una nota; lo que **no** tiene nota y rompe algo es lo que hay que reportar).

### 3.1 Migración desde v0.3.0 (lo más importante)
1. VM con **v0.3.0** instalada (o copiar a `%ProgramData%\LockSuite\config\whitelist.json` una config de v0.3.0) y verificar que hoy `www.google.com` **abre** (el agujero viejo).
2. Actualizar a v0.4.0 (instalador o auto-update). Abrir `whitelist.json`: ya no debe estar "Google (Gmail, Chat, Drive, Fotos)"; deben estar `gmail`, `google-chat`, `google-drive`, `google-photos`, `prog`, `mitmachim` y `kosher-block` (`"blocks": true`), con `"version": 2`.
3. `www.google.com`, `google.com.ar` y `gemini.google.com` → bloqueados. Gmail/Drive → siguen funcionando.

### 3.2 Interfaz
- Lista Blanca: 6 tarjetas (Gmail, Google Chat, Google Drive, Google Fotos, prog.co.il, mitmachim.top), en 3 columnas, que se vean bien también con la ventana al mínimo (850 px).
- "Bloquear X" / "Permitir X": pide contraseña del tutor, cambia el color/estado, y el cambio se refleja en el paquete de abajo.
- "Abrir ↗": abre el navegador en `mail.google.com/mail/`, `chat.google.com/u/0/`, `drive.google.com/drive/my-drive`, `photos.google.com/login`, `www.prog.co.il`, `mitmachim.top`.
- Paquete "Bloqueo fijo…" en rojo; destildarlo pide confirmación.
- "+ Agregar Dominio" con `google.com` → muestra el aviso de dominio amplio; si se agrega igual, `www.google.com` sigue bloqueado.
- Bloqueados Recientes → "Permitir" sobre `www.google.com` → explica que es un bloqueo fijo y no lo agrega.
- Quitar con ✕ un dominio de un paquete de fábrica (p. ej. `keep.google.com`), reiniciar el servicio (`sc stop LockSuiteService` / `sc start LockSuiteService` como admin) → sigue quitado.

### 3.3 Servicio por servicio (navegador Chrome y Edge, cuenta estándar)
- **Gmail:** iniciar sesión desde "Abrir" (incluida verificación en 2 pasos si la cuenta la tiene) · bandeja · correo con imágenes (se ven) · adjunto PDF/imagen: vista previa y descarga · redactar y enviar con adjunto · autocompletar contactos · invitación de Calendario · panel lateral (Calendario/Tareas/Keep/Contactos) · avatar → **Cerrar sesión** · Gemini/"Ayudame a escribir": no debe funcionar.
- **Google Chat:** mensajes en tiempo real entre dos cuentas · imágenes y adjuntos · el botón de GIF no debe mostrar GIFs.
- **Google Drive:** listar, subir, descargar · vista previa de PDF, imagen y Office · abrir y editar Documentos/Hojas/Presentaciones · diálogo Compartir · exportar un Documento como PDF · **video**: anotar si la vista previa reproduce (y qué host aparece bloqueado si no) · Documentos → **Insertar → Imagen → Buscar en la web**: anotar si muestra resultados (si los muestra, anotar el host para agregarlo al bloqueo fijo).
- **Google Fotos:** miniaturas y foto completa · **video** (anotar si reproduce) · subir una foto · abrir un enlace de álbum compartido.
- **prog.co.il:** portada con imágenes y carruseles · abrir un hilo (pasa la verificación "Un momento…" de Cloudflare) · iniciar sesión.
- **mitmachim.top:** portada y un tema con fuentes hebreas correctas · iniciar sesión (con cuenta existente o "Ingresar con Google").

### 3.4 Negativos kosher (todo debe quedar bloqueado, con todos los botones activos)
`www.google.com`, `google.com`, `www.google.com.ar`, `www.google.co.il`, búsqueda desde la barra de direcciones de Chrome y de Edge, `gemini.google.com`, `aistudio.google.com`, `notebooklm.google.com`, `www.youtube.com`, `play.google.com`, `news.google.com`, `maps.google.com`, `workspace.google.com`, `storage.googleapis.com`.

## 4. Al terminar
1. Si algún servicio necesitó un host que no estaba: anotarlo (host + para qué) en `ESTADO.md` → se suma a `Presets.cs` en la próxima sesión (la actualización lo propaga sola a los equipos vía `PresetSync`).
2. Actualizar `ESTADO.md` (resultados reales, nueva línea de log, próximo paso) y commit, p. ej.:
   `feat(v0.4.0): botones Permitir por servicio (Gmail/Chat/Drive/Fotos/prog/mitmachim), bloqueo fijo del buscador de Google y Gemini, migración de presets`
