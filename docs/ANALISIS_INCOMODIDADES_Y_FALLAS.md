# Análisis Exhaustivo de Incomodidades, Fricciones y Fallas Potenciales
**LockSuite Windows — Filtro por Lista Blanca (WFP + DNS)**  
*Documento de auditoría preventiva y catálogo de casos límite (Edge Cases)*

> **Estado de corrección (sesión 11, v0.5.0).** Este documento se revisó punto por punto contra el
> código real. En cada caso, el apartado **«Solución / Cómo se corrigió»** dice qué hace hoy el código
> y en qué versión entró. Lo que todavía requiere compilar/probar en Windows está marcado y listado en
> `HANDOFF_ANTIGRAVITY.md` (sesión 11). Los problemas de **certificados** tienen su propia sección (4),
> agregada a pedido.

---

## 1. Introducción y Propósito

El objetivo de este documento es catalogar **todas las situaciones reales** donde LockSuite pueda generar fricción al usuario o fallar técnicamente, más allá del bloqueo intencional de sitios no autorizados.

Al operar mediante una arquitectura transparente y sin driver de kernel (WFP a nivel de subcapa + resolutor DNS local con redirección NRPT), LockSuite es infinitamente más seguro contra caídas de sistema (BSOD) y no invade la privacidad como un proxy MITM. Sin embargo, en el ecosistema caótico de Windows 11 y la web moderna, existen dependencias ocultas, comportamientos de red y conflictos de software que deben ser comprendidos y mitigados.

**Cómo filtra LockSuite (para entender las soluciones):** el enforcement real es **WFP default-deny** sobre el tráfico saliente. El resolutor DNS local solo *resuelve* los dominios de la lista; al resolver, abre una regla temporal en WFP para esa IP. Por eso muchas soluciones son, simplemente, **agregar el host correcto a un paquete**: si el nombre no resuelve, la conexión no sale.

---

## 2. Incomodidades y Fricciones de Usuario (UX y Navegación Diaria)

Son situaciones donde el filtro está funcionando exactamente como fue programado, pero **la experiencia de uso se degrada o genera confusión**:

### 2.1 Páginas web "rotas" o sin estilo (Dependencias invisibles de CDNs)
* **Descripción:** El tutor autoriza un dominio específico. El usuario ingresa, pero la página se ve en blanco, sin tipografías, desordenada o los botones no responden.
* **Causa técnica:** Las webs modernas cargan estilos y fuentes desde CDNs externas (`fonts.googleapis.com`, `cdnjs.cloudflare.com`), scripts desde gestores de etiquetas y recursos desde buckets S3/GCS. Si esos dominios secundarios no están autorizados, el navegador los bloquea (NXDOMAIN).
* **Solución / Cómo se corrigió (v0.5.0):**
  1. **Paquete nuevo "Recursos web comunes (fuentes y librerías)"** (`web-commons`), **activado por defecto**: incluye Google Fonts (`fonts.googleapis.com`, `fonts.gstatic.com`), `cdnjs.cloudflare.com`, `ajax.googleapis.com`, `code.jquery.com`, Bootstrap CDN y Font Awesome. Son librerías y fuentes (no sirven contenido navegable por sí solas). A propósito **no** se incluyen `cdn.jsdelivr.net` ni `unpkg.com` (sirven archivos arbitrarios de npm/GitHub); si un sitio puntual los necesita, el tutor los agrega desde "Bloqueados recientes" (quedan con una nota explicativa).
  2. **Modo Aprendizaje** ("Bloqueados recientes"): registra los dominios secundarios y el tutor los autoriza con 1 clic.
  3. Implementado en `Presets.cs` (`WebCommonsDomains` + entrada de catálogo `IdWebCommons`). Tests en `PresetsExtraTests`.

---

### 2.2 Redes Wi-Fi públicas y Portales Cautivos (Hoteles, Bares, Aeropuertos)
* **Descripción:** El usuario se conecta a una red Wi-Fi pero **nunca aparece la página para poner la clave o aceptar los términos**, y la máquina queda sin conexión.
* **Causa técnica:** El portal cautivo "secuestra" las consultas DNS/HTTP iniciales y redirige a una IP local (el gateway). Con el default-deny + DNS forzado a `127.0.0.1`, esa redirección no llega y el usuario no puede autenticarse.
* **Solución / Cómo se corrigió (v0.5.0 — NUEVO "Modo portal cautivo"):**
  - En **Configuración → "Wi-Fi de hotel / portal cautivo"** hay un botón **"Abrir modo portal cautivo (5 min)"**. Pide la **contraseña del tutor**.
  - Mientras dura la ventana (configurable, **máximo 15 min**): (a) se abren **solo** las IP del **gateway** y de los **servidores DNS** que entregó la red —y solo si son direcciones **privadas/locales** (10.x, 172.16-31.x, 192.168.x, 169.254.x, 100.64.x/CGNAT)— reutilizando la misma regla temporal por-IP del filtrado normal; (b) se **suspende** la redirección NRPT para que el nombre del portal resuelva con el DNS de la red.
  - **El filtro NO se apaga:** el default-deny sigue activo; internet público sigue bloqueado. Solo se abre la red local para llegar a la página del portal. Al vencer, NRPT se re-aplica sola (y si el servicio se reinicia, también se re-aplica al arrancar). Hay un banner con cuenta regresiva en "Estado" y un botón "Terminar ahora".
  - Implementado en `CaptivePortal/CaptivePortalManager.cs` + comandos IPC `StartCaptivePortal`/`EndCaptivePortal` (requieren tutor) + UI.
  - **Límite honesto:** funciona con los portales alojados en el gateway/DNS de la red (el caso común). Si un portal redirige a una **IP pública propia**, puede no completar; ahí el tutor puede permitir ese dominio a mano o usar el equipo con otra red. **Requiere prueba en VM/portal real (ver HANDOFF §11).**

---

### 2.3 Bloqueo de Captchas (Cloudflare Turnstile, Google reCAPTCHA, hCaptcha)
* **Descripción:** En un sitio permitido no se puede iniciar sesión, enviar un formulario o comprar porque el recuadro *"No soy un robot"* queda cargando o da error.
* **Causa técnica:** Los proveedores alojan los scripts en dominios propios (`challenges.cloudflare.com`, `hcaptcha.com`, `www.google.com/recaptcha`, `recaptcha.net`).
* **Solución / Cómo se corrigió (v0.5.0):**
  - **Paquete nuevo "Verificación «no soy un robot»"** (`captcha`), **activado por defecto**: `challenges.cloudflare.com` (Cloudflare Turnstile), `hcaptcha.com` (cubre `newassets.`, `imgs.`, `api.`) y `recaptcha.net` (el dominio **alternativo** de Google para reCAPTCHA).
  - **Aclaración importante (documentada en el propio paquete):** el reCAPTCHA clásico se sirve desde `www.google.com/recaptcha`, y `www.google.com` está en el **Bloqueo fijo** (es el buscador). Ese reCAPTCHA **no va a funcionar**. Turnstile, hCaptcha y el reCAPTCHA servido por `recaptcha.net` sí. Es el precio de mantener el buscador bloqueado; no se abre `www.google.com` para un captcha.
  - Implementado en `Presets.cs` (`CaptchaDomains` + `IdCaptcha`). Tests en `PresetsExtraTests`.

---

### 2.4 Bloqueo silencioso por DNS-over-HTTPS (DoH) en Chrome, Edge y Firefox
* **Descripción:** De forma imprevista, el navegador deja de abrir incluso los sitios de la lista, con `DNS_PROBE_FINISHED_BAD_CONFIG` o `ERR_NAME_NOT_RESOLVED`; además, el modo aprendizaje no registra nada.
* **Causa técnica:** Los navegadores traen activado el "DNS seguro" (DoH): envían las consultas DNS por HTTPS (443) a Cloudflare/Google, salteándose la resolución nativa de Windows (y, por lo tanto, el resolutor local de LockSuite). WFP bloquea esa conexión directa, así que el navegador no resuelve y se "auto-bloquea".
* **Solución / Cómo se corrigió (v0.5.0):**
  1. **El instalador aplica directivas (registro, `HKLM\SOFTWARE\Policies`) que desactivan el DoH** en los tres navegadores:
     - Chrome y Edge: `DnsOverHttpsMode = "off"`.
     - Firefox: `DNSOverHTTPS\Enabled = 0` y `Locked = 1` (bloquea la opción para que no se re-active).
     Así los navegadores usan siempre el DNS de Windows (el de LockSuite). Se quitan al desinstalar (`uninsdeletevalue`).
  2. **DNS-over-TLS (puerto 853):** no hace falta una regla especial: el default-deny ya bloquea todo saliente no autorizado, y el servicio solo permite 53 al resolutor local; 853 queda bloqueado.
  3. La **sonda "canario" de Firefox** (`use-application-dns.net`) queda bloqueada a propósito: eso hace que Firefox **no** active su DoH automático (tiene una nota en "Bloqueados recientes").
  * Fuentes: documentación de directivas de Microsoft Edge y guía de CleanBrowsing para Chrome/Edge/Firefox.

---

### 2.5 Aplicaciones de escritorio vs. Versiones Web (Zoom, WhatsApp, Teams)
* **Descripción:** La versión web funciona pero la app de escritorio no conecta, o conecta sin audio en las llamadas.
* **Causa técnica:** Las apps de escritorio usan WebRTC sobre **UDP** (STUN/TURN) en rangos de puertos dinámicos, además de los dominios de señalización.
* **Solución / Estado (aclaración técnica):**
  - **La parte de puertos UDP ya está resuelta por diseño:** cuando el resolutor autoriza una IP (porque su dominio está permitido), la regla WFP dinámica abre esa IP **para todos los puertos y protocolos** (no solo 80/443). Es decir, el audio/video WebRTC hacia una IP ya autorizada **funciona**. Lo único necesario es **permitir los dominios de señalización** de la app (p. ej. `*.zoom.us`, `*.whatsapp.net`).
  - Esos dominios se agregan con el **Modo Aprendizaje** (la app los pide, aparecen en "Bloqueados recientes", el tutor los permite) o como paquete propio del tutor. **No se incluyen paquetes por app de fábrica a propósito:** videollamadas (Meet/Zoom) no son parte del caso kosher base y se habilitan solo si el tutor lo decide.
  - Estado: **configurable** (sin cambios de código en esta sesión; el mecanismo ya lo soporta).

---

### 2.6 El dilema "Gmail y Drive sí, pero Buscador de Google no"
* **Descripción:** Autorizar `google.com` abre también el buscador, imágenes, IA y noticias.
* **Solución / Cómo se corrigió (v0.4.0, verificado):** un **botón por servicio** (Gmail, Chat, Drive, Fotos) con hosts puntuales —nunca `google.com`, `gstatic.com`, `googleapis.com` ni `googleusercontent.com` enteros— más el paquete **"Bloqueo fijo: buscador de Google y Gemini"**, que bloquea con prioridad sobre cualquier permiso. Detalle host por host y riesgos residuales en `docs/ANALISIS_DOMINIOS_SERVICIOS.md`. ✅

---

### 2.7 Desfase del reloj (NTP) y falla masiva de certificados SSL
* **Descripción:** Si la pila CMOS se agota o la hora se desincroniza, **ningún sitio HTTPS carga** (`NET::ERR_CERT_DATE_INVALID`, "Su reloj está adelantado/atrasado").
* **Causa técnica:** HTTPS valida la fecha del certificado. Si el reloj está mal y la hora de Windows (`time.windows.com`, **UDP 123**) no se puede sincronizar, Windows nunca corrige la hora.
* **Solución / Cómo se corrigió (v0.5.0):**
  1. `time.windows.com` está en "Esenciales del sistema" (resuelve y abre su IP).
  2. **NUEVO:** se agregó un **permiso base de WFP para NTP (UDP 123)**, de modo que la sincronización de hora funciona de forma robusta aunque la regla dinámica por-IP todavía no esté activa al arrancar. Es un permiso aditivo *best-effort* (su eventual fallo no afecta al filtro base). Implementado en `WfpManager` (`KeyNtpV4`, `TryAddPortPermit`).
  3. **Caveat documentado (comportamiento de Windows, no corregible por el filtro):** si el reloj está desfasado **muchas horas** (p. ej. pila agotada y fecha de fábrica), el servicio de hora de Windows (W32Time) puede **rechazar** la corrección por superar `MaxPos/MaxNegPhaseCorrection`. En ese caso el tutor debe poner la fecha a mano aproximada una vez; después se sincroniza sola.

---

## 3. Fallas Técnicas Potenciales (Estabilidad, Rendimiento y Sistema)

### 3.1 "Brickeo" de red (quedarse sin internet total)
* **Causa técnica:** Fail-closed: si `LockSuite.Service.exe` crashea, los filtros WFP persisten pero el DNS local deja de responder.
* **Mitigación / Cómo se corrigió:** (1) auto-reinicio del servicio (`sc failure`, 3× cada 5 s); (2) comando `--reset-wfp` de rescate; (3) la **parada ordenada** revierte WFP y NRPT; (4) **v0.4.1:** el arranque aplica NRPT **solo si el resolutor DNS está escuchando**: si el puerto 53 estaba ocupado ya no se redirige el DNS a un puerto muerto (evita cortar internet). ✅

---

### 3.2 Drenaje de Batería y Alto Uso de CPU (tormenta de reintentos)
* **Causa técnica:** Apps de Windows en segundo plano reintentan agresivamente al recibir NXDOMAIN.
* **Mitigación / Cómo se corrigió:** (1) `LearningManager` vuelca a disco a lo sumo cada 5 s; (2) resolutor DNS con concurrencia acotada (`SemaphoreSlim(256)`); (3) `IsAllowed` lee un snapshot inmutable sin lock; (4) **v0.4.1:** si el bucle de recepción falla, espera 50 ms para no quedar en bucle caliente. ✅

---

### 3.3 Conflicto de Puerto 53 (bind failure)
* **Causa técnica:** Docker Desktop, Hyper-V, WSL2, ICS o un VPN pueden ocupar `127.0.0.1:53`.
* **Mitigación / Cómo se corrigió:** (1) `DnsResolver` reintenta el bind 3× con `ReuseAddress`; si no puede, **no crashea**, deja `IsListening=false` y el servicio sigue en pie (la red queda bloqueada = fail-closed); (2) **v0.4.1:** en ese estado **no se aplica NRPT** (no se corta internet por redirigir a un puerto muerto); (3) **v0.5.0 (NUEVO):** la GUI muestra un **cartel ámbar** claro en "Estado" ("El resolutor DNS local no está escuchando…"), no solo un renglón de detalle (propiedad `IsDnsDown`). ✅

---

### 3.4 Desincronización por CDNs Anycast (IPs rotativas)
* **Mitigación / Cómo se corrigió:** el TTL mínimo de las reglas dinámicas WFP es de **180 s** y se refresca al re-consultar; el modo aprendizaje captura IPs descartadas. ✅

---

### 3.5 Fuga o Desconexión por IPv6 (Dual-Stack)
* **Mitigación / Cómo se corrigió:** filtros base y default-deny espejo en IPv4 (`ALE_AUTH_CONNECT_V4`) e IPv6 (`ALE_AUTH_CONNECT_V6`); las reglas dinámicas crean la condición de 128 bits para IPv6.
* **NUEVO (v0.5.0):** se agregó un **permiso base para DHCPv6 (UDP 547)**. Sin esto, en una red **solo-IPv6** el equipo no obtenía dirección ni DNS. Permiso aditivo *best-effort* (`KeyDhcpV6`). ✅

---

### 3.6 Conflictos con Antivirus Comerciales de Terceros
* **Causa técnica:** Las suites comerciales instalan sus propios callouts/filtros WFP; dos motores sobre las mismas conexiones pueden competir por pesos de subcapa.
* **Recomendación operativa:** usar **Windows Defender** (nativo, se integra con WFP sin fricción). **Importante (ver 4.5):** algunos antivirus hacen **inspección HTTPS** instalando su **propia CA raíz**; eso es un tema de certificados aparte y conviene desactivarlo en equipos con LockSuite.

---

### 3.7 Caché DNS negativa: "lo permití y sigue bloqueado" (NUEVO)
* **Descripción:** El tutor permite un dominio, pero durante unos minutos sigue sin abrir.
* **Causa técnica:** Cuando un dominio estaba bloqueado, LockSuite respondió **NXDOMAIN** y Windows **cachea esa respuesta negativa**. Al permitirlo, el navegador puede seguir usando el NXDOMAIN cacheado hasta que expire.
* **Solución / Cómo se corrigió (v0.5.0):** cada vez que cambia la configuración (permitir/quitar un dominio, activar un servicio), el servicio ejecuta **`ipconfig /flushdns`** para vaciar la caché del sistema, así el dominio recién permitido abre enseguida. Implementado en `DnsConfigurator.FlushDnsCache()`, disparado desde `Worker` al evento `ConfigManager.ConfigChanged`. La GUI, además, avisa "si ya tenías la página abierta, recargala (F5)".

---

### 3.8 Windows se queda sin actualizaciones de seguridad y el antivirus con firmas viejas (NUEVO)
* **Descripción:** Con el tiempo, Windows deja de recibir parches y Microsoft Defender no actualiza sus firmas. **Es una falla silenciosa** (no se ve ningún error), pero deja el equipo expuesto.
* **Causa técnica:** Windows Update y Defender usan una familia de dominios (`*.windowsupdate.com`, `*.delivery.mp.microsoft.com`, `*.update.microsoft.com`, `definitionupdates.microsoft.com`, `*.wd.microsoft.com`, etc.) que no estaban en la lista.
* **Solución / Cómo se corrigió (v0.5.0):** **paquete nuevo "Actualizaciones de Windows y Defender"** (`windows-update`), **activado por defecto**, con los hosts oficiales de Microsoft (Windows Update, Optimización de distribución, firmas de Defender, protección en la nube/MAPS y SmartScreen). **No** incluye la Tienda, Bing, MSN ni Copilot (siguen bloqueados). Fuente: documentación oficial de Microsoft. Tests en `PresetsExtraTests`.

---

### 3.9 La red local queda bloqueada (impresoras, router, NAS) (NUEVO)
* **Descripción:** No se puede imprimir en la impresora de red, entrar a la configuración del router (`192.168.x.x`) ni ver un NAS local.
* **Causa técnica:** El default-deny corta **todo** el saliente, incluidas las direcciones privadas de la red de casa.
* **Solución / Cómo se corrigió (v0.5.0):** opción **"Permitir la red local"** en Configuración (requiere tutor; **desactivada por defecto**). Al activarla, se agregan permisos WFP para los rangos **privados** (`10.0.0.0/8`, `172.16.0.0/12`, `192.168.0.0/16`, `169.254.0.0/16`). **Internet sigue filtrado igual** (esas direcciones no salen a internet). Se aplica/quita **en vivo**, sin reiniciar el servicio. Implementado en `WhitelistConfig.AllowLocalNetwork`, `WfpManager.SetAllowLocalNetwork()` (permisos aditivos *best-effort* con dirección+máscara) e IPC `SetAllowLocalNetwork`.

---

## 4. Problemas de Certificados (TLS/HTTPS) — sección nueva (sesión 11)

Esta sección reúne, a pedido, **todas** las formas en que un sitio HTTPS puede fallar por certificado con un filtro de lista blanca (no solo con Cloudflare), y qué hace LockSuite en cada caso. El síntoma típico es **"La conexión no es privada"** / `NET::ERR_CERT_…` / `SEC_ERROR_…`.

**Por qué pasa:** validar un certificado no es solo "tenerlo". El navegador suele necesitar, **además del sitio**, conectarse a terceros: a la **CA** para el estado del certificado (OCSP/CRL) y para los **certificados intermedios** (AIA), y al propio Windows/navegador para actualizar su **almacén de raíces** y sus **listas de revocación**. Si esos terceros están bloqueados, el certificado no valida.

### 4.1 Validación OCSP/CRL y certificados intermedios (AIA) de las CAs
* **Causa:** el certificado del sitio apunta (en sus extensiones AIA/CRL) a hosts de su autoridad certificante. Si están bloqueados, la validación es lenta (timeout) o falla.
* **Solución / Cómo se corrigió (v0.5.0):** se agregaron a **"Esenciales del sistema"** los hosts de PKI (no contenido) de las CAs más usadas:
  - **Let's Encrypt** → `lencr.org` *(clave: Let's Encrypt sirve OCSP/CRL/intermedios desde `o.lencr.org`/`c.lencr.org`, **no** desde `letsencrypt.org`; antes faltaba)*.
  - **Amazon** (sitios en AWS/CloudFront) → `amazontrust.com`.
  - **DigiCert** → `digicert.com` (+ marcas `geotrust.com`, `thawte.com`, `rapidssl.com`, `quovadisglobal.com`).
  - **Sectigo/Comodo** → `sectigo.com`, `usertrust.com`, `comodoca.com`.
  - **Google Trust Services** → `pki.goog`. **SSL.com** → `ssl.com` + **Certum** `certum.pl`/`ocsp-certum.com`. **GlobalSign** → `globalsign.com`. **Entrust** → `entrust.net`. **IdenTrust** → `identrust.com`. **Microsoft** → `msocsp.com`, `oneocsp.microsoft.com`, `crl.microsoft.com`, `mscrl.microsoft.com`. **GoDaddy/Starfield** → `ocsp./crl./certificates.godaddy.com`, `*.starfieldtech.com`.
  - Tests en `PresetsExtraTests.SystemEssentials_AllowsCertificateValidationHosts`.

### 4.2 Actualización de los certificados RAÍZ de Windows
* **Causa:** Windows descarga raíces nuevas **bajo demanda** desde `ctldl.windowsupdate.com`. Si está bloqueado, los programas que usan el almacén de Windows (Edge incluido) muestran "La conexión no es privada" con sitios cuya raíz todavía no está instalada (p. ej. raíces nuevas de CAs recientes).
* **Solución (v0.5.0):** `ctldl.windowsupdate.com` está en "Esenciales del sistema" **y** en el paquete de Windows Update.

### 4.3 Cloudflare y las "CAs rotativas"
* **Causa:** Cloudflare (Universal/Advanced SSL) emite sus certificados con **tres** autoridades según el caso: **Let's Encrypt, Google Trust Services y SSL.com** (con respaldo de Sectigo). Un sitio detrás de Cloudflare puede, de un día para otro, presentar un certificado de cualquiera de ellas. Si falta la PKI de alguna, ese sitio empieza a fallar sin que hayas cambiado nada.
* **Solución (v0.5.0):** las tres CAs de Cloudflare están cubiertas en 4.1 (`lencr.org`, `pki.goog`, `ssl.com`/`certum.pl`), más la actualización de raíces de 4.2. Así un sitio de Cloudflare valida sin importar con cuál de las tres le tocó el certificado. Fuente: documentación de "Certificate authorities" de Cloudflare.

### 4.4 Listas de certificados de los **navegadores** (no solo de Windows)
* **Causa:** Chrome y Firefox cada vez dependen menos del almacén de Windows y más de **lo suyo**: Chrome usa su **Chrome Root Store** y **CRLSet**; Firefox trae su propio almacén y hace **"intermediate preloading"** (baja los intermedios que los sitios mal configurados no mandan) y revocación **CRLite**. Si el navegador no puede actualizar esas listas, con el tiempo aparecen errores de certificado (`SEC_ERROR_UNKNOWN_ISSUER` en Firefox con sitios mal configurados, raíces nuevas desconocidas en Chrome).
* **Solución / Cómo se corrigió (v0.5.0):** **paquete nuevo "Actualizaciones de navegadores y sus certificados"** (`browser-updates`), **activado por defecto**: `msedge.api.cdp.microsoft.com` y `edge.microsoft.com` (Edge); `tools.google.com`, `update.googleapis.com`, `dl.google.com`, `edgedl.me.gvt1.com` (Chrome); `firefox.settings.services.mozilla.com`, `firefox-settings-attachments.cdn.mozilla.net`, `content-signature-2.cdn.mozilla.net`, `aus5.mozilla.org` (Firefox: Remote Settings = intermedios + CRLite, y actualizaciones). Fuentes: Microsoft Edge endpoints, ayuda de Chrome Enterprise, blog de seguridad de Mozilla.

### 4.5 Otro filtro o antivirus con su **propia CA raíz** (inspección HTTPS)
* **Causa:** Productos como **Techloq/NetFree** o antivirus con "escaneo HTTPS/SSL" (Kaspersky, Bitdefender, ESET, Avast) instalan una **CA raíz propia** y hacen MITM del tráfico. En un equipo con LockSuite esto genera dos problemas: (a) si ese producto se desinstala mal, puede quedar una raíz huérfana y dar errores; (b) dos filtros de red (su motor WFP + el de LockSuite) pueden competir.
* **Qué hace LockSuite:** **no** hace MITM ni instala/saca ninguna CA raíz (decisión de alcance: nada de inspección TLS con CA propia). Recomendación operativa para el tutor: en un equipo con LockSuite, **desactivar el "escaneo HTTPS/SSL" del antivirus** (o usar Windows Defender, que no lo hace) y, si antes había otro filtro con MITM (p. ej. una prueba de Techloq), **quitar su CA raíz** si quedó. Documentado también en 3.6.

### 4.6 Host de validación de una CA no incluida (CAs chicas o locales)
* **Causa:** un sitio del gobierno, de un banco local o de una institución puede usar una CA que no está en la lista de 4.1 (p. ej. una CA nacional). Su OCSP/CRL queda bloqueado y el sitio valida lento o falla.
* **Solución / Cómo se corrigió (v0.5.0):** "Bloqueados recientes" ahora **reconoce y explica** los hosts que parecen de validación de certificados: si el host empieza con `ocsp`/`ocsp2`/`crl`/`crl3`… muestra una nota ("Parece un servidor de validación de certificados (OCSP/CRL)… conviene permitirlo si un sitio permitido muestra «La conexión no es privada»"). Así el tutor sabe que es seguro permitirlo con 1 clic. Implementado en `Presets.LooksLikeCertificateInfrastructure()` + `ExplainBlocked()`. Tests en `PresetsExtraTests`.

### 4.7 Reloj desfasado → certificado "todavía no válido / vencido"
* Es el caso **2.7** visto desde el certificado. Corregido con el permiso NTP de 2.7; caveat de reloj muy desfasado documentado ahí.

---

## 5. Matriz de Mitigación y Resumen Ejecutivo

| Fricción / Falla | Prob. | Sev. | Mitigación en LockSuite | Estado |
|---|:---:|:---:|---|:---:|
| Páginas rotas por fuentes/CDNs | Alta | Media | Paquete "Recursos web comunes" (on) + Modo Aprendizaje 1-clic | ✅ v0.5.0 |
| Portales cautivos (Wi-Fi hotel/café) | Media | Alta | "Modo portal cautivo": abre solo red local + suspende NRPT, con tutor y autovencimiento | ✅ v0.5.0 · probar en VM |
| Captchas bloqueados | Media | Media | Paquete "Verificación no soy un robot" (Turnstile/hCaptcha/recaptcha.net) | ✅ v0.5.0 |
| DoH en navegadores (Chrome/Edge/Firefox) | Alta | Alta | Directivas de registro que desactivan DoH (instalador) + 853 ya bloqueado | ✅ v0.5.0 |
| Apps de escritorio (Zoom/Teams) | Media | Media | Regla por-IP abre todos los puertos (WebRTC ya funciona) + señalización por aprendizaje | ⚙️ Configurable |
| Buscador de Google / Gemini abiertos | — | Crítica | Botones por servicio + Bloqueo fijo con prioridad | ✅ v0.4.0 |
| Reloj / NTP → certificados | Media | Alta | Permiso base NTP (UDP 123) + `time.windows.com`; caveat reloj muy desfasado | ✅ v0.5.0 |
| Brickeo por caída de servicio | Baja | Crítica | Auto-reinicio 5s + `--reset-wfp` + parada limpia + NRPT solo si DNS escucha | ✅ v0.4.1 |
| Drenaje de batería / CPU | Media | Media | Flush 5s + concurrencia 256 + backoff 50ms | ✅ v0.4.1 |
| Conflicto puerto 53 (Docker/WSL) | Baja | Media | Bind tolerante + no aplica NRPT + **cartel** en GUI | ✅ v0.5.0 |
| IPs cambiantes (Anycast) | Media | Baja | TTL mínimo 180s con refresco | ✅ |
| IPv6 / redes solo-IPv6 | Media | Media | Filtros V4+V6 espejo + **permiso DHCPv6** | ✅ v0.5.0 |
| Caché DNS negativa ("lo permití y sigue") | Alta | Media | `ipconfig /flushdns` al cambiar la config | ✅ v0.5.0 |
| Sin Windows Update / firmas de Defender | Media | Alta | Paquete "Actualizaciones de Windows y Defender" (on) | ✅ v0.5.0 |
| Red local bloqueada (impresora/router) | Media | Baja | Opción "Permitir red local" (rangos privados, off por defecto) | ✅ v0.5.0 |
| **Certificados: OCSP/CRL/AIA de CAs** | Alta | Alta | PKI de las CAs grandes en Esenciales (incl. `lencr.org`, `amazontrust.com`, Cloudflare) | ✅ v0.5.0 |
| **Certificados: raíces de Windows** | Media | Alta | `ctldl.windowsupdate.com` permitido | ✅ v0.5.0 |
| **Certificados: listas de los navegadores** | Media | Media | Paquete "Actualizaciones de navegadores" (Root Store/CRLSet/CRLite) | ✅ v0.5.0 |
| **Certificados: CA chica/local** | Baja | Media | Nota automática de OCSP/CRL en "Bloqueados recientes" + permitir 1-clic | ✅ v0.5.0 |
| **Certificados: otro AV/filtro con CA propia** | Baja | Media | No se hace MITM; recomendación de desactivar escaneo HTTPS | ⚙️ Operativo |

**Riesgos residuales conocidos (honestos):** (1) **IPs compartidas** de grandes proveedores sin inspección de SNI — mejora de **fase 2 (SNI)** ya prevista en `ANALISIS_VIABILIDAD.md`; (2) **portal cautivo** en IP pública propia (2.2); (3) reloj desfasado muchas horas (2.7); (4) `github.com`/`githubusercontent.com` abiertos para todos (los usa el auto-update) — ver `ANALISIS_DOMINIOS_SERVICIOS.md` §11. Todo lo marcado "probar en VM" está en `HANDOFF_ANTIGRAVITY.md` (sesión 11).

---

*Este documento forma parte de la especificación técnica viva de LockSuite Windows y se actualiza ante cada nueva versión.*
