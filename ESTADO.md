# Estado del proyecto — LockSuite Windows

Versión actual: **0.3.0 — Construida, auditada y empaquetada exitosamente (96/96 tests pasando). Incluye modo de prueba (trial), "Instalar en mi usuario" (RunAsInvoker), pestaña Ayuda/FAQ, hardening crítico de auto-update (SHA256SUMS obligatorio + ACL cerrada en updates) y mitigación de evasión de desinstalación.**
Última actualización: 2026-10-21, sesión 8 (Claude: código y auditoría; Antigravity: parches de seguridad C1/A1/A2/M2/M3, build, tests 96/96 y empaquetado Inno Setup).

## 0. Contexto y propósito (importante, leer esto primero)

LockSuite es un filtro de internet por **lista blanca** para computadoras Windows. El caso de uso es una **computadora "Kosher"**: equipos con internet filtrado para miembros de una comunidad **judía ortodoxa**, donde el internet filtrado es la norma. El primero es para un **adolescente** (control parental / del tutor sobre el equipo de un menor); la intención es generalizarlo para más familias de la comunidad que lo pidan.

Esto lo ubica en la misma categoría que productos como Techloq, Netspark o TAG: filtrado de contenido **consentido dentro del marco familiar/comunitario**, aplicado por el tutor/administrador. Es un filtro **transparente** (visible, instalado a la vista) y **local** (no envía la actividad del usuario a ningún lado): no espía, solo controla a qué dominios se puede conectar. Esa transparencia y el que sea para equipos propios de la comunidad, con la gente al tanto, es lo que lo mantiene en terreno legítimo — y marca la diferencia con las cosas que quedaron fuera de alcance (sección 3).

**Filosofía de uso (aclarada en sesión 8):** el objetivo NO es impedir que el usuario instale programas ni restringir el uso del equipo. El usuario puede instalar y usar lo que quiera. Lo único que se controla es la **red**: a qué dominios se puede conectar (lista blanca). El filtro trabaja a nivel de red (WFP + DNS), así que da igual cuántos navegadores o apps se instalen: todos quedan sujetos a la misma lista.

## 1. Qué es, en una línea

Software para Windows 11 que bloquea todo internet salvo una lista blanca de dominios, administrado por una persona con contraseña maestra; quienes usan el equipo tienen cuenta estándar sin esa contraseña.

Lista blanca de referencia: Google (Gmail/Chat/Drive/Fotos), mitmachim.top, prog.co.il, mercadopago.com.ar.

## 2. Decisiones de arquitectura (ya tomadas)

| Decisión | Resumen |
|---|---|
| Separación de cuentas | Admin (el tutor) vs. estándar (quien usa el equipo). Base de todo. |
| Bloqueo de red | Windows Filtering Platform (WFP), *default block* outbound, IPv4 e IPv6. |
| Filtrado por dominio | Resolutor DNS local: solo resuelve dominios de la lista; al resolver abre regla WFP temporal para esa IP. Resto → NXDOMAIN. |
| Filtrado extra (fase 2) | Inspección del SNI del Client Hello de TLS (nombre en texto plano) para el hueco de CDN/IP compartida. Sin descifrar, sin CA propia. |
| Servicio | Windows Service como SYSTEM; la GUI es un cliente. |
| Contraseña | Hash con sal (PBKDF2 + SHA256, 100k iteraciones), DPAPI (`LocalMachine`), límite de intentos anti-fuerza bruta. |
| Interfaz | WPF moderna: estado del sistema, lista blanca, "bloqueados recientes" (modo aprendizaje con 1 clic para permitir), config, y Ayuda/FAQ. |
| Instalador | Inno Setup: registra servicio, aplica filtros, crea directorios protegidos, deja accesos directos, página de contraseña y página de modo de prueba. |
| Instalación de programas por el usuario | El usuario estándar puede instalar programas. Opción de menú contextual "Instalar en mi usuario" (RunAsInvoker) para instaladores que piden admin sin necesitarlo. No afecta el filtro (es a nivel de red). |

## 3. Explícitamente AFUERA del alcance (y por qué)

- **Driver de kernel propio** — innecesario (WFP ya opera en kernel vía API de usuario) y caro de firmar (EV + WHQL).
- **Inspección TLS con CA propia (lo que hace Techloq)** — permitiría leer el contenido del tráfico, no solo el dominio. Mucho más riesgo e invasividad; el SNI logra el mismo filtrado sin eso.
- **Blindaje contra el propio Administrador de Windows** (anti-`sc stop`, proceso invisible/imposible de matar). En este diseño el admin (el tutor) es la parte de confianza; además eso es técnicamente indistinguible de spyware. No se construye. La protección real viene de la separación de cuentas: quien usa el equipo no tiene la cuenta admin ni la contraseña.
- **Ocultar el programa/proceso** — el programa es visible; solo la config (lista + contraseña) está protegida.

Detalle en `docs/ANALISIS_VIABILIDAD.md` y `docs/ANALISIS_TECHLOQ.md`.

## 4. Qué está hecho

### 4.1 Librería Core (`src/LockSuite.Core/`) — HECHA y probada
Multiplataforma (.NET 8).
- `DomainMatcher.cs`: Normaliza y valida hosts contra patrones y comodines.
- `SniParser.cs`: Decodifica el SNI de paquetes TLS ClientHello de forma defensiva.
- `WhitelistConfig.cs`: Modelo de configuración y persistencia JSON.
- `Presets.cs`: Paquetes predefinidos. **(Sesión 8)** Se agregó `githubusercontent.com` (pelado) para cubrir `release-assets.`/`objects.`/`raw.` y que el auto-update no se bloquee a sí mismo.
- `TrialConfig.cs`: **(Sesión 8, NUEVO)** modelo puro del modo de prueba (duración, acción al terminar, autoridad de quitar, estado derivado del reloj). Testeable en Core.
- `IpcMessages.cs` y `LockSuiteClient.cs`: Protocolo y cliente de Named Pipe. **(Sesión 8)** Se agregaron DTOs y métodos de trial (`GetTrialStatus`, `DecideTrial`, `SetTrial`).
- Tests unitarios en `tests/LockSuite.Core.Tests/`: incluyen ahora `TrialConfigTests`.

### 4.2 Servicio de Windows (`src/LockSuite.Service/`) — HECHO
Servicio de Windows y consola interactiva (.NET 8 Windows Service).
- `WfpManager.cs`, `DnsResolver.cs`, `DnsConfigurator.cs`, `SecurityManager.cs`, `ConfigManager.cs`, `LearningManager.cs`, `NamedPipeServer.cs`, `Worker.cs`, `Program.cs`: igual que v0.2.0 (ver detalle en secciones previas / `docs/REVISION_PRODUCCION.md`).
- **(Sesión 8, NUEVO)** `Trial/TrialManager.cs`: gestiona el modo de prueba (persistencia en `trial.json`, estado contra el reloj, decisión final, y lanzamiento del desinstalador de Inno para "quitar"). Fail-closed: mientras no haya decisión, el filtro sigue activo.
- **(Sesión 8)** `Ipc/NamedPipeServer.cs`: comandos `GetTrialStatus`, `DecideTrial` (conservar/quitar; quitar pide tutor solo si la prueba se configuró con `tutorlock`), `SetTrial`.
- **(Sesión 8)** `Program.cs`: comandos CLI `--set-trial <min> [prompt|autoremove] [tutorlock]` y `--trial-status`; `--install` idempotente (hace `sc config` si el servicio ya existe, para upgrades).
- **(Sesión 8)** `Worker.cs`: en el loop llama `TrialManager.CheckAutoRemove()` (solo actúa si la prueba es "autoremove" y venció).

### 4.3 Interfaz de Usuario WPF (`src/LockSuite.UI/`) — HECHA
- Navegación: Estado, Lista Blanca, Bloqueados Recientes, Configuración, **y Ayuda/FAQ (Sesión 8)**.
- **(Sesión 8)** Banner de modo de prueba en "Estado" (cuenta regresiva + decisión al terminar) y `Views/TrialEndedDialog.xaml` (ventana que pregunta Conservar/Quitar al vencer la prueba).
- **(Sesión 8)** Vista Ayuda/FAQ: explica que el usuario puede instalar programas (incluida la opción "Instalar en mi usuario"), que el filtro aplica a nivel de red igual, y cómo pedir que se habilite un sitio.

### 4.4 Sistema de Auto-Actualización (`src/LockSuite.Service/Updates/`) — HECHO y REVISADO (Sesión 8)
- `UpdateManager.cs`: consulta la API de GitHub, compara versión, descarga y ejecuta el instalador silencioso.
- **(Sesión 8) Correcciones:** verificación **SHA-256** del instalador descargado contra `SHA256SUMS.txt` del release **antes** de ejecutarlo como SYSTEM; limpieza de descargas viejas; helpers testeables. Detalle y pendientes (firma Authenticode) en `docs/REVISION_AUTOUPDATE.md`.

### 4.5 Automatización de Releases y CI/CD — HECHO y REVISADO (Sesión 8)
- `scripts/publish-release.ps1`: un comando compila, testea, empaqueta, **genera `SHA256SUMS.txt`**, commitea, pushea y crea el Release con el `.exe` y el sums adjuntos.
- `.github/workflows/build-and-release.yml`: **(Sesión 8)** fija la versión en `Program.cs` y en el `.iss` antes de compilar (consistencia), genera `SHA256SUMS.txt`, y publica el release también por `workflow_dispatch` (`tag_name` explícito).

### 4.6 Instalador (`installer/LockSuiteSetup.iss`) — HECHO y ACTUALIZADO (Sesión 8)
- Página de contraseña del tutor (v0.2.0).
- **(Sesión 8)** `PrepareToInstall`: detiene el servicio y cierra la GUI antes de copiar binarios (necesario para que la auto-actualización pueda sobrescribir los `.exe` en uso).
- **(Sesión 8)** Página opcional de **modo de prueba** (activar, duración 1–96 h, "quitar automáticamente al terminar", "pedir contraseña del tutor para quitar").
- **(Sesión 8)** `[Registry]` opcional (tarea marcable) del menú contextual **"Instalar en mi usuario (sin administrador)"** (RunAsInvoker) para `.exe`.
- **(Sesión 8)** `[UninstallDelete]` de `%ProgramData%\LockSuite` para que "quitar" borre también la config (lista, auth, trial).
- Guard: en modo silencioso (auto-update) no re-pide contraseña ni reinicia la prueba.

### 4.7 Pruebas Automatizadas — 96/96 superadas (100% verde)
- `LockSuite.Core.Tests`: 49 tests (DomainMatcher, SniParser, TrialConfig, etc.).
- `LockSuite.Service.Tests`: 47 tests (DnsPacket, NXDOMAIN, TTL/IP parsing, WfpManager, SecurityManager/DPAPI/Lockout, ConfigManager, LearningManager, IpcProtocol, UpdateManager + SHA256 integrity, TrialManager, TrialIpc).

### 4.8 Auditoría y Parches de Seguridad Aplicados (Sesión 8)
- **C1 & M2:** ACL restringida en `[Dirs]` del instalador (`system-full admins-full` en `{commonappdata}\LockSuite`, `config`, `logs`, `updates`). Cierra la posibilidad de escalada usuario estándar → SYSTEM por TOCTOU y previene la lectura del hash `auth.dat` por usuarios locales.
- **A1:** Protección contra desinstalación prematura no autorizada en modo prueba (`DecideTrial(keep: false)` rechazado antes del vencimiento a menos que se presente autenticación del tutor).
- **A2:** Recarga dinámica por timestamp (`ReloadIfChanged`) en `TrialManager` para que `--set-trial` impacte en el servicio en ejecución en menos de 10s sin requerir reinicio.
- **M3:** Verificación de integridad `SHA256SUMS.txt` convertida en obligatoria para auto-updates en v0.3.0+.

## 5. Explícitamente afuera del alcance
Igual que v0.2.0 (sin driver .sys, sin MITM/CA, sin blindaje anti-admin). Ver `docs/ANALISIS_VIABILIDAD.md`.

## 6. Modo de prueba (trial) — cómo funciona (Sesión 8)

- Lo activa el **tutor** al instalar (página del instalador) o por CLI. Elige la **duración** (1–96 h; o cualquier cantidad de minutos por CLI).
- Durante la prueba, LockSuite filtra normalmente. En "Estado" se ve una cuenta regresiva.
- **Al terminar** (comportamiento por defecto, `prompt`): aparece una ventana que pregunta **"¿Conservar LockSuite de forma permanente o quitarlo?"**. La decide quien está probando (el padre).
  - **Conservar** → queda permanente; desde ahí **solo el administrador** lo saca (desinstalador, que pide admin).
  - **Quitar** → se desinstala por completo y se restaura internet sin filtro.
  - **Fail-closed**: si nadie decide, LockSuite **sigue activo** (no se borra solo).
- Opciones del tutor al configurar la prueba:
  - `autoremove`: al terminar se quita **solo**, sin preguntar (pensado para una **prueba descartable**, p. ej. probar 10 minutos en una VM y que se limpie sola).
  - `tutorlock`: quitar al terminar **exige la contraseña del tutor** (por si se prueba en el equipo de un menor y no se quiere que el usuario estándar lo quite).
- Prueba descartable de 10 minutos (lo que pediste para probar en tu PC): instalar y luego, como admin,
  `"C:\Program Files\LockSuite\service\LockSuite.Service.exe" --set-trial 10 autoremove`.
  A los 10 minutos el servicio lanza el desinstalador silencioso y limpia todo. **Hacelo en una VM, no en la PC de uso diario** (un filtro default-deny corta internet durante la prueba).

## 7. Log de sesiones

### Sesión 1 — 2026-10-20 (diseño)
Arquitectura completa, alcance, análisis de Techloq, sistema de documentación y prompt maestro. Commit inicial.

### Sesión 2 — 2026-10-20 (Core)
Librería Core (.NET 8): DomainMatcher, SniParser, WhitelistConfig, Presets + tests xUnit.

### Sesión 3 — 2026-10-20 (contexto + revisión + handoff)
Contexto Kosher documentado. Corrección de presets y creación de `HANDOFF_ANTIGRAVITY.md`.

### Sesión 4 — 2026-10-21 (Implementación Windows completa — Antigravity)
Servicio Windows (WFP, DNS, DPAPI, IPC), GUI WPF, instalador, 49/49 tests.

### Sesión 5 — 2026-10-21 (Revisión para producción — Claude)
Hardening de concurrencia, anti-evasión, WFP idempotente, perf, UX. Detalle en `docs/REVISION_PRODUCCION.md`.

### Sesión 6 — 2026-10-21 (Build, 70 tests, empaquetado v0.2.0 — Antigravity)
Compilación Release, 70/70 tests, publish self-contained, instalador v0.2.0 (73.6 MB).

### Sesión 7 — 2026-10-21 (Auto-update + CI + release v0.2.0 — Antigravity)
`UpdateManager`, `publish-release.ps1`, workflow CI, repo y release v0.2.0 publicados. 73/73 tests.

### Sesión 8 — 2026-10-21 (Trial + RunAsInvoker + FAQ + revisión auto-update — Claude, sin terminal Windows)
- **Tarea 1:** menú contextual "Instalar en mi usuario (sin administrador)" (RunAsInvoker) en el instalador + vista Ayuda/FAQ en la UI. (Se descartó la idea de restringir instalaciones: el filtro es a nivel de red, instalar apps no lo debilita.)
- **Tarea 2:** revisión a fondo del auto-update. Se corrigieron 6 puntos (el servicio no se detenía en upgrade → binarios en uso; no se verificaba integridad del `.exe` ejecutado como SYSTEM → ahora SHA-256; `release-assets.githubusercontent.com` no estaba cubierto; versión inconsistente entre script y CI; `workflow_dispatch` no publicaba; `--install` no idempotente). Detalle en `docs/REVISION_AUTOUPDATE.md`.
- **Tarea 3:** modo de prueba (trial) configurable con decisión del tutor al terminar (conservar/quitar), + `autoremove` (prueba descartable 10 min) y `tutorlock` opcionales. Fail-closed.
- Todo aplicado **en código**; compilación, tests y empaquetado a cargo de Antigravity (ver HANDOFF). Versión subida a 0.3.0 en `Program.cs` y `.iss`.

---

## 8. Próximo paso

**Para Antigravity (tiene terminal Windows):** compilar, testear y empaquetar **v0.3.0** siguiendo `HANDOFF_ANTIGRAVITY.md` (sección "Sesión 8"):
1. `dotnet build` y `dotnet test` de toda la solución. Deben pasar los 73 previos **más** los nuevos (TrialConfig, TrialManager, TrialIpc, hash del updater). Si algo no compila, es de esta sesión: reportar archivo/línea.
2. `publish` self-contained + empaquetar con Inno Setup → `bin\installer\LockSuiteSetup-0.3.0.exe`, y generar `SHA256SUMS.txt` (lo hace `scripts\publish-release.ps1`).
3. Validar en **VM limpia**:
   - La vista Ayuda/FAQ y el menú contextual "Instalar en mi usuario" (probar con un instalador per-user, p. ej. un navegador portable, que NO salte UAC y se instale en AppData).
   - Modo de prueba: instalar con una prueba corta (o `--set-trial 10 autoremove`), confirmar cuenta regresiva, y que al vencer (a) con `autoremove` se desinstala solo y limpia WFP/NRPT/config; (b) con `prompt` aparece la ventana y "Quitar" desinstala, "Conservar" deja permanente.
   - Auto-update: publicar un release de prueba mayor con `SHA256SUMS.txt` y verificar que el equipo se actualiza solo (incluye la verificación de hash y el reemplazo de binarios con el servicio detenido).
4. Si todo pasa en verde, `publish-release.ps1 -Version 0.3.0` (o commit + tag) y actualizar este `ESTADO.md` (resultados reales + nueva línea de log).

**Nota de limpieza:** quedó un archivo vacío `.ls_write_test` en la raíz (lo dejó la sesión de Claude probando escritura; el entorno no permitía borrarlo). Ya está en `.gitignore`; Antigravity puede borrarlo con `del .ls_write_test`.
