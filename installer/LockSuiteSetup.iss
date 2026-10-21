; Script de instalación Inno Setup para LockSuite Windows
; Versión 0.1.0 — Filtro por lista blanca para Windows 11

#define MyAppName "LockSuite Windows"
#define MyAppVersion "0.1.0"
#define MyAppPublisher "LockSuite Community"
#define MyAppURL "https://github.com/CHKI541/Locksuite-Windows"
#define MyAppExeName "LockSuite.UI.exe"
#define MyServiceExeName "LockSuite.Service.exe"

[Setup]
AppId={{C6044738-B605-4DE8-BC21-E8FE6892540B}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\LockSuite
DefaultGroupName=LockSuite
DisableProgramGroupPage=yes
PrivilegesRequired=admin
OutputDir=..\bin\installer
OutputBaseFilename=LockSuiteSetup-{#MyAppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "autostart"; Description: "Iniciar LockSuite automáticamente al encender el equipo"; GroupDescription: "Inicio:"

[Files]
; Binarios del Servicio Windows (Publicados)
Source: "..\src\LockSuite.Service\bin\Release\net8.0-windows\publish\*"; DestDir: "{app}\service"; Flags: ignoreversion recursesubdirs createallsubdirs
; Binarios de la Interfaz Gráfica (Publicados)
Source: "..\src\LockSuite.UI\bin\Release\net8.0-windows\publish\*"; DestDir: "{app}\ui"; Flags: ignoreversion recursesubdirs createallsubdirs

[Dirs]
Name: "{commonappdata}\LockSuite\config"; Permissions: system-full administrators-full users-read
Name: "{commonappdata}\LockSuite\logs"; Permissions: system-full administrators-full users-read

[Icons]
Name: "{autoprograms}\LockSuite"; Filename: "{app}\ui\{#MyAppExeName}"
Name: "{autodesktop}\LockSuite"; Filename: "{app}\ui\{#MyAppExeName}"; Tasks: desktopicon
Name: "{userstartup}\LockSuite"; Filename: "{app}\ui\{#MyAppExeName}"; Tasks: autostart

[Run]
; Instala e inicia el Windows Service
Filename: "{app}\service\{#MyServiceExeName}"; Parameters: "--install"; StatusMsg: "Registrando e instalando servicio de filtrado..."; Flags: runhidden
Filename: "{app}\service\{#MyServiceExeName}"; Parameters: "--start"; StatusMsg: "Iniciando servicio de filtrado de red..."; Flags: runhidden
; Abre la interfaz gráfica tras la instalación
Filename: "{app}\ui\{#MyAppExeName}"; Description: "{cm:LaunchProgram,LockSuite}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Detiene y desinstala el Windows Service antes de borrar archivos
Filename: "{app}\service\{#MyServiceExeName}"; Parameters: "--stop"; RunOnceId: "StopLockSuiteService"; Flags: runhidden
Filename: "{app}\service\{#MyServiceExeName}"; Parameters: "--uninstall"; RunOnceId: "UninstallLockSuiteService"; Flags: runhidden

[Code]
// Verificación previa de Windows 10/11
function InitializeSetup(): Boolean;
var
  Version: TWindowsVersion;
begin
  GetWindowsVersionEx(Version);
  if Version.Major < 10 then
  begin
    MsgBox('LockSuite requiere Windows 10 o Windows 11 para operar con Windows Filtering Platform.', mbCriticalError, MB_OK);
    Result := False;
    Exit;
  end;
  Result := True;
end;
