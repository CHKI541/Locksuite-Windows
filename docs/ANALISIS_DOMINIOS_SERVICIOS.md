# Análisis de dominios por servicio — botones "Permitir Gmail", etc. (Sesión 9)

Fecha: 2026-10-21. Autor: Claude (sin terminal Windows). Código: `src/LockSuite.Core/Presets.cs` (listas), `PresetSync.cs` (migración), `DomainMatcher.cs` (sintaxis nueva).

## 1. Qué se pidió y qué se decidió

Botones para permitir **completo** (que funcione y se vea bien) cada uno de estos servicios: **Gmail, Google Chat, Google Drive, Google Fotos, prog.co.il y mitmachim.top**, **sin** abrir nada que deje de hacer kosher la computadora. En particular, el **buscador de Google y Gemini tienen que seguir bloqueados**.

Hasta la v0.3.0 el paquete "Google (Gmail, Chat, Drive, Fotos)" habilitaba `google.com`, `googleapis.com`, `gstatic.com` y `googleusercontent.com` **enteros**, más `google.com.ar`. Eso dejaba abiertos el buscador (`www.google.com`, `google.com.ar`), Gemini (`gemini.google.com`), Noticias, Libros, Play, Maps, el almacenamiento público de Google Cloud (`storage.googleapis.com`), la API de Gemini, etc. **Era el agujero kosher más grande del producto.**

Diseño nuevo:

1. **Un paquete por servicio**, con hosts puntuales (nunca `google.com` entero). Cada paquete trae también el inicio de sesión y los estáticos de Google, así cada botón funciona solo.
2. **Paquete "Bloqueo fijo: buscador de Google y Gemini"** (paquete de *bloqueo*): sus dominios quedan bloqueados **aunque otro paquete los permita**. Protege también si alguien agrega `google.com` a mano.
3. **Sintaxis nueva de patrones** para poder ser preciso:
   - `=ejemplo.com` → solo ese host exacto (para bloquear `google.com` sin bloquear `accounts.google.com`).
   - `lh*.ejemplo.com` / `*-docs.ejemplo.com` → comodín **dentro de una sola etiqueta** (nunca cruza un punto). Necesario para familias de hosts de Google como `lh3`, `lh7-rt`, `ci3`…`ci6`, `doc-0s-1a-docs`.
4. **Migración automática** (`PresetSync`): las instalaciones existentes pasan del paquete viejo a los nuevos al actualizarse, conservando si estaba habilitado o no; lo que el tutor había agregado a mano se mueve a "Personalizados".

## 2. Cómo filtra LockSuite (por qué importa el nivel "host")

LockSuite filtra por **nombre de host** (DNS local + WFP). No ve la ruta de la URL (no hay inspección TLS, a propósito). Consecuencias:

- Se puede permitir `mail.google.com` y bloquear `www.google.com`, porque son hosts distintos.
- **No** se puede permitir "solo una parte" de un host: si un host sirve cosas buenas y malas (p. ej. `www.google.com` sirve el buscador y también reCAPTCHA), se bloquea entero.
- Un patrón `ejemplo.com` habilita **todos** sus subdominios. Por eso nunca se usa `google.com`, `googleapis.com`, `gstatic.com` ni `googleusercontent.com` a secas.

## 3. Metodología y fuentes

1. **Listas oficiales de Google para firewalls** (Gmail, Drive y Sites, Gemini, allowlist de Workspace). Son listas "para que todo funcione en una empresa": incluyen `www.google.com` y `play.google.com`, que acá se excluyen a propósito.
2. **Captura real en navegador** (panel de navegador de Claude en la PC del usuario, 2026-10-21): recursos (`performance.getEntriesByType('resource')`), iframes y scripts de: `mitmachim.top` (inicio, un tema, registro, login), `www.prog.co.il` (inicio, login, registro, un hilo → desafío de Cloudflare), página de login de Google, redirecciones sin sesión de Gmail/Drive/Chat/Fotos, y un Documento y una Presentación públicos de Google. *Nota:* esa PC hoy navega detrás de Techloq; algún recurso podría no haberse cargado por ese filtro.
3. **Fuentes secundarias**: Netify (dominios primarios por aplicación), listas de bloqueo de Gemini publicadas (nullgen), informes sobre "funciones inteligentes" de Gmail.
4. **Lo que no se pudo medir**: el tráfico de Gmail/Drive/Chat/Fotos **con sesión iniciada** (no había cuenta de prueba y no corresponde entrar a la cuenta de nadie). Para eso está la validación de la sección 11 (con el modo aprendizaje encendido).

## 4. Base común de Google (incluida en Gmail, Chat, Drive y Fotos)

| Host / patrón | Para qué | Fuente |
|---|---|---|
| `accounts.google.com` | Inicio de sesión, sesión, OAuth | Captura + oficial |
| `accounts.google.com.ar`, `accounts.google.co.il` | Cookies de sesión por país (Argentina / Israel) | Oficial (allowlist Workspace: `accounts.google.co.[país]`) |
| `accounts.youtube.com` | **Solo** el iframe `CheckConnection` del login (verifica la sesión). **No** habilita YouTube (`www.youtube.com`, `googlevideo.com`, `ytimg.com` siguen bloqueados) | Captura + oficial |
| `myaccount.google.com` | "Administrar tu cuenta" (contraseña, seguridad) | — |
| `gds.google.com` | Pantallas que aparecen después del login ("confirmá tu teléfono de recuperación") | — |
| `policies.google.com` | Privacidad y condiciones (alta de cuenta) | — |
| `ogs.google.com` | Barra superior: menú de la cuenta y **"Cerrar sesión"** | Oficial |
| `apis.google.com` | Cargador de APIs (diálogos de compartir, selectores) | Captura + oficial |
| `clients1…clients6.google.com` | APIs internas. **`clients6.google.com` incluye `*-pa.clients6.google.com`** (contactos `people-pa`, tiempo real `signaler-pa`, verificación `waa-pa`, personas `peoplestack-pa`, Drive `drivefrontend-pa`, …). Son muchas y cambian, por eso se habilitan juntas; las de IA las corta el Bloqueo fijo | Captura + oficial |
| `www.googleapis.com`, `content.googleapis.com`, `oauth2.googleapis.com`, `oauthaccountmanager.googleapis.com` | APIs (Drive, tokens, proxy de gapi) | Oficial |
| `www.gstatic.com`, `ssl.gstatic.com`, `fonts.gstatic.com`, `fonts.googleapis.com` | Scripts, íconos, fuentes (Google Sans, emojis) | Captura + oficial |
| `lh*.googleusercontent.com` | Fotos de perfil e imágenes alojadas por Google (`lh3`, `lh4`, `lh5`, `lh6`, `lh7-rt`, …) | Oficial + Gemini list |
| `usercontent.google.com` (y subdominios) | Dominio nuevo de contenido de usuario: `drive.usercontent.google.com` (descargas), `photos.fife.usercontent.google.com` (imágenes de Fotos) | Captura (Presentación pública) + oficial + Netify |

**Excluidos a propósito de la base** (y de todos los paquetes): `www.google.com`, `google.com`, `play.google.com` (tienda Play; Google lo usa para estadísticas — bloquearlo no rompe nada), `csp.withgoogle.com` (reportes), `ogads-pa.clients6.google.com` (promociones), `gstatic.com`/`googleapis.com`/`googleusercontent.com` enteros.

## 5. Gmail

| Host / patrón | Para qué |
|---|---|
| `mail.google.com` | Gmail |
| `gmail.com`, `googlemail.com` | Atajos que redirigen a Gmail. `gmail.com` incluye `imap.`/`smtp.gmail.com` (Thunderbird/Outlook) |
| `mail-attachment.googleusercontent.com`, `mail-attachment.google.com` | Adjuntos: vista previa y descarga |
| `ci*.googleusercontent.com` | **Proxy de imágenes** de Gmail (`ci3`…`ci6`): las imágenes de los correos se ven a través de Google |
| `drive-thirdparty.googleusercontent.com` | Íconos de tipo de archivo |
| `lh*.google.com` | Miniaturas de adjuntos de Drive |
| `client-channel.google.com` (+ `0.`, `1.`, …) | Canal en tiempo real |
| `contacts.google.com` | Contactos y tarjetas de contacto |
| `calendar.google.com`, `tasks.google.com`, `keep.google.com` | Invitaciones y panel lateral |

Excluidos: `mail-ads.google.com` (publicidad de Gmail), `meet.google.com` (videollamadas), `chat.google.com` (es el botón de Chat), `docs.google.com`/`drive.google.com` (son el botón de Drive; para previsualizar adjuntos de Office conviene activar Drive también).

**Entrar sin sesión iniciada:** `mail.google.com/mail/` va directo al login (verificado). `gmail.com` sin sesión redirige a `workspace.google.com` (página publicitaria, bloqueada). Por eso la tarjeta tiene un botón **"Abrir"** que usa la dirección directa.

## 6. Google Chat

| Host / patrón | Para qué |
|---|---|
| `chat.google.com` | Chat (la app web). `chat.google.com/u/0/` va directo al login |
| `client-channel.google.com`, `*-pa.clients6.google.com` (base) | Tiempo real (`signaler-pa`, `chat-pa`), personas (`peoplestack-pa`) |
| `contacts.google.com` | Tarjetas de contacto |
| `ci*.googleusercontent.com`, `lh*.googleusercontent.com` (base) | Imágenes y vistas previas |

Excluidos a propósito: **GIFs de Tenor** (`tenor.com`, `media.tenor.com`, `tenor.googleapis.com`: es un buscador de imágenes animadas), `meet.google.com` (videollamadas/"huddles"). El Chat **dentro de Gmail** (`mail.google.com/chat`) necesita además el botón de Gmail.

## 7. Google Drive (incluye Documentos, Hojas, Presentaciones y Formularios)

| Host / patrón | Para qué | Fuente |
|---|---|---|
| `drive.google.com` (+ `*.drive.google.com`) | Drive; `*.c.drive.google.com` reproduce videos | Oficial |
| `docs.google.com` (+ `0.docs`, `1.docs`, `*.c.docs`) | Documentos, Hojas, Presentaciones, Formularios | Captura + oficial |
| `sheets.`, `slides.`, `forms.`, `spreadsheets.google.com` | Atajos que redirigen a `docs.google.com` | Oficial |
| `drivefrontend-pa.googleapis.com`, `drivelabels.googleapis.com` | APIs de Drive | Netify |
| `fcmconnection.googleapis.com`, `signaler-pa.googleapis.com` | "Drive para computadora" | Oficial |
| `drive-thirdparty.googleusercontent.com` | Íconos | Netify |
| `*-docs.googleusercontent.com` | Exportaciones/descargas (`doc-0s-1a-docs…`) | Oficial (`*.googleusercontent.com`) |
| `*-apidata.googleusercontent.com` | Descargas por API | — |
| `lh*.google.com` | Miniaturas (`lh3.google.com`) | Oficial (`lh[0-9].google.com`) |
| `inputtools.google.com` | Teclados/herramientas de escritura de Documentos | Oficial |
| `contacts.google.com` | Tarjetas de contacto, diálogo Compartir | Captura |

Excluidos a propósito (aunque estén en la lista oficial de Google): `script.google.com` (Apps Script: permite armar páginas web y proxies), `sites.google.com` y `*.googlegroups.com` (contenido de terceros), `takeout.google.com`, `googledrive.com`, `ipv4.google.com` (**es el buscador**), `video.google.com`, `s.ytimg.com`, `youtube.googleapis.com` (infraestructura de YouTube), `*.gvt1.com`.

**Videos:** si la vista previa de un video de Drive dependiera de la infraestructura de YouTube, no se va a reproducir (queda bloqueada a propósito); se puede descargar. A verificar (sección 11).

## 8. Google Fotos

| Host / patrón | Para qué | Fuente |
|---|---|---|
| `photos.google.com` | Fotos. `photos.google.com/login` va directo al login | Captura |
| `photos.fife.usercontent.google.com` (vía `usercontent.google.com`) | Imágenes | Netify |
| `lh*.googleusercontent.com` (base) | Imágenes | — |
| `photosdata-pa.googleapis.com`, `photos.googleapis.com`, `photosfirstparty-pa.googleapis.com` | APIs de Fotos | Netify |
| `video-downloads.googleusercontent.com` | Descarga de videos | — |
| `photos.app.goo.gl` | Enlaces para compartir álbumes | — |

Excluidos: mapas (`maps.googleapis.com`, `maps.gstatic.com`, Street View), Lens (`lens.google.com`, está en el Bloqueo fijo). Sin sesión, `photos.google.com` redirige a `www.google.com/photos/about` (bloqueado): usar "Abrir".

## 9. Bloqueo fijo: buscador de Google y Gemini (prioridad sobre todo lo permitido)

| Grupo | Hosts |
|---|---|
| Buscador | `=google.com`, `www.google.com`, `ipv4.`/`ipv6.`/`encrypted.google.com`, `images.google.com`, `lens.google.com`, `news.google.com`, `books.google.com`, `=google.com.ar`, `www.google.com.ar`, `=google.co.il`, `www.google.co.il` |
| Gemini y otras IA | `gemini.google.com`, `bard.google.com`, `gemini.google`, `aistudio.google.com`, `notebooklm.google.com`, `notebooklm.google`, `labs.google`, `labs.google.com`, `assistant.google.com`, `gemini.gstatic.com`, `generativelanguage.googleapis.com`, `aiplatform.googleapis.com`, `*-aiplatform.googleapis.com`, `robinfrontend-pa.*`, `opa-pa.*`, `aisandbox-pa.googleapis.com`, `alkalimakersuite-pa.clients6.google.com` |
| **Gemini dentro de Gmail/Documentos/Drive** | `appsgenaiserver-pa.clients6.google.com` (observado: un Documento público lo llama 4 veces aunque no haya sesión) |
| Promociones dentro de las apps | `ogads-pa.clients6.google.com` |

Por qué hace falta aunque las listas no incluyan `google.com`: los paquetes de Google habilitan `clients6.google.com` con sus subdominios (las APIs de las apps), y ahí también viven backends de IA. El bloqueo los corta con prioridad. Además protege si alguien agrega `google.com` por error (la interfaz avisa).

**Recomendación adicional (cuenta de Google del usuario):** Gmail → Configuración → "Funciones inteligentes" y "Funciones inteligentes de Google Workspace" → desactivar. Así Gemini no aparece en Gmail/Documentos ni siquiera como opción. (La cuenta la controla su dueño; el bloqueo de red es lo que el usuario no puede deshacer.)

## 10. prog.co.il y mitmachim.top

### prog.co.il (XenForo, detrás de Cloudflare) — capturado
| Host | Para qué | Decisión |
|---|---|---|
| `prog.co.il` (+ `www.`, `s3.`, `amember.`) | El sitio, sus imágenes y avisos propios, membresías | ✅ |
| `challenges.cloudflare.com` | Verificación "Un momento…" de Cloudflare: **sin esto los hilos no abren** (observado) | ✅ |
| `code.jquery.com` | jQuery: sin esto el sitio no funciona | ✅ |
| `cdnjs.cloudflare.com` | Carruseles (Swiper, OwlCarousel) | ✅ |
| `cdn.jsdelivr.net` | Estilos de carrusel, calendario hebreo (hebcal) | ✅ (ver riesgos) |
| `www.hebcal.com` | Fecha hebrea y zmanim | ✅ |
| `prod-progsite-s3.s3.eu-central-1.wasabisys.com` | Archivos del sitio (bucket propio) | ✅ |
| `s3.eu-central-003.backblazeb2.com` | Imágenes de secciones (bucket `prog-www`) | ✅ (ver riesgos) |
| `cdn.userway.org`, `api.userway.org` | Botón de accesibilidad | ✅ |
| `accounts.google.com` y estáticos | "Ingresar con Google" | ✅ |
| `www.googletagmanager.com`, `analytics.google.com`, `stats.g.doubleclick.net`, `www.google.com` (conversiones), `connect.facebook.net`, `static.cloudflareinsights.com`, `mpc2-…a.run.app` | Estadísticas/publicidad/seguimiento | ❌ (no hacen falta) |
| `www.weather2day.co.il` | Widget del clima | ❌ (opcional; queda el recuadro vacío) |
| Imágenes externas en mensajes, videos de YouTube incrustados | Contenido de terceros | ❌ a propósito |

### mitmachim.top (NodeBB) — capturado
| Host | Para qué | Decisión |
|---|---|---|
| `mitmachim.top` | El foro, sus imágenes subidas y el socket en tiempo real | ✅ |
| `fonts.googleapis.com`, `fonts.gstatic.com` | Fuentes hebreas (Assistant, Arimo) | ✅ |
| `accounts.google.com` y estáticos | "Ingresar con Google" | ✅ |
| `www.googletagmanager.com`, `www.google-analytics.com` | Estadísticas | ❌ |
| `www.google.com/recaptcha` + `www.gstatic.com/recaptcha` | reCAPTCHA del **registro** de usuarios nuevos | ❌ (`www.google.com` es el buscador). Entrar con cuenta existente funciona; registrarse: "Ingresar con Google" o desde otro equipo |

Además, mitmachim tiene un botón "Buscar en Google" (`www.google.co.il/search?q=site:mitmachim.top…`): queda bloqueado a propósito; el buscador propio del foro funciona.

## 11. Riesgos residuales y límites conocidos (honestos)

1. **IPs compartidas de Google (sin SNI todavía).** Todos los servicios de Google comparten IPs. Al permitir Gmail se autorizan IPs que también sirven `www.google.com`. El buscador sigue bloqueado para el uso normal (el nombre no resuelve), pero alguien técnico podría forzarlo (p. ej. Chrome con `--host-resolver-rules`). La solución es la **fase 2 (SNI)** de `ANALISIS_VIABILIDAD.md` — ahora más relevante.
2. **Contenido que le envían o comparten al usuario.** Gmail, Chat, Drive y Fotos muestran lo que otras personas envían (imágenes de correos vía proxy, adjuntos, archivos compartidos). Es inherente a permitir esos servicios; la tarjeta lo avisa.
3. **Documentos de Google: "Insertar → Imagen → Buscar en la web".** Si funciona, es una búsqueda de imágenes dentro de Docs. **Verificar** en la prueba; si funciona, identificar el host en "Bloqueados recientes"/DevTools y sumarlo al Bloqueo fijo.
4. **Gemini dentro de las apps**: cortado a nivel red (`appsgenaiserver-pa`). Si Google lo moviera a un endpoint del mismo host de la app (p. ej. dentro de `mail.google.com`), no se podría cortar por DNS: por eso la recomendación de apagar "Funciones inteligentes".
5. **Hosts genéricos de contenido con URL conocida**: `cdn.jsdelivr.net` (sirve archivos de GitHub/npm), `s3.eu-central-003.backblazeb2.com` (endpoint regional compartido de Backblaze), `lh*.googleusercontent.com` (imágenes de Google). Sin buscador no hay forma práctica de descubrir contenido ahí. El tutor puede quitar cualquiera con la ✕ (queda anotado y la actualización no lo vuelve a poner).
6. **`clients1.google.com/complete/search`** devuelve sugerencias de búsqueda en texto (sin resultados ni imágenes). **`www.googleapis.com/customsearch`** requiere una clave de API. Riesgo menor.
7. **Videos de Drive/Fotos**: si dependen de `googlevideo.com`/YouTube, no se reproducen (a propósito). Alternativa: descargarlos.
8. **"Esenciales del sistema" deja `github.com` y `githubusercontent.com` abiertos para todos** (los necesita el auto-update). GitHub tiene contenido de usuarios (imágenes y videos en issues). Mejora propuesta: que el servicio resuelva GitHub por su cuenta (ya tiene permiso WFP propio por AppId) y sacar GitHub de la lista blanca general.

## 12. Cómo mantener las listas

- Si algo no carga, el tutor lo ve en **Bloqueados recientes**; ahora cada dominio conocido muestra una **nota** ("YouTube: bloqueado a propósito", "Publicidad: no hace falta", "Bloqueo fijo…").
- Para corregir un paquete de fábrica: editar `Presets.cs` y publicar una versión. Al actualizarse, `PresetSync` actualiza los paquetes de cada equipo (respetando lo que el tutor quitó a mano y moviendo a "Personalizados" lo que agregó).

## Fuentes

- Google Workspace — [Set up a Google Workspace host name allowlist](https://knowledge.workspace.google.com/admin/getting-started/set-up-a-google-workspace-host-name-allowlist)
- Google Workspace — [Firewall and proxy settings](https://knowledge.workspace.google.com/admin/security/firewall-and-proxy-settings) (Drive y Sites, Gemini, Gmail)
- Google Workspace — [Gmail firewall settings](https://knowledge.workspace.google.com/admin/gmail/advanced/gmail-firewall-settings?hl=en)
- Google Workspace — [Drive and Sites firewall and proxy settings](https://knowledge.workspace.google.com/admin/drive/drive-and-sites-firewall-and-proxy-settings)
- Netify — [Gmail](https://www.netify.ai/resources/applications/gmail), [Google Drive](https://www.netify.ai/resources/applications/google-drive), [Google Photos](https://www.netify.ai/resources/applications/google-photos), [Google Chat](https://www.netify.ai/resources/applications/google-chat)
- nullgen — [How to block Gemini](https://nullgen.ai/block/gemini)
- Shelly Palmer — [Gmail's AI is On by Default](https://shellypalmer.com/2026/01/gmails-ai-is-on-by-default/) (cómo desactivar las funciones inteligentes)
- Captura propia en navegador (2026-10-21): mitmachim.top, www.prog.co.il, login de Google, Documento y Presentación públicos de Google.
