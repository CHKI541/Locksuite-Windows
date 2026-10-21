# Estado del proyecto — LockSuite Windows

Versión actual: **0.1 — librería Core escrita, revisada y con tests; falta todo lo específico de Windows.**
Última actualización: 2026-10-20, sesión 3.

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
| Contraseña | Hash con sal, validado por el servicio, con límite de intentos. |
| Interfaz | WPF: lista blanca, "bloqueados recientes" (aprendizaje), plantillas, config. |
| Instalador | Inno Setup: crea el servicio, aplica filtros, deja la interfaz. |

## 3. Explícitamente AFUERA del alcance (y por qué)

- **Driver de kernel propio** — innecesario (WFP ya opera en kernel vía API de usuario) y caro de firmar (EV + WHQL).
- **Inspección TLS con CA propia (lo que hace Techloq)** — permitiría leer el contenido del tráfico, no solo el dominio. Mucho más riesgo e invasividad; el SNI logra el mismo filtrado sin eso.
- **Blindaje contra el propio Administrador de Windows** (anti-`sc stop`, proceso invisible/imposible de matar). En este diseño el admin (el tutor) es la parte de confianza; además eso es técnicamente indistinguible de spyware. No se construye. La protección real viene de la separación de cuentas: quien usa el equipo no tiene la cuenta admin ni la contraseña.
- **Ocultar el programa/proceso** — el programa es visible; solo la config (lista + contraseña) está protegida.

Detalle en `docs/ANALISIS_VIABILIDAD.md` y `docs/ANALISIS_TECHLOQ.md`.

## 4. Qué está hecho

### Librería Core (`src/LockSuite.Core/`) — HECHA, revisada, lógica validada
Multiplataforma (.NET 8, sin dependencias de Windows). Es el corazón lógico; se compila y testea en cualquier lado.
- `DomainMatcher.cs` — decide si un host está permitido. `dominio.com` habilita el dominio y todos sus subdominios; `*.dominio.com` solo subdominios. Normaliza (minúsculas, punto final). **Seguridad validada:** no matchea por substring (`notgoogle.com` ≠ `google.com`) y listar un subdominio no abre el padre.
- `SniParser.cs` — extrae el SNI de un ClientHello TLS. Defensivo: ante paquete truncado/inválido devuelve null, nunca lee fuera de rango.
- `WhitelistConfig.cs` — modelo de config (paquetes, fail-closed, modo aprendizaje) con JSON. El hash de la contraseña NO vive acá (lo maneja el servicio con DPAPI).
- `Presets.cs` — paquetes de fábrica: Esenciales del sistema, Google, Mercado Pago, Sitios de la comunidad. Usan dominios "pelados" (sin wildcards redundantes), apoyados en la semántica del matcher.
- Tests en `tests/LockSuite.Core.Tests/` (xUnit).

### Revisión (sesión 3) — hallazgos
1. Lógica de `DomainMatcher` y `SniParser` validada con prototipo en Python corriendo todos los casos borde (incluidos los de seguridad). OK.
2. `Presets.cs` tenía un `gstatic.com` duplicado y varios `*.x` redundantes (el dominio pelado ya cubre subdominios). **Corregido** esta sesión.
3. **Pendiente de compilar:** el C# todavía no se compiló (acá no hay .NET). Puede tener algún detalle de compilación. Confirmar con `dotnet test` (ver sección 7).
4. **Semántica a tener presente:** un dominio pelado habilita *todos* sus subdominios. Para un filtro estricto es un criterio amplio pero manejable (se ajusta con el modo aprendizaje). Si en algún caso se quiere más fino, usar subdominios puntuales.
5. **Limitación conocida del SNI (para fase 2):** no maneja ClientHello fragmentado en varios records TLS (caso raro, pero un evasor decidido podría fragmentar). A resolver cuando se implemente el filtrado por SNI en el servicio.

## 5. Qué sigue (en orden)

1. **(Antigravity)** `dotnet test` del Core, confirmar verde — ver sección 7 y `HANDOFF_ANTIGRAVITY.md`.
2. Servicio de Windows: WFP default-deny + resolutor DNS local + IPC (named pipe) con la GUI + guardado de config con DPAPI + validación de contraseña. **Se construye y prueba en una VM de Windows, por partes, no todo de una.** Empezar por aplicar/quitar los filtros WFP base y verificar que corta y que loopback/DNS siguen.
3. Resolutor DNS local enganchado al matcher del Core + apertura de reglas WFP temporales por IP.
4. Interfaz WPF.
5. Instalador (Inno Setup) + firma (si hay certificado) + pruebas en VM.
6. Manual de instalación y de uso.

## 6. Preguntas abiertas

1. ~~¿Para quiénes son las computadoras?~~ **RESPONDIDA** (sección 0): computadoras Kosher para una comunidad judía ortodoxa; la primera para un adolescente; se busca generalizar. → Pendiente derivado: definir textos de la interfaz y si se muestra un cartel de "equipo con filtrado" (probablemente sí, por transparencia).
2. **¿Qué hay en la máquina de desarrollo?** ¿.NET 8 SDK? ¿Visual Studio / VS Code? ¿Inno Setup?
3. **Firma de código:** ¿hay certificado, o se prueba sin firmar por ahora (aceptando el aviso de SmartScreen)?

## 7. Para Antigravity

El brief completo y copiable está en **`HANDOFF_ANTIGRAVITY.md`** (en la raíz del proyecto). Resumen:
- Tarea inmediata: `dotnet test tests\LockSuite.Core.Tests\LockSuite.Core.Tests.csproj` y reportar resultado acá.
- Tarea grande: implementar y **probar en VM** los componentes de Windows (servicio WFP + DNS local + GUI + instalador) siguiendo `docs/ANALISIS_VIABILIDAD.md`, respetando lo de la sección 3.
- Al terminar: actualizar este `ESTADO.md` y dejar el proyecto listo para seguir pegando `PROMPT_MAESTRO.md` en una conversación nueva.

## 8. División de trabajo

- **Claude:** código fuente que no dependa de Windows (como el Core), docs, scripts, investigación, revisión de lógica. Lee/escribe en `C:\Locksuite windows`. **No tiene terminal de Windows:** no compila .NET, no corre el .exe, no prueba la GUI ni el servicio.
- **Antigravity / usuario:** compilar, testear, debuggear en vivo, VM, servicio Windows, instalador, firma.

## 9. Log de sesiones

### Sesión 1 — 2026-10-20 (diseño)
Arquitectura completa, alcance, análisis de Techloq, sistema de documentación y prompt maestro. Commit inicial.

### Sesión 2 — 2026-10-20 (Core)
Escrita la librería Core (.NET 8): DomainMatcher, SniParser, WhitelistConfig, Presets + tests xUnit. Lógica crítica validada con prototipo en Python.

### Sesión 3 — 2026-10-20 (contexto + revisión + handoff)
Se aclaró y documentó el contexto (computadora Kosher para comunidad judía ortodoxa; primero un adolescente; se busca generalizar) — sección 0. Revisión del Core: corregido `gstatic.com` duplicado y wildcards redundantes en `Presets.cs`; registrados hallazgos y limitaciones (sección 4). Creado `HANDOFF_ANTIGRAVITY.md`. Pendiente sin cambios: compilar/testear el Core y construir los componentes de Windows en VM.
