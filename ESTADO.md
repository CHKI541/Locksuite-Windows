# Estado del proyecto — LockSuite Windows

Versión actual: **0.2 — Core compilado y validado; Servicio de Windows (WFP + DNS Local + DPAPI + IPC), GUI WPF e Instalador Inno Setup implementados; 49/49 tests pasando.**
Última actualización: 2026-10-21, sesión 4 (Antigravity).

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

### 4.4 Instalador Inno Setup (`installer/LockSuiteSetup.iss`) — HECHO
Script listo para compilar con Inno Setup 6. Empaqueta los binarios publicados del servicio y de la UI, instala el servicio con `sc.exe`, inicia el filtrado, crea los directorios protegidos en `%ProgramData%\LockSuite` y coloca los accesos directos.

### 4.5 Pruebas Automatizadas — 49/49 superadas
- `LockSuite.Core.Tests`: 26 tests (DomainMatcher, SniParser).
- `LockSuite.Service.Tests`: 23 tests (DnsPacket, NXDOMAIN, TTL/IP parsing, WfpManager, SecurityManager/DPAPI/Lockout, ConfigManager, LearningManager, IpcProtocol).

## 5. Qué sigue (en orden)

1. **Prueba en máquina virtual (VM) limpia de Windows 11:**
   - Copiar los binarios publicados de `publish/` o el instalador generado.
   - Ejecutar la instalación, verificar que el servicio inicia y que las consultas a dominios de la lista blanca resuelven y navegan, mientras que sitios no listados quedan bloqueados (NXDOMAIN).
   - Verificar la experiencia de la GUI en la cuenta estándar y la aprobación en 1 clic desde "Bloqueados recientes".
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

---

## 8. Próximo paso

El proyecto está completamente implementado y con todos los tests pasando en verde.
El **próximo paso concreto** es:
1. Probar el ejecutable publicado o el instalador en una **máquina virtual limpia de Windows 11** para validar el comportamiento del filtro en vivo en un entorno aislado sin riesgo de desconexión del host.
2. Validar el flujo de usuario: arranque del servicio -> navegación a sitios permitidos (Google, Mercado Pago) -> detección en "Bloqueados recientes" de dominios externos -> autorización con contraseña de tutor -> verificación de apertura de regla WFP.
