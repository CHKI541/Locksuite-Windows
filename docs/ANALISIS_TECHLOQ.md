# Qué sirve (y qué no) del análisis de Techloq

Resumen de lo que mandó Antigravity analizando Techloq instalado, y qué se toma para LockSuite.

## Confirma lo que ya estaba planteado

- **No hace falta un driver de kernel propio.** El mismo análisis lo dice: Techloq invirtió en certificar `tpd.sys` (firma EV + WHQL), pero para una lista blanca estricta no es necesario — se logra lo mismo con herramientas nativas (WFP). Coincide con `docs/ANALISIS_VIABILIDAD.md`.
- **Firewall en modo "bloquear todo, permitir por excepción" (fail-closed).** Exactamente el diseño ya elegido.
- **Servicio como SYSTEM + separación admin/estándar + instalador que automatiza la configuración en un paso.** Mismo enfoque.

## Dos cosas que Techloq hace y que no se van a copiar

**1. Inspección SSL con un certificado raíz propio** (el `tps.exe` como proxy que redirige todo el puerto 443 e inspecciona el tráfico). Esto le permite a Techloq no solo decidir a qué dominio se conecta un proceso, sino leer el contenido de cualquier conexión HTTPS de la máquina. Para una lista blanca de dominios no hace falta llegar a ese punto: alcanza con mirar el SNI (el nombre del sitio, que va sin cifrar en el saludo inicial de TLS) — ver `docs/ANALISIS_VIABILIDAD.md`, sección 2.4. Se evita meter una autoridad certificadora propia en el almacén de Windows, que es un componente de mucho riesgo si se compromete.

**2. Protección a nivel de kernel para que ni un administrador pueda frenar el servicio** (callbacks de kernel, DACL reforzada contra `sc stop` incluso con admin). Tiene sentido para Techloq porque es un producto genérico que no puede asumir que el cliente configuró bien la separación de cuentas. Acá sí se va a exigir esa separación como parte de la instalación, así que no es necesario blindarse contra el propio admin — y es, además, la parte de la arquitectura de Techloq que más se parece a técnicas de malware/spyware (un proceso que ni el dueño de la máquina puede controlar). No se va a construir.

## En una frase

Todo lo que hace viable y prolijo el enfoque de Techloq (WFP, fail-closed, servicio SYSTEM, instalador automatizado) ya estaba en el plan. Lo que agrega por encima (inspección SSL y blindaje anti-admin) es más invasivo de lo que este proyecto necesita, así que queda afuera.
