# Estado del proyecto — LockSuite Windows

Versión actual: **0.0 — diseño, sin código todavía.**
Última actualización: 2026-10-20, sesión inicial de diseño.

## 1. Qué es esto

Software para instalar en varias computadoras Windows 11, que bloquea todo internet salvo una lista blanca de dominios. Lo administra una sola persona (dueña de la contraseña maestra); las personas que usan las computadoras tienen cuentas estándar, sin esa contraseña.

Lista blanca de referencia que dio el usuario:
- Google: Gmail, Google Chat, Drive, Fotos (en la práctica son muchos subdominios — ver nota en `docs/ANALISIS_VIABILIDAD.md`)
- mitmachim.top
- prog.co.il
- mercadopago.com.ar

## 2. Decisiones de arquitectura (ya tomadas)

| Decisión | Resumen |
|---|---|
| Separación de cuentas | Admin (vos) vs. estándar (la persona). Es la base de todo; sin esto ningún software alcanza. |
| Bloqueo de red | Windows Filtering Platform (WFP), política *default block* en outbound, IPv4 e IPv6. |
| Filtrado por dominio | Resolutor DNS local: solo responde los dominios permitidos (con comodín `*.dominio.com`); al responder, abre una regla de firewall temporal solo para esa IP. Todo lo demás, NXDOMAIN. |
| Filtrado extra (fase 2) | Inspección del SNI en el "Client Hello" de TLS (el nombre del sitio va sin cifrar ahí) para tapar el hueco de IPs compartidas/CDN, sin descifrar nada. |
| Servicio | Windows Service corriendo como SYSTEM; la GUI es solo un cliente que le habla. |
| Contraseña | Hash con sal, validado por el servicio, con límite de intentos. |
| Interfaz | WPF: lista blanca, "bloqueados recientes" (modo aprendizaje), plantillas (Google, etc.), configuración. |
| Instalador | Inno Setup: crea el servicio, aplica los filtros, deja la interfaz lista. |

## 3. Explícitamente AFUERA del alcance (y por qué)

- **Driver de kernel propio.** No hace falta (WFP ya opera en el kernel sin necesitar firmar un driver), y conseguir firma EV + certificación WHQL de Microsoft es caro y lento.
- **Inspección de tráfico HTTPS con certificado raíz propio (TLS MITM).** Filtrar por dominio no requiere leer el contenido del tráfico. Meter una CA propia en el almacén de Windows es un componente de mucho más riesgo (si alguien compromete esa clave, puede interceptar todo el tráfico cifrado de esa máquina) y rompe apps con *certificate pinning*. El SNI (punto anterior) da casi todo el mismo filtrado sin ese riesgo.
- **"Anti-tamper" que resista a un Administrador de Windows** (hooks de kernel para que ni el admin pueda matar el proceso, DACL que bloquea `sc stop` incluso con permisos de admin). En este diseño el único Administrador es la persona de confianza, así que no hace falta blindarse contra el admin — alcanza con que el proceso corra como SYSTEM (ya intocable para una cuenta estándar sin nada especial) y con que la persona restringida nunca tenga esa cuenta. Construir algo que se defiende de su propio administrador es, visto desde afuera, indistinguible de spyware — terreno en el que no se va a entrar, más allá de la legitimidad del uso acá.
- **Ocultar el programa o el proceso.** Es un programa instalado normal (ícono, carpeta de instalación, aparece en "Aplicaciones"). La persona puede saber que está instalado; lo que no puede es sacarlo sin la contraseña.

## 4. Preguntas abiertas

1. **¿Quiénes son las "muchas personas" que van a tener esto instalado?** (¿Empleados? ¿Estudiantes de una institución? ¿Hijos? ¿Gente que lo instala para su propio autocontrol?) No cambia la arquitectura de arriba, pero sí cosas como los textos de la interfaz, si conviene un aviso visible de "este equipo tiene restricciones", y qué dominios conviene traer precargados además de los 4 de ejemplo.
2. **¿Qué hay instalado en la máquina donde se va a programar/compilar?** ¿.NET SDK? ¿Visual Studio o VS Code? Decide qué puede dejar listo Claude para compilar directamente.
3. **Firma de código.** Un instalador que toca el firewall y el DNS como SYSTEM va a disparar alertas de SmartScreen/antivirus si no está firmado. ¿Hay certificado de firma de código, o por ahora se prueba sin firmar (aceptando el cartel de Windows) y se deja para más adelante?

## 5. Qué sigue (próximos pasos, en orden)

1. Responder las preguntas abiertas de arriba (al menos la 1).
2. Escribir la librería "Core" (multiplataforma: matcher de dominios/comodines, formato de configuración, parser de SNI, plantillas). Claude la puede escribir y probar sin necesitar Windows.
3. Escribir el servicio de Windows (WFP + DNS local + IPC con la interfaz). Hay que compilarlo y probarlo en Windows real → primero en una VM, no en la máquina de nadie.
4. Escribir la interfaz WPF.
5. Instalador (Inno Setup) + firma (si hay certificado) + pruebas en VM.
6. Documentar instalación y manual de uso.

## 6. División de trabajo (quién hace qué)

- **Claude (vía el puente con esta compu):** código fuente (Core, lógica del servicio, XAML de la interfaz), documentación, scripts, investigación. Puede leer/escribir archivos en `C:\Locksuite windows` directamente.
- **Antigravity (o el usuario):** todo lo que necesite Windows de verdad: compilar, ejecutar, debuggear interactivamente, probar la interfaz con clics, generar/instalar el instalador, firmar el ejecutable, probar en la VM. Claude deja instrucciones puntuales para esto en una sección "Para Antigravity" en este archivo cuando corresponda.
- Importante: Claude **no tiene una terminal de Windows real** — el puente con esta compu permite leer/escribir archivos y correr algunos comandos en una VM Linux que ve esos archivos, pero no ejecutar el `.exe` ni abrir la interfaz. El código hay que compilarlo y probarlo del lado de Windows.

## 7. Para Antigravity (pendiente ahora)

- Nada todavía — todavía no hay código. Se va a ir completando esta sección a medida que haya pasos que necesiten Windows real (compilar, probar, firmar).

## 8. Log de sesiones

### Sesión 1 — 2026-10-20 (diseño inicial)
- Se definió la arquitectura completa (secciones 2 y 3).
- Se analizó un resumen de la arquitectura de Techloq (hecho por Antigravity) — ver `docs/ANALISIS_TECHLOQ.md`. Confirma el enfoque de WFP + default-deny; se descartan la inspección TLS y el anti-tamper contra administrador.
- Se armó este sistema de documentación y el prompt maestro.
- Pendiente: arrancar con el Core (sección 5, punto 2) en la próxima conversación, idealmente ya con la pregunta abierta 1 respondida.
