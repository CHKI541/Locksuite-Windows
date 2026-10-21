; Script de instalacion Inno Setup para LockSuite Windows
; Version 0.2.0 - Filtro por lista blanca para Windows 11
;
; Publicacion esperada (self-contained, sin dependencia de .NET en el equipo destino):
;   dotnet publish src\LockSuite.Service -c Release -r win-x64 --self-contained true -o installer\publish\service
;   dotnet publish src\LockSuite.UI      -c Release -r win-x64 --self-contained true -o installer\publish\ui
; Luego compilar este script con ISCC.

#define MyAppName "LockSuite Windows"
#define MyAppVersion "0.2.0"
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

[Files]
; Binarios del Servicio Windows (publicados self-contained)
Source: "publish\service\*"; DestDir: "{app}\service"; Flags: ignoreversion recursesubdirs createallsubdirs
; Binarios de la Interfaz Grafica (publicados self-contained)
Source: "publish\ui\*"; DestDir: "{app}\ui"; Flags: ignoreversion recursesubdirs createallsubdirs

[Dirs]
Name: "{commonappdata}\LockSuite\config"; Permissions: system-full admins-full users-readexec
Name: "{commonappdata}\LockSuite\logs"; Permissions: system-full admins-full users-readexec

[Icons]
Name: "{autoprograms}\LockSuite"; Filename: "{app}\ui\{#MyAppExeName}"
Name: "{autodesktop}\LockSuite"; Filename: "{app}\ui\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; La instalacion/arranque del servicio y el set de contrasena se hacen en [Code] (ssPostInstall)
; para controlar el orden. Aqui solo se abre la interfaz al terminar.
Filename: "{app}\ui\{#MyAppExeName}"; Description: "{cm:LaunchProgram,LockSuite}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Detiene, desinstala el servicio y limpia filtros WFP + NRPT antes de borrar archivos.
Filename: "{app}\service\{#MyServiceExeName}"; Parameters: "--stop"; RunOnceId: "StopLockSuiteService"; Flags: runhidden
Filename: "{app}\service\{#MyServiceExeName}"; Parameters: "--uninstall"; RunOnceId: "UninstallLockSuiteService"; Flags: runhidden
Filename: "{app}\service\{#MyServiceExeName}"; Parameters: "--reset-wfp"; RunOnceId: "ResetLockSuiteWfp"; Flags: runhidden

[Code]
var
  PwPage: TInputQueryWizardPage;

procedure InitializeWizard;
begin
  PwPage := CreateInputQueryPage(wpSelectTasks,
    'Contrasena del tutor',
    'Defina la contrasena maestra de LockSuite',
    'Esta contrasena la usara el tutor/administrador para modificar la lista blanca de dominios.' + #13#10 +
    'El usuario estandar del equipo NO debe conocerla.' + #13#10#13#10 +
    'IMPORTANTE: guardela en un lugar seguro. Sin ella no se podran cambiar los filtros ni permitir nuevos sitios.');
  PwPage.Add('Contrasena maestra:', True);
  PwPage.Add('Repetir contrasena:', True);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if CurPageID = PwPage.ID then
  begin
    if PwPage.Values[0] = '' then
    begin
      MsgBox('Ingrese una contrasena maestra.', mbError, MB_OK);
      Result := False;
      Exit;
    end;
    if Length(PwPage.Values[0]) < 6 then
    begin
      MsgBox('La contrasena debe tener al menos 6 caracteres.', mbError, MB_OK);
      Result := False;
      Exit;
    end;
    if PwPage.Values[0] <> PwPage.Values[1] then
    begin
      MsgBox('Las contrasenas no coinciden.', mbError, MB_OK);
      Result := False;
      Exit;
    end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  svc, tmp: String;
  rc: Integer;
begin
  if CurStep = ssPostInstall then
  begin
    svc := ExpandConstant('{app}\service\{#MyServiceExeName}');
    tmp := ExpandConstant('{tmp}\lsinit.txt');

    { 1) Fijar la contrasena maestra del tutor ANTES de iniciar el servicio }
    if SaveStringToFile(tmp, PwPage.Values[0], False) then
    begin
      Exec(svc, '--set-password-file "' + tmp + '"', '', SW_HIDE, ewWaitUntilTerminated, rc);
      DeleteFile(tmp);
    end
    else
      MsgBox('No se pudo escribir el archivo temporal de contrasena. Podra definirla luego desde la aplicacion.', mbError, MB_OK);

    { 2) Registrar e iniciar el servicio de filtrado }
    Exec(svc, '--install', '', SW_HIDE, ewWaitUntilTerminated, rc);
    Exec(svc, '--start', '', SW_HIDE, ewWaitUntilTerminated, rc);
  end;
end;

{ Verificacion previa de Windows 10/11 }
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
