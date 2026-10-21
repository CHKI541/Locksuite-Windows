# Análisis Exhaustivo de Incomodidades, Fricciones y Fallas Potenciales
**LockSuite Windows — Filtro por Lista Blanca (WFP + DNS)**  
*Documento de auditoría preventiva y catálogo de casos límite (Edge Cases)*

---

## 1. Introducción y Propósito

El objetivo de este documento es catalogar **todas las situaciones reales** donde LockSuite pueda generar fricción al usuario o fallar técnicamente, más allá del bloqueo intencional de sitios no autorizados.

Al operar mediante una arquitectura transparente y sin driver de kernel (WFP a nivel de subcapa + resolutor DNS local con redirección NRPT), LockSuite es infinitamente más seguro contra caídas de sistema (BSOD) y no invade la privacidad como un proxy MITM. Sin embargo, en el ecosistema caótico de Windows 11 y la web moderna, existen dependencias ocultas, comportamientos de red y conflictos de software que deben ser comprendidos y mitigados.

---

## 2. Incomodidades y Fricciones de Usuario (UX y Navegación Diaria)

Son situaciones donde el filtro está funcionando exactamente como fue programado, pero **la experiencia de uso se degrada o genera confusión**:

### 2.1 Páginas web "rotas" o sin estilo (Dependencias invisibles de CDNs)
* **Descripción:** El tutor autoriza un dominio específico (por ejemplo, `facultad.edu.ar`, `banco.com.ar` o un portal de noticias comunitarias). El usuario ingresa, pero la página se ve completamente en blanco, sin tipografías, desordenada o los botones no responden.
* **Causa técnica:** Las aplicaciones web modernas raramente se alojan en un único servidor. Cargan estilos y fuentes desde CDNs externas (`fonts.googleapis.com`, `cdnjs.cloudflare.com`, `cdn.jsdelivr.net`), scripts desde gestores de etiquetas y recursos multimedia desde buckets de Amazon AWS S3 o Google Cloud Storage. Si esos dominios secundarios no están autorizados, el navegador los bloquea (NXDOMAIN).
* **Solución en LockSuite:** 
  1. El **Modo Aprendizaje** ("Bloqueados recientes") registra de inmediato estos dominios secundarios asociados al intento.
  2. El tutor puede autorizarlos con 1 solo clic desde la interfaz.
  3. Paquete predefinido "Recursos Web Comunes" para evitar tener que habilitar fuentes y CDNs básicas manualmente.

---

### 2.2 Redes Wi-Fi públicas y Portales Cautivos (Hoteles, Bares, Aeropuertos)
* **Descripción:** El usuario viaja con la notebook o va a un café/biblioteca/aeropuerto. Se conecta a la red Wi-Fi, pero **nunca aparece la página para poner la clave de la habitación o aceptar los términos y condiciones**, dejando la máquina completamente sin conexión.
* **Causa técnica:** Los portales cautivos funcionan "secuestrando" las consultas DNS iniciales o el tráfico HTTP puerto 80 para redirigir al navegador a una IP local (ej. `192.168.1.1` o `login.hotspot.com`). LockSuite, al trabajar en modo *Default-Deny* saliente y forzar el DNS a `127.0.0.1:53` mediante NRPT, bloquea la redirección del router antes de que el usuario pueda validarse.
* **Solución en LockSuite:**
  - Implementar una regla de escape temporal en WFP para la IP del Gateway local (`Default Gateway`) en el puerto 80/443 cuando la máquina detecte que no tiene salida a internet.
  - Opción "Modo Viaje / Portal Cautivo" en la GUI de LockSuite (activable con clave del tutor) que permite 5 minutos de navegación libre en la subred local para autenticarse en el Wi-Fi.

---

### 2.3 Bloqueo de Captchas (Cloudflare Turnstile, Google reCAPTCHA, hCaptcha)
* **Descripción:** El usuario entra a un sitio permitido, pero no puede iniciar sesión, enviar un formulario o finalizar una compra porque el recuadro de *"No soy un robot"* queda cargando eternamente o tira error de conexión.
* **Causa técnica:** Los proveedores de seguridad alojan los scripts de verificación en dominios propios (`challenges.cloudflare.com`, `www.google.com/recaptcha`, `hcaptcha.com`, `recaptcha.net`).
* **Solución en LockSuite:** Incluir un preset opcional en `Presets.cs` llamado "Servicios de Captcha y Verificación" que contenga estos dominios estándar pre-aprobados.

---

### 2.4 Bloqueo silencioso por DNS-over-HTTPS (DoH) en Chrome, Edge y Firefox
* **Descripción:** De forma imprevista, el navegador deja de abrir incluso los sitios que están en la lista blanca, mostrando el error `DNS_PROBE_FINISHED_BAD_CONFIG` o `ERR_NAME_NOT_RESOLVED`.
* **Causa técnica:** Los navegadores modernos traen activada la opción de "DNS Seguro" (DoH). Al detectar problemas, intentan enviar las consultas DNS directamente por HTTPS (puerto 443) a servidores de Cloudflare (`1.1.1.1`) o Google (`8.8.8.8`), salteándose la resolución nativa de Windows. Como WFP bloquea cualquier conexión directa no autorizada por nuestro resolutor local, el navegador se auto-bloquea.
* **Solución en LockSuite:**
  1. El instalador puede desplegar directivas locales (GPO / Registro de Windows) para Chrome y Edge (`BuiltInDnsClientEnabled = false`), forzándolos a usar siempre el DNS de Windows.
  2. WFP bloquea el puerto 853 (DNS-over-TLS) para evitar fugas.

---

### 2.5 Aplicaciones de escritorio vs. Versiones Web (Zoom, WhatsApp Desktop, Teams)
* **Descripción:** `web.whatsapp.com` funciona bien en el navegador, pero la aplicación instalada desde la tienda no conecta, o conecta pero las llamadas de voz no tienen audio.
* **Causa técnica:** Las aplicaciones de escritorio no solo hacen peticiones web estándar (TCP 80/443). Para la transmisión de audio y video en tiempo real utilizan protocolos WebRTC sobre UDP (STUN/TURN) en rangos de puertos dinámicos (ej: UDP 3478, 8801-8810) y servidores con rotación constante de IP.
* **Solución en LockSuite:**
  - Crear paquetes de lista blanca especializados por aplicación con sus rangos de dominios de señalización (ej. `*.zoom.us`, `*.whatsapp.net`).
  - Habilitar en WFP la apertura de puertos UDP específicos asociados a esas IPs autorizadas.

---

### 2.6 El dilema "Gmail y Drive sí, pero Buscador de Google no"
* **Descripción:** Si se autoriza `google.com` para usar Gmail, Google Chat o Drive, automáticamente queda accesible el buscador mundial de Google (`www.google.com`), que incluye imágenes, resúmenes de inteligencia artificial y noticias.
* **Causa técnica:** Google unifica casi todos sus servicios bajo el dominio raíz `google.com` y comparte cookies de autenticación mediante `accounts.google.com`.
* **Solución en LockSuite:**
  - El tutor puede autorizar subdominios puntuales: `mail.google.com`, `chat.google.com`, `drive.google.com`, `accounts.google.com`, `gstatic.com`, `googleusercontent.com`.
  - Se excluye explícitamente `www.google.com`. (Nota: algunos botones internos de Google pueden intentar redirigir al buscador, pero quedarán bloqueados con NXDOMAIN).

---

### 2.7 Desfase del reloj (NTP) y falla masiva de certificados SSL
* **Descripción:** Si la computadora se queda sin batería en la placa madre (pila CMOS) o la hora se desincroniza más de 15 minutos, **ningún sitio web carga**, arrojando el error *"Su reloj está adelantado/atrasado"* o *"La conexión no es privada (NET::ERR_CERT_DATE_INVALID)"*.
* **Causa técnica:** El protocolo HTTPS valida la fecha de validez de los certificados digitales. Si el reloj está mal y el protocolo de hora de Windows (`time.windows.com`, puerto UDP 123) está bloqueado, Windows nunca puede corregir la hora automáticamente.
* **Solución en LockSuite:** `time.windows.com` y la apertura de DHCP/NTP ya están incluidos de fábrica e inmutables en `Presets.SystemEssentials()`.

---

## 3. Fallas Técnicas Potenciales (Estabilidad, Rendimiento y Sistema)

Riesgos de bugs o comportamientos no deseados del software en la computadora:

### 3.1 "Brickeo" de red (Quedarse sin internet total por caída del servicio)
* **Descripción:** El usuario se queda sin internet por completo. No carga ningún sitio, ni siquiera los permitidos.
* **Causa técnica:** LockSuite opera en modo *Fail-Closed* por seguridad (la regla terminante de WFP bloquea toda salida salvo excepciones explícitas). Si el proceso `LockSuite.Service.exe` sufre un crash inesperado, un apagón repentino o un deadlock, los filtros WFP persisten en el kernel de Windows pero el DNS local `127.0.0.1:53` deja de responder.
* **Mitigación implementada:**
  1. Configuración de recuperación de Windows Service (`sc failure LockSuiteService reset= 86400 actions= restart/5000/restart/5000/restart/5000`): si el servicio cae, Windows lo resucita automáticamente en 5 segundos.
  2. Comando de rescate para el tutor: `LockSuite.Service.exe --reset-wfp` limpia instantáneamente los filtros WFP y la tabla NRPT restaurando la red.
  3. La parada ordenada (`sc stop` o desinstalación) remueve los filtros prolijamente antes de cerrarse.

---

### 3.2 Drenaje de Batería y Alto Uso de CPU (Tormenta de Reintentos)
* **Descripción:** La batería de la notebook dura significativamente menos o los ventiladores se aceleran con la computadora sin usar.
* **Causa técnica:** Windows 11 tiene decenas de servicios de telemetría y apps en segundo plano (OneDrive, Widgets, Edge Update, Cortana, Xbox Live). Cuando estas apps intentan conectarse a sus servidores y reciben un rechazo inmediato (NXDOMAIN), muchas entran en un **bucle agresivo de reintentos**, disparando cientos de consultas UDP por segundo. Si el servicio de LockSuite tuviera fugas de memoria o escribiera en disco en cada intento, la CPU no podría entrar en estados de bajo consumo (C-States).
* **Mitigación implementada:**
  1. `LearningManager` implementa **throttling estricto de disco**: las consultas bloqueadas se acumulan en memoria y solo se persisten a disco como máximo **una vez cada 5 segundos**.
  2. Bounded Concurrency: el resolutor DNS procesa con un semáforo de concurrencia acotado (`SemaphoreSlim(256)`) para evitar la creación descontrolada de hilos.
  3. Snapshot `volatile` libre de locks en `ConfigManager.IsAllowed` para que el chequeo de dominio tome microsegundos.

---

### 3.3 Conflicto de Puerto 53 (Bind Failure)
* **Descripción:** LockSuite no puede iniciar su resolutor DNS local porque el puerto `127.0.0.1:53` ya está siendo utilizado por otro programa.
* **Causa técnica:** Herramientas de desarrollo o virtualización como Docker Desktop, máquinas virtuales de Hyper-V, WSL2 (Subsistema de Windows para Linux), el servicio de "Conexión compartida a Internet" (ICS) o clientes VPN corporativos a veces levantan su propio DNS local en el puerto 53.
* **Mitigación implementada:**
  - `DnsResolver` tiene lógica de reintento (`MaxBindAttempts = 3`) con opción `ReuseAddress = true`.
  - Si el puerto sigue ocupado, el servicio **no crashea**: reporta `IsListening = false`, mantiene el resto del sistema seguro y la GUI muestra un banner de advertencia claro al tutor para que sepa exactamente qué resolver.

---

### 3.4 Desincronización por CDNs Anycast (IPs rotativas)
* **Descripción:** Un sitio autorizado carga bien, pero al hacer clic en un enlace interno después de 30 segundos, el navegador se queda cargando y tira "Tiempo de espera agotado".
* **Causa técnica:** Sitios como Mercado Libre, Twitter o plataformas en Cloudflare tienen miles de servidores con Anycast. Cuando LockSuite resolvió la consulta DNS, autorizó en WFP la IP `104.16.1.1` con un TTL de 3 minutos. Si el navegador o un subrecurso resuelve internamente a `104.16.1.2` (otra IP del mismo pool Anycast que no pasó por una consulta DNS fresca), WFP descartará los paquetes a esa segunda IP.
* **Mitigación implementada:**
  - El TTL mínimo de validez de regla dinámica WFP está fijado en **3 minutos** (`MinTtlSeconds = 180`) y refresca la vigencia si la IP vuelve a consultarse.
  - El modo aprendizaje captura de inmediato cualquier IP descartada para diagnóstico rápido.

---

### 3.5 Fuga o Desconexión por IPv6 (Dual-Stack)
* **Descripción:** En conexiones de fibra óptica modernas o redes móviles que usan IPv6 nativo, algunos sitios tardan hasta 10 segundos en abrirse.
* **Causa técnica:** Los navegadores envían consultas en paralelo para IPv4 (registro `A`) y para IPv6 (registro `AAAA`). Si la regla de bloqueo de WFP en la capa IPv6 no está perfectamente sincronizada con la regla IPv4, el navegador se congela esperando la respuesta de IPv6 antes de hacer el fallback por IPv4 (*Algoritmo Happy Eyeballs*).
* **Mitigación implementada:**
  - LockSuite instala filtros base y default-deny espejo tanto para IPv4 (`ALE_AUTH_CONNECT_V4`) como para IPv6 (`ALE_AUTH_CONNECT_V6`).
  - Las reglas dinámicas manejan `AddressFamily.InterNetworkV6` creando la condición de 128 bits correspondiente.

---

### 3.6 Conflictos con Antivirus Comerciales de Terceros
* **Descripción:** Si la máquina tiene instalado un antivirus comercial (Kaspersky, Bitdefender, Norton, ESET), la computadora experimenta desconexiones intermitentes o bloqueos.
* **Causa técnica:** Las suites de seguridad comercial instalan sus propios callouts y filtros en la plataforma WFP de Windows para inspeccionar el tráfico en busca de virus. Dos motores WFP interactuando sobre las mismas conexiones pueden generar condiciones de carrera si sus pesos de subcapa entran en conflicto.
* **Recomendación operativa:**
  - En equipos con LockSuite se recomienda utilizar **Windows Defender** (el antivirus nativo de Windows 11), el cual se integra perfectamente con WFP sin generar ninguna fricción ni sobrecarga.

---

## 4. Matriz de Mitigación y Resumen Ejecutivo

| Fricción / Falla | Probabilidad | Severidad | Mitigación Técnica en LockSuite | Estado |
|---|:---:|:---:|---|:---:|
| **Páginas rotas por fuentes/CDNs** | Alta | Media | Modo Aprendizaje (1-clic) + Presets enriquecidos | ✅ Resuelto en v0.3.0 |
| **Portales cautivos (Wi-Fi hotel/café)** | Media | Alta | Regla de escape para Gateway local en puerto 80 | 📋 Fase de diseño |
| **DoH en navegadores (Chrome/Edge)** | Alta | Alta | Desactivación por directiva de registro + bloqueo DoT (853) | ⚠️ A incorporar en GPO |
| **Captchas bloqueados** | Media | Media | Preset dedicado para servicios de verificación web | ✅ Configurable |
| **Brickeo por caída de servicio** | Baja | Crítica | Auto-reinicio en 5s + comando `--reset-wfp` + parada limpia | ✅ Resuelto en v0.3.0 |
| **Drenaje de batería / CPU** | Media | Media | Flush a disco cada 5s + concurrencia DNS acotada (256) | ✅ Resuelto en v0.3.0 |
| **Conflicto puerto 53 (Docker/WSL)** | Baja | Media | Bind tolerante con reintentos + aviso en GUI sin crash | ✅ Resuelto en v0.3.0 |
| **IPs cambiantes en CDNs Anycast** | Media | Baja | TTL mínimo WFP de 180s con refresco continuo | ✅ Resuelto en v0.3.0 |
| **Fuga o latencia por IPv6** | Media | Media | Reglas WFP simétricas en capas V4 y V6 | ✅ Resuelto en v0.3.0 |
| **Apps de escritorio (Zoom/Teams)** | Media | Media | Paquetes de señalización + reglas para puertos WebRTC | 📋 Configurable |

---

*Este documento forma parte de la especificación técnica viva de LockSuite Windows y se actualiza ante cada nueva versión.*
