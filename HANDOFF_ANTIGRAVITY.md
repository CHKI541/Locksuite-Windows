# Handoff para Antigravity — LockSuite Windows

Pegá este archivo (o su contenido) como prompt inicial en Antigravity, que corre local en Windows y puede compilar, ejecutar y probar lo que Claude no puede.

---

## Contexto

LockSuite es un filtro de internet por lista blanca para Windows 11. Caso de uso: **computadoras "Kosher"** para una comunidad judía ortodoxa — internet filtrado en el equipo de un adolescente (control del tutor sobre el equipo de un menor), con intención de generalizarlo a más familias. Es un filtro **transparente y local**: visible, sin espiar ni mandar la actividad a ningún lado; solo controla a qué dominios se conecta el equipo.

Todo el proyecto está en `C:\Locksuite windows` (repo git, rama `main`, remoto `CHKI541/Locksuite-Windows`).

## Antes de tocar nada

Leé, en este orden:
1. `ESTADO.md` — estado real, decisiones, qué está hecho, qué sigue, y **sección 3 (lo que queda explícitamente fuera de alcance)**.
2. `docs/ANALISIS_VIABILIDAD.md` — la arquitectura en detalle.
3. `docs/ANALISIS_TECHLOQ.md` — qué se tomó y qué no de Techloq.

Respetá las decisiones de la sección 3 de `ESTADO.md` (sin driver de kernel propio, sin inspección TLS con CA propia, sin blindaje anti-administrador, programa visible). Si creés que alguna ya no sirve, dejalo anotado y consultá — no la cambies por tu cuenta.

## Tarea 1 — build y test del Core (rápida, hacela primero)

```
cd "C:\Locksuite windows"
dotnet test tests\LockSuite.Core.Tests\LockSuite.Core.Tests.csproj
```
Esperado: compila y pasan todos los tests (DomainMatcher + SniParser). Si falta el SDK, instalar **.NET 8 SDK**. Si algo falla, corregí lo mínimo para que compile y pase, y anotá en `ESTADO.md` (sección 4) qué tocaste.

## Tarea 2 — componentes de Windows (lo grande)

Implementá y **probá en una VM de Windows 11, por partes**, siguiendo `docs/ANALISIS_VIABILIDAD.md`. Un filtro default-deny mal probado deja la máquina sin red — nunca lo pruebes por primera vez en un equipo real de alguien. Orden sugerido, probando cada etapa antes de la siguiente:

1. **Servicio base** (Windows Service en SYSTEM, .NET 8): esqueleto que instala/arranca/para y escribe log.
2. **Filtros WFP base**: aplicar la política default-deny de salida (IPv4+IPv6) con las excepciones mínimas (loopback, DHCP, DNS al resolutor local). Verificar: con el servicio activo no hay salida; loopback y DNS siguen; al parar el servicio, el estado por defecto queda cerrado (fail-closed).
3. **Resolutor DNS local** enganchado al `DomainMatcher` del Core: resuelve solo lo permitido, y por cada IP resuelta abre una regla WFP temporal. Resto → NXDOMAIN, y se registra para el modo aprendizaje.
4. **IPC** (named pipe) servicio↔GUI + guardado de config con **DPAPI** + validación de contraseña (hash+sal, límite de intentos).
5. **GUI WPF**: login, lista blanca con los presets del Core, "bloqueados recientes", config.
6. **Instalador Inno Setup**: instala el servicio, aplica filtros, crea/convierte la cuenta estándar, deja la GUI. Firma si hay certificado (ver pregunta abierta 3 en `ESTADO.md`).

Para el detalle de cada componente, la referencia es `docs/ANALISIS_VIABILIDAD.md`; no hace falta reinventar el diseño, sí implementarlo y probarlo.

## Al terminar (importante)

1. Actualizá `ESTADO.md`: qué construiste, qué probaste y cómo, qué quedó pendiente, nuevos hallazgos/limitaciones, y una línea nueva en el log de sesiones.
2. Si llegaste a una versión que compila y anda, hacé commit (mensaje claro de qué incluye) y dejá la rama lista para push.
3. Dejá escrito, al final de `ESTADO.md`, un **"Próximo paso"** concreto, para que al pegar `PROMPT_MAESTRO.md` en una conversación nueva (con Claude o con vos) se retome sin perder tiempo.
