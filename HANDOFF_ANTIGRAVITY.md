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
