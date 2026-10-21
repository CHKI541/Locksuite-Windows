# Revisión para producción — LockSuite Windows (Sesión 5, Claude)

Fecha: 2026-10-21. Revisión a fondo del código con foco en: (1) bugs/correctness, (2) evasión y fallos de arranque, (3) rendimiento/CPU, (4) UX de configuración. Todo lo que requiere compilar/probar en Windows quedó en `HANDOFF_ANTIGRAVITY.md`.

> **Contexto defensivo.** Este análisis de "evasión" es para **endurecer el propio filtro** (software del tutor, instalado de forma transparente y consentida en equipos de la comunidad). No es una guía para eludir filtros de terceros. Los límites que ningún software puede superar (contraseña de admin, acceso físico, segundo dispositivo con datos) ya están en `ANALISIS_VIABILIDAD.md` §6 y no se repiten acá.

---

## 0. Resumen ejecutivo

Se encontraron y corrigieron en **código** varios problemas reales (concurrencia, idempotencia de WFP, semántica de parada, escritura a disco por paquete, un **hueco de seguridad en el arranque**: cualquiera podía fijar la contraseña maestra primero). Se agregó resiliencia de arranque (el servicio ya no se cae si el DNS no puede enlazar), auto-recuperación del servicio ante caídas, limpieza para desinstalación y avisos claros en la interfaz cuando el filtro NO está realmente activo.

Quedan validaciones que **sólo** se pueden hacer en Windows real/VM (compilar, correr tests, probar WFP en vivo, empaquetar). Están detalladas en `HANDOFF_ANTIGRAVITY.md`.

Estado de enforcement (sin cambios de alcance): la seguridad sigue apoyada en **cuenta admin (tutor) vs. estándar (usuario)** + WFP default-deny + resolutor DNS local. No se agregó driver kernel, ni MITM/CA, ni blindaje anti-admin (respetado el alcance).

---

## 1. Bugs y correctness (corregidos en esta sesión)

### 1.1 Carrera de datos en `ConfigManager` (serialización vs. mutación) — CORREGIDO
`IsAllowed()` se ejecuta **por cada consulta DNS** y recalculaba `ActivePatterns()` (LINQ + `Distinct` + `ToList`) sin lock, mientras otras hebras (comandos IPC `AddDomain`/`RemoveDomain`) mutaban las listas de dominios en vivo. Esto podía lanzar `InvalidOperationException: Collection was modified` o leer estado a medio escribir. Además `GetConfig`/`GetStatus` serializaban/contaban la config fuera del lock.
- **Fix:** se mantiene un **snapshot inmutable** de patrones activos (`volatile string[] _activePatterns`) que se reemplaza atómicamente en cada cambio. El hot path (`IsAllowed`, `ActivePatternCount`) lee el snapshot **sin lock**; las mutaciones y la serialización (`GetConfigJson`) toman el lock. Beneficio doble: correctness + menos CPU por consulta (ver §3).

### 1.2 Estadísticas DNS no atómicas — CORREGIDO
`TotalQueries++/AllowedQueries++/BlockedQueries++` se incrementaban desde múltiples hebras (`Task.Run` por consulta) → incrementos perdidos y lecturas rotas desde la GUI.
- **Fix:** campos backing con `Interlocked.Increment` y getters con `Interlocked.Read`.

### 1.3 `WfpManager`: acumulación de filtros duplicados al reiniciar — CORREGIDO
Los filtros base se creaban con `Guid.NewGuid()` en cada arranque. Como la sesión WFP no dinámica (fail-closed) **persiste los filtros tras una caída hasta el reinicio del equipo**, al relanzarse el servicio se agregaba **otro** juego completo de filtros base (duplicados que se acumulaban).
- **Fix:** claves de filtro **fijas** + instalación dentro de una **transacción WFP** que primero **borra por clave** los filtros base previos y luego los recrea. Resultado: idempotente (nunca duplica) y atómico (nunca se ve un estado intermedio con permits pero sin el block).

### 1.4 Semántica confusa de "detener el servicio" — CORREGIDO
Con `FailClosed=true` (default), `StopAsync` **no** revertía WFP: al hacer `sc stop` la máquina quedaba **sin red** (WFP bloqueando + NRPT apuntando a un resolutor ya muerto). Un admin que parara el servicio para diagnosticar quedaba sin internet sin explicación.
- **Fix:** una parada **ordenada** (`StopAsync`: `sc stop`, apagado, desinstalación) ahora **siempre revierte** WFP y NRPT y restaura la conectividad. El escenario fail-closed corresponde a una **caída** del proceso (que no invoca `StopAsync`): ahí los filtros de la sesión no dinámica persisten y la red queda bloqueada hasta el reinicio. Es el modelo correcto: parada intencional = se abre; caída = se queda cerrado.

### 1.5 Escritura a disco por cada consulta bloqueada — CORREGIDO (perf + robustez)
`LearningManager.RecordBlocked` hacía `File.WriteAllText` + `Move` **en cada** dominio bloqueado, bajo lock, desde las hebras del DNS. Una página con muchos trackers o un proceso haciendo beaconing generaba una ráfaga de escrituras a disco.
- **Fix:** la lista se actualiza en memoria al instante (marca "sucio") y un temporizador vuelca a disco **a lo sumo cada 5 s**; además se vuelca al vaciar y al apagar el servicio (`IDisposable`). Ver §3.

### 1.6 Lockout anti-fuerza-bruta no se reiniciaba al expirar — CORREGIDO (UX)
Tras un bloqueo, el contador de fallos quedaba en su valor alto; el **primer** error posterior volvía a bloquear de inmediato. 
- **Fix:** al expirar el bloqueo se reinicia el contador. No debilita la protección (el umbral sigue siendo 5→60 s, 10→5 min) y evita re-bloqueos por un solo tipeo.

### 1.7 Validación de dominios inexistente — CORREGIDO
`AddDomain` aceptaba cualquier cosa. Agregar `com` o `*` por error habilitaba medio internet (un patrón "pelado" matchea el dominio y todos sus subdominios).
- **Fix:** `DomainMatcher.IsValidPattern` rechaza vacío, TLD/label único (`com`, `localhost`), comodín fuera de `"*."`, caracteres inválidos y labels vacíos/mal formados. `ConfigManager.AddDomain` lo usa y devuelve `false` (la GUI muestra el error). Tests nuevos cubren los casos.

### 1.8 Fuga potencial de memoria nativa en `AddIpFilter` (IPv6) — CORREGIDO
En la rama IPv6, si fallaba una asignación intermedia, los `AllocHGlobal` podían no liberarse en todos los caminos.
- **Fix:** `try/finally` anidados correctamente; `byte16Ptr` y `condPtr` se liberan siempre.

---

## 2. Evasión y fallos de arranque (endurecido)

### 2.1 HUECO CRÍTICO DE BOOTSTRAP: cualquiera podía fijar la contraseña maestra — CORREGIDO
El instalador **no** fijaba la contraseña del tutor. El comando IPC `SetInitialPassword` no requiere (ni puede requerir) autenticación. Resultado: la **primera persona** que abría la app y hacía clic en "Desbloquear" fijaba la contraseña y se convertía en el tutor. Si el usuario estándar (p. ej. el adolescente) lo hacía primero, controlaba la lista blanca.
- **Fix:** 
  - Nuevo comando de servicio `--set-password-file <ruta>` (lee la clave de un archivo, la fija y borra el archivo).
  - El **instalador** ahora tiene una **página de contraseña del tutor** (con confirmación y mínimo 6 caracteres) y la fija durante la instalación, **antes** de que el usuario estándar pueda abrir la app. A partir de ahí `IsPasswordSet()` es true y el IPC `SetInitialPassword` responde "ya configurada" a todos.
- **Recomendación operativa:** instalar siempre desde la cuenta del **tutor/admin**, definir la contraseña en ese paso, y recién después entregar el equipo al usuario con su cuenta **estándar**.

### 2.2 El servicio podía no arrancar si el puerto 53 estaba ocupado — CORREGIDO
`DnsResolver.Start()` lanzaba excepción si no podía enlazar `127.0.0.1:53` (p. ej. otro servicio DNS, ICS, algún VPN). Eso propagaba y **tiraba abajo** el arranque del servicio, dejando WFP activo pero el servicio caído.
- **Fix:** `Start()` **reintenta** (3×) y, si no puede, deja `IsListening=false`, **no** lanza, y el servicio sigue en pie (la red permanece bloqueada por WFP = fail-closed). La GUI muestra el estado del resolutor para que el tutor lo detecte y actúe (ver §4).

### 2.3 Auto-recuperación del servicio ante caídas — AGREGADO
Si el servicio se cae (bug, OOM, etc.), antes quedaba caído hasta reinicio (con WFP persistiendo el bloqueo: fail-closed pero sin posibilidad de resolver nada).
- **Fix:** el instalador configura `sc failure` para **reiniciar** el servicio automáticamente (3 reintentos, 5 s). Al reiniciar, los filtros base se re-aplican idempotentes (§1.3) y se restablece el resolutor/NRPT.

### 2.4 Verificación del modelo anti-evasión (sin cambios de código, confirmación)
Se revisó que el enforcement real es **WFP default-deny**, no el DNS:
- **DoH / DNS alterno / hosts manipulado:** aunque el usuario resuelva un nombre por otro canal, **no puede conectarse** porque WFP bloquea todo outbound salvo a IPs que el resolutor local autorizó. DNS externo (puerto 53 a otra IP) y DoH (443 a un resolutor no permitido) quedan bloqueados. El usuario estándar tampoco puede cambiar NRPT, el DNS del adaptador ni el archivo hosts (requiere admin).
- **Si NRPT falla:** las apps resuelven contra el DNS del adaptador (externo) → WFP lo bloquea → no resuelven → **fail-closed** (se rompe la navegación, no se abre). Dirección correcta.
- **Parar/forzar el servicio:** requiere admin (el usuario estándar no puede `sc stop`).

### 2.5 Limpieza para desinstalación / recuperación — AGREGADO
Nuevo comando `--reset-wfp` (borra por clave los filtros base + subcapa de LockSuite y remueve la regla NRPT). El desinstalador lo ejecuta, y `--uninstall` también limpia. Esto evita dejar la máquina con DNS redirigido a un resolutor inexistente o con filtros huérfanos tras una desinstalación.

### 2.6 Limitaciones conocidas que quedan documentadas (hardening futuro, requiere VM)
Estas NO son inseguras para el caso de uso, pero conviene cerrarlas en una fase 2 con pruebas reales:

1. **Reglas dinámicas por IP sin restricción de puerto.** `AddIpFilter` abre la IP para **cualquier puerto/protocolo**, no sólo 80/443 como sugería el diseño. Una IP autorizada por un dominio permitido queda abierta en todos sus puertos. Mitigación propuesta: añadir condiciones `IP_PROTOCOL=TCP` + `IP_REMOTE_PORT in {80,443}` al filtro dinámico. Riesgo: romper servicios legítimos en otros puertos → **probar en VM**.
2. **IPs compartidas de CDN (Cloudflare, etc.).** Es el hueco ya previsto en `ANALISIS_VIABILIDAD.md` §2.4: si un dominio permitido y uno no permitido comparten IP, autorizar la IP habilita ambos. Cierre previsto: **inspección de SNI** (fase 2, el `SniParser` ya existe y está probado) a nivel de flujo WFP. Requiere enganchar el parser en la capa de stream → **sólo testeable en Windows**.
3. **Fuga menor de reglas dinámicas tras una caída dura.** Las reglas por IP se agregan en la sesión no dinámica; si el proceso **cae** (no parada ordenada), quedan en WFP hasta el reinicio del equipo y la nueva instancia no las conoce (no se limpian hasta reboot). Severidad baja (son IPs que ya estaban permitidas). Cierre limpio propuesto: mover **sólo** las reglas por IP a una **segunda sesión WFP dinámica** (se autolimpian al morir el proceso) o enumerar/limpiar al arrancar. Requiere P/Invoke adicional y **pruebas en VM**.
4. **DNS sólo UDP.** Si una respuesta se trunca (TC=1) el cliente reintenta por TCP/53, que el resolutor no atiende. Raro para A/AAAA normales. Agregar listener TCP/53 en fase 2.
5. **DHCPv6** no está permitido explícitamente (sólo DHCP v4). En redes IPv6 puras podría afectar; en la práctica no es el caso objetivo.

---

## 3. Rendimiento / CPU

Objetivo: gastar lo mínimo. Hot paths revisados: resolución DNS (por consulta) y registro de bloqueados.

- **Patrones activos cacheados (§1.1):** antes cada consulta DNS hacía `SelectMany + Select + Distinct + ToList` sobre todos los packs bajo lock. Ahora lee un `string[]` inmutable sin lock. Menos asignaciones y cero contención en el camino caliente.
- **Sin escritura a disco por consulta bloqueada (§1.5):** antes una ráfaga de bloqueos = ráfaga de `File.WriteAllText`. Ahora, a lo sumo una escritura cada 5 s.
- **Back-pressure en el resolutor:** se agregó un `SemaphoreSlim(256)` para acotar tareas/sockets en vuelo ante una ráfaga de consultas (evita crecer sin tope).
- **NRPT en una sola invocación de PowerShell:** el arranque lanzaba `powershell.exe` dos veces (remove + add). Ahora es **una** sola invocación (remove+add) → arranque más rápido y menos procesos.
- **Costo en reposo:** un timer de expiración de reglas WFP cada 5 s y un timer de flush cada 5 s; ambos son triviales. La GUI refresca cada 3 s por IPC (un round-trip de named pipe), aceptable; se puede subir el intervalo si se quiere aún menos actividad.

Nota: el filtrado ocurre dentro de la pila WFP del kernel (no hay un proxy en espacio de usuario inspeccionando paquete a paquete), así que el costo por conexión es mínimo, como en `simplewall`.

Idea opcional (no aplicada, bajo impacto): el cliente DNS de Windows (`dnscache`) ya cachea por TTL, por lo que un caché propio en el resolutor aportaría poco. No se agregó para no sumar complejidad/riesgo.

---

## 4. UX / configuración

- **Avisos claros cuando el filtro NO está activo (nuevo).** En la vista Estado:
  - Banner rojo "**Sin conexión con el servicio**" (cuando la GUI no habla con el servicio).
  - Banner ámbar "**Modo simulación: el filtro NO está activo**" (cuando WFP no se pudo aplicar por falta de privilegios). Esto es importante: antes el tutor podía creer que estaba protegido sin estarlo.
  - Fila "**Resolutor DNS escuchando: Sí/No**" en los detalles, para detectar el caso del puerto 53 ocupado (§2.2).
- **Modo aprendizaje ahora es configurable desde la GUI** (antes el checkbox estaba deshabilitado). Requiere autenticación del tutor; comando IPC `SetLearningMode` agregado.
- **Fail-Closed** queda como informativo (siempre activo por seguridad) con un tooltip que lo explica.
- **Validación de dominios** al agregar (§1.7): si el tutor tipea algo peligroso/mal formado, se rechaza con mensaje en vez de aceptarlo.

Pendiente de pulido (recomendado, requiere ejecutar la GUI en Windows — ver HANDOFF): buscador/filtro en la lista blanca, refresco incremental de "Bloqueados recientes" (hoy limpia y recarga cada 3 s → parpadeo), estados vacíos más amables, y un asistente de primera ejecución. Son mejoras cosméticas; el flujo ya es funcional.

---

## 5. Qué NO se tocó (y por qué)
- Alcance intacto: sin driver `.sys`, sin MITM/CA propia, sin blindaje anti-admin (ver `ANALISIS_VIABILIDAD.md`).
- La lógica central de `DomainMatcher`, `SniParser` y `DnsPacket` ya era correcta y está bien testeada; sólo se **agregó** `IsValidPattern` (aditivo).

---

## 6. Pendiente para Antigravity (Windows real) — ver `HANDOFF_ANTIGRAVITY.md`
1. `dotnet build` / `dotnet test` de toda la solución (los cambios son C# gestionado + cambios en WFP/instalador que **no** pude compilar acá: este entorno es Linux sin `dotnet` ni WFP).
2. Validar la ruta WFP real en **VM limpia** (idempotencia, fail-closed en caída, rollback en parada ordenada, `--reset-wfp`).
3. Probar el **instalador** nuevo: página de contraseña, servicio arriba, contraseña ya fijada (el usuario estándar no puede re-fijarla), navegación permitida/bloqueada, desinstalación limpia.
4. Matriz de pruebas de evasión (defensiva) en VM: DNS alterno, DoH del navegador, hosts, cambio de DNS del adaptador desde cuenta estándar (debe fallar), `sc stop` desde cuenta estándar (debe fallar).
