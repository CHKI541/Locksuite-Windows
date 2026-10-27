# Estado del proyecto — LockSuite Windows

Versión actual: **0.5.0 — Construida, testeada al 100% (436/436 tests), empaquetada y publicada en GitHub Releases.** Corrige el bug crítico de marshalling de puntero en WFP (`FWP_VALUE0.uint64`) que provocaba la caída al "Modo simulación", cierra vectores de evasión (`DOTNET_STARTUP_HOOKS`, herencia débil en ACLs de ProgramData, túneles UDP en puertos NTP/DHCP), agrega verificación de TransactionId y conexión en DNS upstream, guarda contra sufijos públicos peligrosos (`co.il`, `com.ar`, etc.), y activa la actualización automática de clientes.
Versión anterior: **0.4.1 — Construida, testeada al 100% (372/372 tests) y empaquetada.**
Última actualización: 2026-10-27, sesión 13 (Auditoría de seguridad y evasión, corrección de marshalling en WFP, hardening de ACLs/IPC/DNS, 436 tests en verde y publicación de Release v0.5.0 para auto-actualización).

## 0. Contexto y propósito (importante, leer esto primero)

LockSuite es un filtro de internet por **lista blanca** para computadoras Windows. El caso de uso es una **computadora "Kosher"**: equipos con internet filtrado para miembros de una comunidad **judía ortodoxa**, donde el internet filtrado es la norma. El primero es para un **adolescente** (control parental / del tutor sobre el equipo de un menor); la intención es generalizarlo para más familias de la comunidad que lo pidan.

Esto lo ubica en la misma categoría que productos como Techloq, Netspark o TAG: filtrado de contenido **consentido dentro del marco familiar/comunitario**, aplicado por el tutor/administrador. Es un filtro **transparente** (visible, instalado a la vista) y **local** (no envía la actividad del usuario a ningún lado): no espía, solo controla a qué dominios se puede conectar. Esa transparencia y el que sea para equipos propios de la comunidad, con la gente al tanto, es lo que lo mantiene en terreno legítimo — y marca la diferencia con las cosas que quedaron fuera de alcance (sección 3).

**Filosofía de uso (aclarada en sesión 8):** el objetivo NO es impedir que el usuario instale programas ni restringir el uso del equipo. El usuario puede instalar y usar lo que quiera. Lo único que se controla es la **red**: a qué dominios se puede conectar (lista blanca). El filtro trabaja a nivel de red (WFP + DNS), así que da igual cuántos navegadores o apps se instalen: todos quedan sujetos a la misma lista.

## 1. Qué es, en una línea

Software para Windows 11 que bloquea todo internet salvo una lista blanca de dominios, administrado por una persona con contraseña maestra; quienes usan el equipo tienen cuenta estándar sin esa contraseña.

Lista blanca de referencia: Gmail, Google Chat, Google Drive, Google Fotos, mitmachim.top, prog.co.il (cada uno con su botón "Permitir…"), mercadopago.com.ar. El buscador de Google y Gemini quedan bloqueados siempre (bloqueo fijo con prioridad).

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
| Servicios de Google (sesión 9) | Un paquete por servicio con hosts puntuales. **Nunca** `google.com`, `googleapis.com`, `gstatic.com` ni `googleusercontent.com` enteros (abren el buscador, Gemini, almacenamiento público, etc.). Cada paquete trae el login y los estáticos, así cada botón funciona solo. Análisis host por host: `docs/ANALISIS_DOMINIOS_SERVICIOS.md`. |
| Bloqueo con prioridad (sesión 9) | Un paquete puede ser de bloqueo (`blocks: true`): sus hosts quedan bloqueados aunque otro paquete los permita. De fábrica: "Bloqueo fijo: buscador de Google y Gemini" (incluye el backend de Gemini dentro de Gmail/Docs, `appsgenaiserver-pa.clients6.google.com`). |
| Patrones (sesión 9) | `x.com` = sitio + subdominios; `*.x.com` = solo subdominios; `=x.com` = solo ese host; `lh*.x.com` = comodín dentro de una sola etiqueta (nunca cruza un punto). |
| Paquetes de fábrica (sesión 9) | Tienen `id` estable. `PresetSync` los migra y actualiza al cargar la config: lo que el tutor quitó queda en `excluded` (no vuelve) y lo que agregó se mueve a "Personalizados". |

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
- `Presets.cs`: Paquetes predefinidos. **(Sesión 8)** Se agregó `githubusercontent.com` (pelado) para cubrir `release-assets.`/`objects.`/`raw.` y que el auto-update no se bloquee a sí mismo. **(Sesión 9)** Reescrito como catálogo de paquetes por servicio + bloqueo fijo (ver 4.9).
- `PresetSync.cs`: **(Sesión 9, NUEVO)** migración y sincronización de los paquetes de fábrica.
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

### 4.9 Botones "Permitir" por servicio + bloqueo fijo del buscador y Gemini (Sesión 9, v0.4.0) — EN CÓDIGO, sin compilar
**Qué se construyó:**
- **Análisis exhaustivo de dominios** de Gmail, Google Chat, Google Drive (con Documentos/Hojas/Presentaciones/Formularios), Google Fotos, prog.co.il y mitmachim.top: listas oficiales de Google para firewalls + captura real en navegador (recursos, iframes y scripts de cada sitio; login de Google; un Documento y una Presentación públicos) + fuentes secundarias. Tabla host por host, qué se incluye, qué se excluye a propósito y riesgos residuales en **`docs/ANALISIS_DOMINIOS_SERVICIOS.md`**.
- **Hallazgo crítico corregido:** el paquete viejo "Google (Gmail, Chat, Drive, Fotos)" habilitaba `google.com`, `googleapis.com`, `gstatic.com`, `googleusercontent.com` enteros y `google.com.ar` → **el buscador de Google y Gemini estaban abiertos**. Ahora no.
- **Core:** `DomainMatcher` (sintaxis `=host` y comodín de una etiqueta; bloqueos con prioridad), `WhitelistConfig` (`id`, `blocks`, `excluded`; `version` 2), `Presets` reescrito como catálogo (paquetes Gmail, Google Chat, Google Drive, Google Fotos, prog.co.il, mitmachim.top + "Bloqueo fijo: buscador de Google y Gemini" + notas para "Bloqueados recientes"), `PresetSync` (NUEVO: migración idempotente de configs v0.2/v0.3).
- **Servicio:** `ConfigManager` aplica permitidos − bloqueos en el camino caliente del DNS (snapshot inmutable), migra al cargar, y maneja quitar/restituir dominios de paquetes de fábrica.
- **UI:** en "Lista Blanca", sección **"Servicios con un clic"** con 6 tarjetas: botón **"Permitir Gmail"** / **"Bloquear Gmail"** (y lo mismo para cada servicio), estado, aviso de qué queda afuera, y botón **"Abrir ↗"** que entra directo al login. Paquetes de bloqueo en rojo (desactivarlos pide confirmación). "Bloqueados Recientes" muestra una **nota** por dominio conocido (p. ej. "YouTube: bloqueado a propósito", "Publicidad: no hace falta"). "Permitir" sobre algo del bloqueo fijo explica y no lo agrega. Agregar `google.com` a mano avisa. 3 FAQ nuevas.
- Versión subida a **0.4.0** en `Program.cs` y `.iss`.

**Qué se probó:**
- `dotnet test`: **365/365 tests pasando (100% verde)** — 312 Core + 53 Service (validación de matcher `=host`, comodines de una etiqueta, PresetSync, notas de dominios, bloqueo prioritario).
- Publicación autocontenida win-x64 y empaquetado Inno Setup generado: `LockSuiteSetup-0.4.0.exe` (70.32 MB).
- Publicado en GitHub Releases: `v0.4.0` con `SHA256SUMS.txt`.
- **Próximo paso de validación:** Probar en VM con una cuenta de Google de prueba (checklist en `HANDOFF_ANTIGRAVITY.md`, sección "Sesión 9").

**Riesgos residuales conocidos** (detalle en el análisis §11): IPs compartidas de Google sin SNI (un usuario técnico podría forzar el buscador → fase 2 SNI); lo que le compartan al usuario en Gmail/Drive/Fotos/Chat se ve; "Insertar imagen → Buscar en la web" de Documentos a verificar; videos de Drive/Fotos podrían no reproducirse (si usan infraestructura de YouTube); el registro de cuentas nuevas en mitmachim.top usa reCAPTCHA de `www.google.com` (bloqueado; entrar con cuenta existente funciona); "Esenciales del sistema" deja `github.com` abierto para todos (lo usa el auto-update).

### 4.10 Fallas de UX/sistema y certificados (Sesión 11, v0.5.0) — EN CÓDIGO, sin compilar
Se revisó `docs/ANALISIS_INCOMODIDADES_Y_FALLAS.md` punto por punto contra el código y se corrigió lo que faltaba (el documento ahora dice, en cada caso, **cómo** se corrigió y en qué versión). Resumen:
- **Certificados (sección nueva del doc):** a "Esenciales del sistema" se sumaron los hosts de PKI (OCSP/CRL/intermedios) de las CAs más usadas — Let's Encrypt (`lencr.org`, antes faltaba), Amazon (`amazontrust.com`), DigiCert, Sectigo/Comodo/USERTrust, Google Trust Services, SSL.com/Certum, GlobalSign, Entrust, IdenTrust, Microsoft, GoDaddy/Starfield — y `ctldl.windowsupdate.com` (raíces de Windows). Las **3 CAs de Cloudflare** quedan cubiertas. Paquete nuevo **"Actualizaciones de navegadores y sus certificados"** (Chrome Root Store/CRLSet, Edge, Firefox intermediate preloading/CRLite). "Bloqueados recientes" reconoce hosts `ocsp*`/`crl*` y los explica.
- **DoH:** el instalador desactiva el "DNS seguro" de Chrome/Edge/Firefox por directiva de registro.
- **Windows Update / Defender:** paquete nuevo (activado por defecto) con los hosts oficiales; no incluye Tienda/Bing/MSN/Copilot.
- **Recursos web comunes** (fuentes/CDNs) y **Captchas** (Turnstile/hCaptcha/recaptcha.net): paquetes nuevos activados por defecto (páginas que no se rompen; logins con captcha).
- **Portal cautivo (Wi-Fi de hotel):** modo con tutor y autovencimiento que abre solo la red local y suspende NRPT, sin apagar el filtro.
- **Red local:** opción (tutor, off por defecto) para permitir direcciones privadas (impresora/router/NAS) sin abrir internet.
- **Caché DNS negativa:** `ipconfig /flushdns` al cambiar la config (un dominio recién permitido abre enseguida).
- **Puerto 53 ocupado:** cartel claro en la GUI (`IsDnsDown`), además de no aplicar NRPT (ya en v0.4.1).
- **IPv6 / reloj:** permisos base nuevos DHCPv6 (UDP 547) y NTP (UDP 123).
- **Riesgos residuales honestos:** IPs compartidas sin SNI (fase 2), portal cautivo en IP pública, reloj desfasado muchas horas, `github.com` abierto para todos.

Pendiente de Antigravity: compilar, correr tests (372 previos + nuevos), empaquetar v0.5.0 y validar en VM (ver `HANDOFF_ANTIGRAVITY.md` §11).

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

### Sesión 9 — 2026-10-21 (Botones "Permitir" por servicio + bloqueo fijo del buscador y Gemini — Claude, sin terminal Windows; tarea programada)
- Análisis exhaustivo de dominios de Gmail, Google Chat, Google Drive, Google Fotos, prog.co.il y mitmachim.top (listas oficiales de Google + captura en navegador + fuentes secundarias) → `docs/ANALISIS_DOMINIOS_SERVICIOS.md`.
- Corregido el agujero kosher del paquete viejo de Google (`google.com` entero = buscador y Gemini abiertos).
- Un paquete y una tarjeta por servicio ("Permitir Gmail", etc., con "Abrir"), paquete "Bloqueo fijo: buscador de Google y Gemini" con prioridad, sintaxis `=host` y `lh*.x.com`, migración automática (`PresetSync`), notas en "Bloqueados Recientes", FAQ.
- Tests nuevos/ajustados en Core y Service; lógica verificada con simulación en Python (todo en verde). Versión 0.4.0.

### Sesión 10 — 2026-10-21 (Fix Named Pipe Deadlock, UI Startup Guard, Inno x64, 372 Tests y Release v0.4.1 — Antigravity)
- **Diagnóstico y solución del fallo de apertura de la UI:**
  1. **Deadlock en Named Pipe IPC:** `StreamWriter(pipe, Encoding.UTF8)` con `AutoFlush=true` emitía el preámbulo UTF-8 BOM sincrónicamente al instanciarse en ambos lados (cliente y servidor) con búfer Win32 de 0 bytes, bloqueando el pipe de forma cruzada. Solución: codificación canónica UTF8 sin BOM (`new UTF8Encoding(false)`), `AutoFlush=false` con `FlushAsync()` explícito, timeout de inactividad de 30s en servidor y manejo de `OperationCanceledException`.
  2. **Cascada de timeouts en arranque de UI:** En `MainViewModel.cs`, si el servicio aún no contestaba al primer llamado de estado, el código encadenaba llamadas a `GetRecentBlockedAsync`, `RefreshTrialAsync` y `ReloadConfigAsync`, acumulando más de 12 segundos de bloqueo sincrónico. Se implementó guard de concurrencia con `Interlocked.Exchange` y retorno inmediato si `status == null`.
  3. **Instalador Inno Setup:** Se agregaron directivas `ArchitecturesAllowed=x64compatible` y `ArchitecturesInstallIn64BitMode=x64compatible` para que el servicio se instale en el directorio nativo de 64 bits (`C:\Program Files\LockSuite`). Se agregó flag `runasoriginaluser` en la directiva `[Run]` para que la UI se lance con el token de usuario estándar interactivo y no con elevación oculta.
  4. **DNS Socket Error 10054:** Se configuró el control de socket `SIO_UDP_CONNRESET` en `DnsResolver.cs` para evitar excepciones por paquetes ICMP Port Unreachable de clientes locales cerrados.
  5. **Suite de pruebas de integración de IPC:** Se implementó `tests/LockSuite.Service.Tests/IpcRoundTripTests.cs` validando el ciclo de vida completo de NamedPipe (Ping, GetStatus, tolerancia a UTF-8 BOM, manejo de líneas inválidas y fallo rápido si el servicio no está corriendo).
- **Resultados:** 372/372 tests pasando al 100% (312 Core + 60 Service).
- **Empaquetado:** Generado `LockSuiteSetup-0.4.1.exe` (70.33 MB) con su respectivo `SHA256SUMS.txt`, y copiado a la unidad USB (`F:\Locksuite windows`).

### Sesión 11 — 2026-10-22 (Certificados + fallas de UX/sistema → v0.5.0 — Claude, sin terminal Windows)
- Verificación punto por punto de `docs/ANALISIS_INCOMODIDADES_Y_FALLAS.md` contra el código real (varios ítems marcados "✅ Resuelto" solo estaban a medias: los presets "Recursos web comunes" y "Captcha" no existían; DoH y portal cautivo estaban pendientes; el puerto 53 solo mostraba un renglón, no un cartel).
- **Problemas de certificado** (a pedido del usuario, no solo Cloudflare): OCSP/CRL/intermedios de las CAs grandes en Esenciales (incl. `lencr.org` y `amazontrust.com`, que faltaban), raíces de Windows, y paquete de actualización de las listas de certificados de los navegadores.
- DoH off por directiva; paquetes Windows Update/Defender, navegadores, recursos web y captchas; portal cautivo; red local; flush DNS; permisos base NTP y DHCPv6; cartel de DNS caído.
- Todo **en código** + tests nuevos de Core/Service. Versión subida a 0.5.0.

### Sesión 13 — 2026-10-27 (Auditoría de seguridad y evasión, bugfix de WFP, hardening y publicación Release v0.5.0)
- **Diagnóstico y solución raíz de la caída a "Modo Simulación":**
  - Se identificó la discrepancia en `FWP_VALUE0.uint64` (`NativeMethods.cs`): la cabecera nativa de WFP (`fwpstypes.h`) define `UINT64* uint64` (puntero a `uint64`), a diferencia de `FWP_CONDITION_VALUE0.uint64` que almacena el valor directo. Al pasar pesos enteros directos (ej. 50, 100), `FwpmFilterAdd0` desreferenciaba direcciones inválidas (`0x32`, `0x64`), lanzando excepción y haciendo que `WfpManager.Initialize()` cayera en modo simulación (`_isSimulationMode = true`).
  - Solución: se implementó helper `CreateWeight(ulong weight, out IntPtr allocPtr)` con `Marshal.AllocHGlobal`, asignando punteros válidos y liberándolos tras la llamada en los 9 filtros base y dinámicos.
- **Hardening de seguridad y mitigación de evasión:**
  - **Inyección por `DOTNET_STARTUP_HOOKS`:** Se configuró `<RuntimeHostConfigurationOption Include="System.StartupHookProvider.IsSupported" Value="false" />` en `LockSuite.Service.csproj`, impidiendo la inyección de DLLs no autorizadas para aprovechar el `ALE_APP_ID` del servicio.
  - **Permisos débiles en `%ProgramData%\LockSuite`:** Se creó `AclHelper.cs` (llamado en `Worker.StartAsync`) y se actualizó `LockSuiteSetup.iss` para deshabilitar la herencia de NTFS (`/inheritance:r`) y conceder Control Total exclusivo a `SYSTEM` (`*S-1-5-18`) y `Administrators` (`*S-1-5-32-544`), protegiendo `auth.dat`, `whitelist.json` y el directorio de updates contra manipulación o lectura de usuarios estándar.
  - **Evasión por puertos UDP permitidos:** En `TryAddPortPermit` y `AddDhcpPermitFilter`, se agregaron condiciones restrictivas de puerto de origen (`FWPM_CONDITION_IP_LOCAL_PORT == PORT_NTP` [123], `PORT_DHCP_CLIENT` [68], `PORT_DHCPV6_CLIENT` [546]), cerrando túneles VPN/UDP hacia esos puertos desde puertos efímeros de usuario.
  - **Protección DNS Upstream:** En `DnsResolver.cs`, el socket se conecta directamente al upstream (`ConnectAsync`) y se valida que el `TransactionId` de la respuesta coincida exactamente con el de la consulta, mitigando spoofing e inyección de IPs en WFP.
  - **Validación de sufijos públicos:** En `DomainMatcher.IsValidPattern`, se agregó una lista exhaustiva de sufijos públicos y TLDs multi-parte (`co.il`, `com.ar`, `co.uk`, etc.) para impedir que un patrón bare abra todos los subdominios de un país.
  - **Saneamiento en `SaveConfig`:** En IPC `NamedPipeServer.cs` y `ConfigManager.UpdateConfig`, se verifica y filtra cualquier patrón inválido o peligroso en paquetes personalizados.
- **Resultados:** **436/436 tests pasando al 100% (verde)** (373 Core + 63 Service).
- **Publicación:** Compilación de binarios autocontenidos, generación de instalador Inno Setup (`LockSuiteSetup-0.5.0.exe`), cálculo de hash SHA-256 en `SHA256SUMS.txt`, y publicación de GitHub Release `v0.5.0` para despliegue de actualización automática a usuarios.

---

## 8. Próximo paso

1. Monitorear el despliegue automático de la actualización v0.5.0 en clientes con v0.4.x.
2. Evaluar la fase 2 (inspección de **SNI**) de `docs/ANALISIS_VIABILIDAD.md` §2.4.
