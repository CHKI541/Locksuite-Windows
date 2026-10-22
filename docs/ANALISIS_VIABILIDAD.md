# Análisis de viabilidad y arquitectura — LockSuite Windows

## 1. Es viable

Bloquear todo el tráfico de una computadora Windows 11 y permitir solo una lista blanca de dominios, de forma que una persona con cuenta estándar (sin ser administradora) no pueda evadirlo con ningún programa, es viable con herramientas nativas de Windows, sin drivers de kernel propios y sin ralentizar la máquina.

Lo que no es viable, en ningún software, para ningún fabricante: que sea imposible de evadir para alguien que tenga la contraseña de administrador, acceso físico a la máquina (arrancar desde USB, resetear BIOS, cambiar el disco) o un segundo dispositivo con su propia conexión (el celular con datos). Ese límite no es de este proyecto: Techloq, Qustodio, Covenant Eyes, Microsoft Family Safety, todos tienen el mismo límite y lo resuelven igual — separación de cuentas + control del acceso físico, no magia de software.

## 2. Componentes

### 2.1 Servicio de Windows (`LockSuiteService`, corre como SYSTEM)
Es el único que manda. Expone un canal de IPC local (named pipe) para que la interfaz le hable. Guarda la configuración (lista blanca, hash de la contraseña) cifrada en disco con DPAPI, en una carpeta con ACL restringida a SYSTEM.

Responsabilidades: instalar y mantener los filtros de WFP, correr el resolutor DNS local, (fase 2) correr el inspector de SNI, validar la contraseña maestra (hash + sal, límite de intentos, backoff), exponer el estado y aceptar cambios de configuración solo autenticados.

### 2.2 Filtro de red — Windows Filtering Platform (WFP)
- Capa `FWPM_LAYER_ALE_AUTH_CONNECT_V4/V6`, peso alto, acción por defecto **bloquear todo saliente**.
- Excepciones persistentes mínimas y fijas: loopback, DHCP, y la resolución DNS hacia el resolutor local.
- Excepciones dinámicas de corta duración para las IPs que el resolutor acaba de autorizar (ver 2.3).
- *Fail-closed*: si el servicio se cae o los filtros no se pueden cargar, el estado por defecto de WFP ya es "bloqueado".
- No hace falta un driver `.sys` propio: WFP ya opera en el kernel vía la API de usuario (`fwpuclnt.dll`); no hay que firmar nada con WHQL.

### 2.3 Resolutor DNS local
- Escucha en 127.0.0.1:53 y se configura como DNS de la interfaz de red (vía DHCP/NRPT), para que no se pueda "cambiar el DNS" desde la configuración de Windows sin permisos de admin.
- Por cada consulta: si el dominio (o su dominio padre, para comodines `*.ejemplo.com`) está en la lista blanca → resuelve de verdad (contra 1.1.1.1 u 8.8.8.8) y, con la IP de la respuesta, le pide al servicio que abra una regla de WFP temporal (TTL = al TTL de la respuesta DNS, con un piso de unos minutos) solo para esa IP y ese puerto 443/80.
- Si no está en la lista → NXDOMAIN.
- "Modo aprendizaje": cada dominio NXDOMAIN se loguea con timestamp; la interfaz lo muestra para aprobar con un clic. Esto es lo que hace usable el día a día — una página moderna pide recursos de 10-20 dominios distintos (CDN, fuentes, analytics) y sin esto la lista blanca es insostenible.

### 2.4 (Fase 2, recomendado) Inspección de SNI
El punto débil real de filtrar solo por IP es que muchos sitios comparten IP detrás de un mismo CDN (Cloudflare, etc.): alguien técnico podría, en teoría, pedir otro dominio contra una IP ya habilitada. Para tapar esto sin descifrar nada: el nombre del sitio viaja en texto plano dentro del "Client Hello" de TLS (campo SNI), antes de que se establezca el cifrado. Un filtro a nivel de WFP puede leer ese campo y cortar la conexión si el SNI no está en la lista blanca — sin proxy, sin CA propia, sin romper nada. No es trivial, pero es mucho más simple y menos riesgoso que una inspección TLS completa.

### 2.5 Interfaz (WPF, .NET 8)
- Login con la contraseña maestra (la valida el servicio, no la interfaz).
- Lista blanca: alta/baja de dominios, con comodines, agrupados en "paquetes" (ej. "Google", "Mercado Pago").
- Pestaña "Bloqueados recientes" (modo aprendizaje, ver 2.3).
- Plantillas predefinidas para instalar rápido y tener algo usable.
- Bandeja del sistema: visible, con ícono — este programa no se esconde (ver sección 5).

### 2.6 Instalador (Inno Setup)
En un asistente: pide la contraseña maestra; si la cuenta desde la que se instala es la única administradora, ofrece crear o convertir la cuenta de la persona que va a usar la máquina en cuenta estándar; instala el servicio, lo configura para arrancar con Windows, aplica los filtros de WFP; deja accesos para abrir la interfaz.

## 3. Presets de ejemplo (lista blanca de referencia)

- **Google (Gmail, Chat, Drive, Fotos):** en la práctica es un paquete de docenas de subdominios (`accounts.google.com`, `mail.google.com`, `chat.google.com`, `drive.google.com`, `photos.google.com`, `*.googleusercontent.com`, `*.gstatic.com`, `apis.google.com`, `*.googleapis.com`, etc.), no un solo dominio. Conviene como "paquete" armado y ajustado con el modo aprendizaje, no tipeado a mano.
  - **Actualización sesión 9:** se implementó como **un paquete por servicio** (botones "Permitir Gmail", "Permitir Google Chat", "Permitir Google Drive", "Permitir Google Fotos") con hosts puntuales — nunca `google.com`, `googleapis.com`, `gstatic.com` ni `googleusercontent.com` enteros, porque abren el buscador, Gemini, almacenamiento público, etc. — más un paquete de **bloqueo con prioridad** ("Bloqueo fijo: buscador de Google y Gemini"). Detalle host por host en `docs/ANALISIS_DOMINIOS_SERVICIOS.md`.
- `mitmachim.top`, `prog.co.il`, `mercadopago.com.ar` — dominios puntuales, van directo a la lista. (Sesión 9: `prog.co.il` y `mitmachim.top` tienen su propio paquete/botón con los recursos de terceros que necesitan para verse bien: Cloudflare, jQuery, CDNs, fuentes hebreas.)
- Conviene sumar de entrada, como "esenciales del sistema" (si no, aparecen errores raros o el cartel de "sin internet" de Windows): servidores de hora (`time.windows.com`), verificación de certificados (OCSP/CRL de los CAs) y el endpoint de verificación de conectividad de Windows.

### 3.1 Sintaxis de patrones y prioridad de bloqueo (sesión 9)

- `ejemplo.com` → el sitio y todos sus subdominios; `*.ejemplo.com` → solo subdominios.
- `=ejemplo.com` → solo ese host exacto.
- `lh*.ejemplo.com`, `*-docs.ejemplo.com` → comodín dentro de **una** etiqueta (nunca cruza un punto). Hace falta para familias de hosts como `lh3`/`lh7-rt`, `ci3`…`ci6`, `doc-0s-1a-docs`.
- Un paquete puede ser de **bloqueo** (`blocks: true`): mientras esté activo, sus hosts quedan bloqueados aunque otro paquete los permita. Permitido = (coincide con la lista blanca) **y no** (coincide con un bloqueo activo).
- Con los servicios de Google habilitados, el agujero de IP compartida de la sección 2.4 se vuelve más relevante (las IPs de Gmail también sirven el buscador): la fase 2 (SNI) pasa a ser la mejora de seguridad más valiosa.

## 4. Rendimiento

El filtrado ocurre dentro de la pila de red (WFP), no por inspección paquete a paquete con un proceso intermedio pesado, y el DNS local es un resolutor chico con caché. No debería notarse al navegar. `simplewall` (open source, usa el mismo WFP) es la prueba de que esto es liviano.

## 5. Explícitamente afuera del alcance

(Resumen corto en `ESTADO.md`, sección 3. Acá el detalle de por qué.)

**Driver de kernel propio:** no agrega nada que WFP no haga ya para este caso de uso, y cuesta firma EV + certificación WHQL (cientos de dólares al año, proceso de aprobación de Microsoft).

**Inspección TLS con CA propia (lo que hace Techloq):** permite leer el contenido de cualquier conexión HTTPS de la máquina, no solo a qué dominio se conecta. Es un salto grande de invasividad y de riesgo: esa clave privada, si se filtra o se extrae de la máquina, permite interceptar cualquier tráfico cifrado de quien la tenga instalada. El SNI (sección 2.4) consigue el mismo filtrado por dominio sin ese riesgo. No se va a construir.

**Blindaje contra el propio administrador de Windows** (lo que Techloq hace con su driver: ni siquiera un admin puede parar el servicio): tiene sentido si no se puede garantizar que la persona restringida jamás tenga una cuenta de admin. En este diseño, la separación de cuentas es un requisito, no un detalle — si se cumple, no hace falta defenderse del admin, porque el admin es la parte de confianza. Construir algo que resiste a su propio administrador (proceso protegido a nivel de kernel, invisible en el Administrador de Tareas, DACL que rechaza `sc stop` con admin) es, visto desde afuera (un antivirus, alguien auditando el binario, Microsoft SmartScreen), indistinguible de un programa espía. No es una línea que se vaya a cruzar en este proyecto, más allá de lo legítimo que sea el uso acá.

**Ocultar el proceso o el programa de la lista de instalados:** el programa es visible (ícono, carpeta de instalación, entrada en "Aplicaciones"). Lo único oculto es la configuración (lista blanca, contraseña), no la existencia del programa.

## 6. Límites que ningún software resuelve

- Alguien con la contraseña de administrador puede desinstalar cualquier cosa. Mitigación: que la persona restringida nunca tenga esa cuenta ni la contraseña.
- Acceso físico: arrancar desde un pendrive con otro sistema, resetear la contraseña de administrador desde un USB de recuperación, sacar el disco. Mitigación parcial: contraseña de BIOS/UEFI, deshabilitar el arranque por USB, cifrado de disco (BitLocker) con la clave de recuperación guardada por el administrador, no por la persona restringida.
- Otros dispositivos: un celular con datos móviles no pasa por este control. Es inherente — ningún software que corra en la notebook puede controlar otro dispositivo.
