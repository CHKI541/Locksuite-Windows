# LockSuite Windows

Programa para Windows 11 que bloquea todo el acceso a internet de una computadora salvo una lista blanca de dominios definida por un administrador. Pensado para instalarse en varias máquinas.

## Documentos del proyecto

- [`ESTADO.md`](./ESTADO.md) — qué está hecho, qué falta y qué preguntas están abiertas. **Leer siempre primero.**
- [`PROMPT_MAESTRO.md`](./PROMPT_MAESTRO.md) — texto para pegar al principio de cada conversación nueva con Claude.
- [`docs/ANALISIS_VIABILIDAD.md`](./docs/ANALISIS_VIABILIDAD.md) — arquitectura elegida, por qué, y qué queda explícitamente afuera.
- [`docs/ANALISIS_TECHLOQ.md`](./docs/ANALISIS_TECHLOQ.md) — qué tomamos y qué no de analizar Techloq.

## Idea general

- Una cuenta de **administrador** (la tenés vos) y una cuenta **estándar** (la usa la persona).
- El firewall de Windows bloquea TODO el tráfico saliente por defecto.
- Un resolutor DNS local solo responde por los dominios de la lista blanca; al resolver, habilita temporalmente esa IP en el firewall.
- Una interfaz de escritorio (WPF) protegida con tu contraseña para administrar la lista.
- Todo corre como servicio de Windows (SYSTEM), se instala con un instalador (Inno Setup) y arranca con Windows.

## Qué NO incluye, a propósito

No hay driver de kernel, no hay inspección de tráfico HTTPS (sin certificado raíz propio), y no se intenta bloquear al administrador de la máquina. El porqué está en `docs/ANALISIS_VIABILIDAD.md`.

## Flujo de trabajo

Cada conversación nueva con Claude empieza pegando `PROMPT_MAESTRO.md`. Al terminar esa conversación, `ESTADO.md` queda actualizado para que la siguiente arranque donde quedó esta.
