# Estado del proyecto — LockSuite Windows

Versión actual: **0.1 — librería Core escrita y con tests; falta todo lo específico de Windows.**
Última actualización: 2026-10-20, sesión 2.

## 1. Qué es esto

Software para instalar en computadoras Windows 11 que bloquea todo internet salvo una lista blanca de dominios. Lo administra una sola persona (contraseña maestra); las personas que usan las computadoras tienen cuentas estándar, sin esa contraseña. Es un filtro **transparente** (visible, instalado a la vista) y **local** (no envía la actividad del usuario a ningún lado): no espía, solo filtra a qué dominios se puede conectar.

Lista blanca de referencia: Google (Gmail/Chat/Drive/Fotos), mitmachim.top, prog.co.il, mercadopago.com.ar.

## 2. Decisiones de arquitectura (ya tomadas)

| Decisión | Resumen |
|---|---|
| Separación de cuentas | Admin (vos) vs. estándar (la persona). Base de todo. |
| Bloqueo de red | Windows Filtering Platform (WFP), *default block* outbound, IPv4 e IPv6. |
| Filtrado por dominio | Resolutor DNS local: solo resuelve dominios de la lista; al resolver abre regla WFP temporal para esa IP. Resto → NXDOMAIN. |
| Filtrado extra (fase 2) | Inspección del SNI del Client Hello de TLS (nombre en texto plano) para el hueco de CDN/IP compartida. Sin descifrar, sin CA propia. |
| Servicio | Windows Service como SYSTEM; la GUI es un cliente. |
| Contraseña | Hash con sal, validado por el servicio, con límite de intentos. |
| Interfaz | WPF: lista blanca, "bloqueados recientes" (aprendizaje), plantillas, config. |
| Instalador | Inno Setup: crea el servicio, aplica filtros, deja la interfaz. |

## 3. Explícitamente AFUERA del alcance (y por qué)

- **Driver de kernel propio** — innecesario (WFP ya opera en kernel vía API de usuario) y caro de firmar (EV + WHQL).
- **Inspección TLS con CA propia (lo que hace Techloq)** — permitiría leer el contenido del tráfico, no solo el dominio. Mucho más riesgo e invasividad; el SNI logra el mismo filtrado sin eso.
- **Blindaje contra el propio Administrador de Windows** (anti-`sc stop`, proceso invisible). En este diseño el admin es la parte de confianza; además eso es técnicamente indistinguible de spyware. No se construye.
- **Ocultar el programa/proceso** — el programa es visible; solo la config (lista + contraseña) está protegida.

Detalle en `docs/ANALISIS_VIABILIDAD.md` y `docs/ANALISIS_TECHLOQ.md`.

## 4. Qué está hecho

### Librería Core (`src/LockSuite.Core/`) — HECHA, lógica validada
Multiplataforma (.NET 8, sin dependencias de Windows). Es el corazón lógico; se puede compilar y testear en cualquier lado.
- `DomainMatcher.cs` — decide si un host está permitido. Soporta `dominio.com` (dominio + subdominios), `*.dominio.com` (solo subdominios), normalización (minúsculas, punto final). **Seguridad validada:** no matchea por substring (`notgoogle.com` ≠ `google.com`) y listar un subdominio no abre el padre.
- `SniParser.cs` — extrae el SNI de un ClientHello TLS. Defensivo: ante paquete truncado/inválido devuelve null, nunca lee fuera de rango.
- `WhitelistConfig.cs` — modelo de config (paquetes de dominios, fail-closed, modo aprendizaje) con JSON. El hash de la contraseña NO vive acá (lo maneja el servicio con DPAPI).
- `Presets.cs` — paquetes de fábrica: Esenciales del sistema, Google, Mercado Pago, Sitios de la comunidad.
- Tests en `tests/LockSuite.Core.Tests/` (xUnit): casos borde de matcher y parser.
- La lógica de matcher y parser fue validada con un prototipo en Python (todos los casos borde pasan) antes de portarla a C#. Falta correr los tests de C# del lado de Windows/dotnet (ver sección 7).

## 5. Qué sigue (en orden)

1. **(Para Antigravity)** Correr `dotnet test` sobre el Core y confirmar verde — ver sección 7.
2. Servicio de Windows: WFP default-deny + resolutor DNS local + IPC (named pipe) con la GUI + guardado de config con DPAPI + validación de contraseña. **Se construye y prueba en una VM de Windows, por partes, no todo de una** (un filtro default-deny mal probado deja la máquina sin red). Empezar por: aplicar/quitar los filtros WFP base y verificar que corta y que loopback/DNS siguen.
3. Resolutor DNS local enganchado al matcher del Core + apertura de reglas WFP temporales por IP.
4. Interfaz WPF.
5. Instalador (Inno Setup) + firma (si hay certificado) + pruebas en VM.
6. Manual de instalación y de uso.

## 6. Preguntas abiertas

1. **¿Para quiénes son las computadoras?** (empleados / institución educativa / familia / autocontrol). No cambia la arquitectura, sí los textos de la interfaz, si conviene un cartel visible de "equipo con restricciones", y qué dominios precargar. Por prog.co.il / mitmachim.top parece contexto comunitario/educativo — confirmar.
2. **¿Qué hay en la máquina de desarrollo?** ¿.NET 8 SDK? ¿Visual Studio / VS Code? ¿Inno Setup?
3. **Firma de código:** ¿hay certificado, o se prueba sin firmar por ahora (aceptando el aviso de SmartScreen)?

## 7. Para Antigravity

**Tarea 1 — build y test del Core.** En `C:\Locksuite windows`:
```
dotnet test tests\LockSuite.Core.Tests\LockSuite.Core.Tests.csproj
```
Esperado: compila y todos los tests pasan (matcher + SNI parser). Si falta el SDK, instalar .NET 8 SDK. Reportar en esta sección si algo falla, con el output.

*(Las tareas del servicio WFP/DNS se van a ir detallando acá a medida que se escriban, siempre para construir y probar en una VM primero.)*

## 8. División de trabajo

- **Claude:** código fuente, docs, scripts, investigación, y validación de lógica que no dependa de Windows (como se hizo con el Core). Lee/escribe en `C:\Locksuite windows`. **No tiene terminal de Windows:** no compila .NET, no corre el .exe, no prueba la GUI ni el servicio. Eso es de Antigravity / el usuario, en VM.
- **Antigravity / usuario:** compilar, testear, debuggear en vivo, VM, instalador, firma.

## 9. Log de sesiones

### Sesión 1 — 2026-10-20 (diseño)
Arquitectura completa, alcance, análisis de Techloq, sistema de documentación y prompt maestro. Commit inicial.

### Sesión 2 — 2026-10-20 (Core)
Escrita la librería Core (.NET 8): DomainMatcher, SniParser, WhitelistConfig, Presets + tests xUnit. La lógica crítica (matcher + parser SNI) se validó con un prototipo en Python corriendo todos los casos borde, incluidos los de seguridad (no substring, no abrir el padre al listar un subdominio). Pendiente correr los tests de C# del lado de Windows. El servicio, la GUI y el instalador (todo lo que necesita Windows) siguen pendientes y se harán por partes en VM.
