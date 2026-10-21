# Prompt maestro — pegar al principio de cada conversación nueva

Estamos construyendo LockSuite Windows, un programa que bloquea todo el internet de una computadora Windows salvo una lista blanca de dominios. Todo el proyecto vive en la carpeta `C:\Locksuite windows` de mi compu, conectada a esta conversación.

Antes de hacer nada:
1. Leé `ESTADO.md` completo (es el estado real del proyecto: decisiones tomadas, qué está afuera del alcance a propósito, preguntas abiertas, qué sigue).
2. Leé también `docs/ANALISIS_VIABILIDAD.md` si vas a tocar la arquitectura, y cualquier otro archivo de `docs/` que sea relevante para lo que te voy a pedir.
3. Contame en 2-3 líneas qué entendiste del estado actual, para confirmar que lo leíste bien, antes de arrancar.

Después de eso, esto es lo que necesito en esta sesión:

> [ACÁ ESCRIBO YO LO QUE QUIERO QUE HAGAS HOY]

Reglas para toda la sesión:
- Respetá las decisiones ya tomadas en `ESTADO.md` (en particular, la sección "Explícitamente afuera del alcance"). Si te parece que alguna ya no tiene sentido, decímelo y lo discutimos, pero no la cambies por tu cuenta.
- Podés escribir y editar cualquier archivo de la carpeta del proyecto. No tenés una terminal de Windows real: no podés compilar, ejecutar el .exe, ni probar la interfaz con clics. Si algo necesita eso, no lo intentes simular: escribilo como tarea pendiente.
- Cuando algo quede pendiente porque necesita Windows de verdad (compilar, probar, firmar, empaquetar, debuggear en vivo), agregalo a `ESTADO.md`, sección "Para Antigravity", con instrucciones concretas y accionables (qué comando correr, qué archivo mirar, qué se espera que pase).
- Al terminar la sesión, actualizá `ESTADO.md`: qué se hizo, qué decisiones nuevas se tomaron, un renglón nuevo en el log de sesiones (fecha + resumen), y las preguntas abiertas que queden. Si se llega a una versión nueva y funcional, proponeme un mensaje de commit.
