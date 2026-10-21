# Handoff para Antigravity — LockSuite Windows (Sesión 4 completada)

Pegá este archivo (o pedile a Antigravity que lo lea) cuando abras una conversación en Antigravity para ejecutar tareas que requieren terminal real en Windows.

---

## Estado actual
- **Versión 0.2 implementada:**
  - `LockSuite.Core`: DomainMatcher, SniParser, WhitelistConfig, Presets, IPC client.
  - `LockSuite.Service`: WFP ALE Outbound default-deny + reglas dinámicas temporales con auto-rollback; resolutor DNS local 127.0.0.1:53 con NXDOMAIN sintético; DPAPI + PBKDF2 (SHA256) para clave del tutor; IPC Named Pipe.
  - `LockSuite.UI`: Interfaz WPF moderna con Estado, Lista Blanca, Modo Aprendizaje ("Bloqueados recientes" con 1 clic para permitir) y diálogo de tutor.
  - `LockSuiteSetup.iss`: Script de instalación Inno Setup listo.
- **Tests automatizados:** 49/49 pasando (26 de Core + 23 de Service/IPC).
- **Git:** Rama `main` en commit `c0feea5`.

---

## Tarea para esta sesión en Antigravity

1. **Compilar y verificar salud de la solución:**
   ```powershell
   & "$env:USERPROFILE\.dotnet\dotnet.exe" test LockSuite.sln
   ```
   (Deben pasar las 49 pruebas).

2. **Probar empaquetado del instalador con Inno Setup:**
   - Si `iscc` no está instalado en la máquina:
     ```powershell
     winget install JRSoftware.InnoSetup --accept-source-agreements --accept-package-agreements
     ```
   - Compilar el instalador:
     ```powershell
     & "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\LockSuiteSetup.iss
     ```
   - Verificar que genera `bin\installer\LockSuiteSetup-0.1.0.exe`.

3. **Prueba en máquina virtual (VM) limpia de Windows 11:**
   - Instalar `LockSuiteSetup-0.1.0.exe` en una VM.
   - Verificar:
     - El servicio `LockSuiteService` arranca como SYSTEM.
     - Navegación hacia `google.com` o `mercadopago.com.ar` funciona correctamente.
     - Sitios fuera de la lista blanca (ej. `facebook.com`, `instagram.com`) no cargan.
     - La GUI muestra el intento en "Bloqueados recientes" y permite autorizarlo con la contraseña maestra.

4. **Al terminar:**
   - Actualizar `ESTADO.md` con los resultados de la prueba en VM y empaquetado.
   - Hacer commit en git y dejar el "Próximo paso" escrito.
