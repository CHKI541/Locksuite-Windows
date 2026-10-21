# Estado del proyecto — LockSuite Windows

Versión actual: **0.2.0 — Repositorio sincronizado en GitHub, Release v0.2.0 publicado, sistema de auto-actualización y pipeline automatizado (73/73 tests pasando).**
Última actualización: 2026-10-21, sesión 7 (Antigravity).

## 0. Contexto y propósito (importante, leer esto primero)

LockSuite es un filtro de internet por **lista blanca** para computadoras Windows. El caso de uso es una **computadora "Kosher"**: equipos con internet filtrado para miembros de una comunidad **judía ortodoxa**, donde el internet filtrado es la norma. El primero es para un **adolescente** (control parental / del tutor sobre el equipo de un menor); la intención es generalizarlo para más familias de la comunidad que lo pidan.

Esto lo ubica en la misma categoría que productos como Techloq, Netspark o TAG: filtrado de contenido **consentido dentro del marco familiar/comunitario**, aplicado por el tutor/administrador. Es un filtro **transparente** (visible, instalado a la vista) y **local** (no envía la actividad del usuario a ningún lado): no espía, solo controla a qué dominios se puede conectar. Esa transparencia y el que sea para equipos propios de la comunidad, con la gente al tanto, es lo que lo mantiene en terreno legítimo — y marca la diferencia con las cosas que quedaron fuera de alcance (sección 3).

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
| Interfaz | WPF moderna: estado del sistema, lista blanca, "bloqueados recientes" (modo aprendizaje con 1 clic para permitir), config. |
| Instalador | Inno Setup: registra servicio, aplica filtros, crea directorios protegidos, deja accesos directos. |

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
- `Presets.cs`: Paquetes predefinidos (Esenciales del sistema, Google, Mercado Pago, Sitios de la comunidad).
- `IpcMessages.cs` y `LockSuiteClient.cs`: Protocolo y cliente de Named Pipe fuertemente tipado.
- Tests unitarios en `tests/LockSuite.Core.Tests/`: **26/26 tests superados** (35 ms).

### 4.2 Servicio de Windows (`src/LockSuite.Service/`) — HECHO y probado
Servicio de Windows y consola interactiva (.NET 8 Windows Service).
- `WfpManager.cs`: Integración Win32 P/Invoke con WFP (`fwpuclnt.dll`). Registra la subcapa propia (`LockSuiteSubLayerGuid`, peso 0xEE00), instala filtros base persistentes (Loopback IPv4/IPv6, DHCP, DNS al resolutor local, AppId del ejecutable para salida upstream, y política terminante default-deny outbound). Gestiona reglas dinámicas temporales para IPs autorizadas con limpieza por TTL. Incluye modo seguro con auto-rollback automático en 30s (`--test-wfp`) y modo simulación cuando corre sin elevación.
- `DnsResolver.cs`: Resolutor DNS en `127.0.0.1:53` (UDP). Si el dominio está permitido en `WhitelistConfig`, consulta a servidores upstream (1.1.1.1 y 8.8.8.8), extrae IPs y TTL, abre la regla temporal en WFP y responde al cliente. Si está bloqueado, genera respuesta sintética NXDOMAIN y lo registra en el modo aprendizaje.
- `DnsConfigurator.cs`: Redirección DNS del sistema mediante reglas NRPT (Name Resolution Policy Table) de Windows para el namespace `.`.
- `SecurityManager.cs`: Hashing con sal criptográfica usando PBKDF2 (SHA256, 100.000 iteraciones), almacenamiento cifrado con DPAPI (`DataProtectionScope.LocalMachine` en `%ProgramData%\LockSuite\config\auth.dat`), emisión de tokens de sesión temporales (15 min) y bloqueo anti-fuerza bruta tras 5 intentos fallidos (60s) y 10 intentos (5 min).
- `ConfigManager.cs`: Gestión atómica del archivo de lista blanca (`whitelist.json`), con recarga y eventos en tiempo real.
- `LearningManager.cs`: Buffer circular persistente de consultas bloqueadas (`recent_blocked.json`).
- `NamedPipeServer.cs`: Servidor IPC en `\\.\pipe\LockSuiteServicePipe` con validación de tokens y soporte multi-instancia.
- `Worker.cs` y `Program.cs`: Orquestación del ciclo de vida del servicio con soporte para `--install`, `--uninstall`, `--start`, `--stop`, `--run` y `--test-wfp`.

### 4.3 Interfaz de Usuario WPF (`src/LockSuite.UI/`) — HECHA y compilada
Aplicación de escritorio moderna para Windows 11 (.NET 8 WPF).
- Navegación por barra lateral (Estado, Lista Blanca, Bloqueados Recientes, Configuración).
- Vista Estado: Indicadores visuales de protección activa, tarjetas con conteo de dominios, reglas dinámicas WFP activas, estadísticas DNS y detalles técnicos.
- Vista Lista Blanca: Visualización de paquetes con conmutadores (toggle switches), desglose de dominios y opciones para agregar y eliminar dominios.
- Vista Bloqueados Recientes: Lista en tiempo real de consultas bloqueadas con botón "Permitir" de un solo clic.
- Modal de autenticación del tutor para proteger cambios críticos.
- Opciones de cambio de contraseña maestra y prueba segura de filtros WFP (30s).

### 4.4 Sistema de Auto-Actualización (`src/LockSuite.Service/Updates/`) — HECHO y probado
- `UpdateManager.cs`: Motor en segundo plano integrado al Windows Service. Consulta periódicamente (y al iniciar) `https://api.github.com/repos/CHKI541/Locksuite-Windows/releases/latest`. Si detecta una versión remota mayor a la instalada, descarga el instalador silenciosamente a `%ProgramData%\LockSuite\updates` y lo ejecuta con flags `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART`. Inno Setup detiene el servicio viejo, actualiza los binarios en `C:\Program Files\LockSuite` y arranca el nuevo servicio, todo sin intervención del usuario.
- `Presets.cs`: Inclusión de dominios de actualización (`api.github.com`, `github.com`, `objects.githubusercontent.com`, `raw.githubusercontent.com`) en `SystemEssentials` para permitir la resolución y descarga sin abrir sitios no autorizados.

### 4.5 Automatización de Releases y CI/CD — HECHO
- `scripts/publish-release.ps1`: Script PowerShell interactivo de publicación. En un solo comando (`.\scripts\publish-release.ps1 -Version X.Y.Z`), ejecuta los tests (100% verde), compila la publicación autocontenida win-x64, compila el instalador con Inno Setup, comitea, pushea a GitHub y crea el Release con el `.exe` adjunto vía `gh release create`.
- `.github/workflows/build-and-release.yml`: Workflow para GitHub Actions que compila, testea y publica releases directamente desde la nube de GitHub.
- Repositorio y Release: Proyecto vinculado a `https://github.com/CHKI541/Locksuite-Windows` con el Release `v0.2.0` publicado con su instalador de 73.6 MB adjunto.

### 4.6 Pruebas Automatizadas — 73/73 superadas (100% verde)
- `LockSuite.Core.Tests`: 43 tests (DomainMatcher, SniParser, etc.).
- `LockSuite.Service.Tests`: 30 tests (DnsPacket, NXDOMAIN, TTL/IP parsing, WfpManager, SecurityManager/DPAPI/Lockout, ConfigManager, LearningManager, IpcProtocol, UpdateManager).

## 5. Qué sigue (en orden)

1. **Prueba en máquina virtual (VM) limpia de Windows 11:**
   - Descargar el instalador directo desde GitHub Releases: `https://github.com/CHKI541/Locksuite-Windows/releases/latest/download/LockSuiteSetup-0.2.0.exe`.
   - Ejecutar la instalación, configurar la contraseña maestra del tutor durante el wizard.
   - Probar navegación en cuenta estándar:
     - Dominios permitidos (Google, Mercado Pago, etc.) deben resolver y cargar.
     - Dominios bloqueados deben devolver NXDOMAIN y aparecer en "Bloqueados recientes".
     - Validar que el botón "Permitir" en la GUI los desbloquea al ingresar la contraseña del tutor.
   - Probar auto-actualización cuando se publique una versión posterior.
2. **Fase 2 (Inspección SNI opcional):**
   - Si se detectan casos de evasión por IP compartida en CDNs (Cloudflare), enganchar `SniParser` en la capa de flujo WFP para inspeccionar el ClientHello TLS.

## 6. Preguntas abiertas y estado del entorno

1. ~~¿Para quiénes son las computadoras?~~ **RESPONDIDA:** computadoras Kosher para una comunidad judía ortodoxa; la primera para un adolescente; se busca generalizar.
2. ~~¿Qué hay en la máquina de desarrollo?~~ **RESPONDIDA:** SDK de .NET 8 (8.0.424) instalado con runtimes Core, AspNetCore y WindowsDesktop. Compilación y publicación funcionan sin requerir software externo adicional. Inno Setup puede compilarse con `iscc` instalándolo vía `winget install JRSoftware.InnoSetup` o en una máquina de empaquetado.
3. **Firma de código:** por ahora se prueba sin firmar o con certificado autofirmado para pruebas de laboratorio en VM.

## 7. Log de sesiones

### Sesión 1 — 2026-10-20 (diseño)
Arquitectura completa, alcance, análisis de Techloq, sistema de documentación y prompt maestro. Commit inicial.

### Sesión 2 — 2026-10-20 (Core)
Escrita la librería Core (.NET 8): DomainMatcher, SniParser, WhitelistConfig, Presets + tests xUnit. Lógica crítica validada con prototipo en Python.

### Sesión 3 — 2026-10-20 (contexto + revisión + handoff)
Se aclaró y documentó el contexto (computadora Kosher para comunidad judía ortodoxa; primero un adolescente; se busca generalizar). Corrección en presets y creación de `HANDOFF_ANTIGRAVITY.md`.

### Sesión 4 — 2026-10-21 (Implementación Windows completa — Antigravity)
- **Core validado:** Ejecutado `dotnet test` sobre `LockSuite.Core.Tests` (26/26 pruebas pasando).
- **Servicio Windows implementado:** `LockSuite.Service` con P/Invoke WFP nativo (`fwpuclnt.dll`), subcapa propia, reglas base y temporales dinámicas; resolutor DNS local con upstream y generador NXDOMAIN; DPAPI + PBKDF2 para credenciales del tutor con mitigación de fuerza bruta; y Named Pipe IPC con ACLs locales.
- **GUI WPF construida:** `LockSuite.UI` moderna con navegación Fluent/Slate, vistas de Estado, Lista Blanca, Modo Aprendizaje con aprobación en 1 clic y diálogos de tutor.
- **Suite de pruebas de integración y servicio:** 23 pruebas añadidas en `LockSuite.Service.Tests`. Total: 49/49 pruebas pasando.
- **Instalador:** Script Inno Setup 6 (`installer/LockSuiteSetup.iss`) y validación de publicación en Release.

### Sesión 5 — 2026-10-21 (Revisión para producción — Claude, sin terminal Windows)
Revisión a fondo (bugs, evasión, arranque, rendimiento, UX). Aplicado **en código**; compilación/tests/VM a cargo de Antigravity. Detalle en `docs/REVISION_PRODUCCION.md`.
- **Correctness/concurrencia:** snapshot inmutable de patrones en `ConfigManager` (fix de carrera + menos CPU por consulta); stats DNS con `Interlocked`; reset de lockout al expirar; validación de dominios (`IsValidPattern`); fix de liberación de memoria nativa IPv6.
- **Anti-evasión / arranque:** contraseña del tutor fijada **en la instalación** (cierra el hueco de que el usuario estándar la fije primero: nuevo `--set-password-file` + página en el instalador); servicio tolerante a fallo de bind DNS (`IsListening`, ya no se cae); auto-recuperación `sc failure`; `--reset-wfp` + limpieza en la desinstalación.
- **WFP:** claves de filtro fijas + instalación **atómica (transacción) e idempotente** (no acumula filtros duplicados al reiniciar); parada **ordenada** revierte WFP/NRPT, sólo una **caída** deja el estado fail-closed; `HardReset`.
- **Rendimiento:** `LearningManager` con throttle de escritura a disco (antes escribía por cada consulta bloqueada); NRPT en una sola invocación de PowerShell; back-pressure en el resolutor.
- **UX:** avisos de "sin conexión" y "modo simulación (el filtro NO está activo)"; estado del resolutor DNS; toggle de modo aprendizaje funcional (IPC `SetLearningMode`).
- **Docs:** `docs/REVISION_PRODUCCION.md` y `HANDOFF_ANTIGRAVITY.md` reescrito con comandos exactos y matriz de validación en VM.

### Sesión 6 — 2026-10-21 (Verificación, Build, 70 Tests y Empaquetado v0.2.0 — Antigravity)
- **Compilación verificada:** Toda la solución compila en Release sin advertencias ni errores (`LockSuite.sln`).
- **Tests ejecutados:** Se corrieron los tests en Release; pasan los **70/70 tests (100% verde)**:
  - `LockSuite.Core.Tests`: 43 tests (incluye validación exhaustiva de patrones de dominio y comodines).
  - `LockSuite.Service.Tests`: 27 tests (incluye DNS, WFP, IPC, LearningMode con buffer y persistencia diferida, y DPAPI/Lockout).
- **Publicación autónoma (self-contained):** Publicados `LockSuite.Service` y `LockSuite.UI` para `win-x64` con runtime .NET 8 embebido en `installer\publish\service` e `installer\publish\ui`.
- **Instalador generado:** Se instaló Inno Setup 6.7.3 en la máquina, se corrigieron los SIDs de permisos en `LockSuiteSetup.iss` (`admins-full users-readexec`), y se compiló exitosamente el instalador independiente:
  `bin\installer\LockSuiteSetup-0.2.0.exe` (73.6 MB).

---

## 8. Próximo paso

El instalador **`bin\installer\LockSuiteSetup-0.2.0.exe`** ya está generado, es autónomo y no requiere tener .NET instalado en el equipo final.
El **próximo paso concreto** es:
1. Copiar `bin\installer\LockSuiteSetup-0.2.0.exe` a una **máquina virtual limpia de Windows 11**.
2. Ejecutar la instalación desde cuenta de administrador, ingresar la contraseña maestra del tutor durante el asistente, y verificar el servicio (`sc query LockSuiteService`).
3. Cambiar a una cuenta estándar y probar la navegación en vivo:
   - Sitios permitidos (`google.com`, `mercadopago.com.ar`) deben cargar normalmente.
   - Sitios no permitidos (`facebook.com`, `instagram.com`) deben dar error inmediato.
   - En la interfaz gráfica, la pestaña "Bloqueados recientes" debe mostrar las consultas rechazadas y permitir autorizarlas con 1 clic ingresando la clave de tutor.
4. Validar la matriz de evasión (§4.5 de `HANDOFF_ANTIGRAVITY.md`).
