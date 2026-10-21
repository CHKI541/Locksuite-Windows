# Prompt Maestro — Para iniciar cualquier conversación nueva (Claude o Antigravity)

Copiá y pegá el bloque siguiente como primer mensaje en cada nueva conversación:

```markdown
Estamos desarrollando LockSuite Windows (filtro de internet por lista blanca local para computadoras "Kosher" en Windows 11). Todo el repositorio está en la carpeta de este proyecto.

Antes de hacer nada:
1. Leé completo `ESTADO.md` (es la única fuente de la verdad: decisiones tomadas, qué está hecho, qué está afuera del alcance a propósito, y el "Próximo paso").
2. Si vas a modificar la arquitectura, leé `docs/ANALISIS_VIABILIDAD.md`.
3. Confirmame en 2-3 líneas qué entendiste del estado actual y qué componentes están activos antes de empezar.

---

### Lo que necesito hacer en esta sesión:
> [ESCRIBÍ ACÁ TU PEDIDO PUNTUAL]

---

### Reglas para toda la sesión:
1. **Respetá las decisiones de alcance:** No inventar drivers de kernel propios (.sys), no hacer inspección MITM/SSL con certificados falsos, no hacer blindaje contra el administrador. La seguridad viene de la cuenta estándar vs admin.
2. **Si sos Claude (sin terminal de Windows):**
   - Modificá el código, arquitectura o documentación que corresponda.
   - Todo lo que requiera compilar, correr tests en vivo, probar el servicio/WFP o empaquetar, dejalo documentado y listo en `HANDOFF_ANTIGRAVITY.md` con los comandos exactos para que Antigravity lo ejecute luego.
3. **Si sos Antigravity (con terminal de Windows):**
   - Podés compilar con `dotnet build` / `dotnet test`, probar servicios, verificar y hacer commits en git.
   - Si modificás código o la versión funciona y pasan los tests, hacé commit con un mensaje descriptivo.
4. **Regla de cierre obligatoria (ambos):**
   - Al terminar la tarea, actualizá `ESTADO.md`:
     - Qué se construyó y qué se probó.
     - Un renglón nuevo en la sección "Log de sesiones" con fecha y resumen.
     - Dejá escrita la sección final "**Próximo paso**" con la indicación exacta de lo que sigue para la próxima conversación.
```
