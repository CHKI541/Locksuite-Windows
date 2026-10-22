; Script de instalacion Inno Setup para LockSuite Windows
; Version 0.3.0 - Filtro por lista blanca para Windows 11
;
; Publicacion esperada (self-contained, sin dependencia de .NET en el equipo destino):
;   dotnet publish src\LockSuite.Service -c Release -r win-x64 --self-contained true -o installer\publish\service
;   dotnet publish src\LockSuite.UI      -c Release -r win-x64 --self-contained true -o installer\publish\ui
; Luego compilar este script con ISCC.

#define MyAppName "LockSuite Windows"
#define MyAppVersion "0.4.0"
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
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "ctxinstalluser"; Description: "Agregar al menu contextual la opcion ""Instalar en mi usuario (sin administrador)"" para archivos .exe"; GroupDescription: "Opciones avanzadas:"

[Files]
; Binarios del Servicio Windows (publicados self-contained)
Source: "publish\service\*"; DestDir: "{app}\service"; Flags: ignoreversion recursesubdirs createallsubdirs
; Binarios de la Interfaz Grafica (publicados self-contained)
Source: "publish\ui\*"; DestDir: "{app}\ui"; Flags: ignoreversion recursesubdirs createallsubdirs

[Dirs]
Name: "{commonappdata}\LockSuite"; Permissions: system-full admins-full
Name: "{commonappdata}\LockSuite\config"; Permissions: system-full admins-full
Name: "{commonappdata}\LockSuite\logs"; Permissions: system-full admins-full
Name: "{commonappdata}\LockSuite\updates"; Permissions: system-full admins-full

[Registry]
; Menu contextual (RunAsInvoker): permite al usuario estandar instalar en su carpeta personal
; los instaladores .exe que piden admin sin necesitarlo, sin el cartel de UAC. No otorga admin
; (hace lo contrario: fuerza ejecucion SIN elevacion). Se crea solo si el tutor marca la tarea.
Root: HKCR; Subkey: "exefile\shell\LockSuiteAsInvoker"; ValueType: string; ValueName: ""; ValueData: "Instalar en mi usuario (sin administrador)"; Flags: uninsdeletekey; Tasks: ctxinstalluser
Root: HKCR; Subkey: "exefile\shell\LockSuiteAsInvoker"; ValueType: string; ValueName: "Icon"; ValueData: "{app}\ui\{#MyAppExeName}"; Tasks: ctxinstalluser
Root: HKCR; Subkey: "exefile\shell\LockSuiteAsInvoker\command"; ValueType: string; ValueName: ""; ValueData: "cmd /c ""set __COMPAT_LAYER=RUNASINVOKER&& start """" ""%1"""""; Tasks: ctxinstalluser

[Icons]
Name: "{autoprograms}\LockSuite"; Filename: "{app}\ui\{#MyAppExeName}"
Name: "{autodesktop}\LockSuite"; Filename: "{app}\ui\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; La instalacion/arranque del servicio, el set de contrasena y el modo de prueba se hacen en
; [Code] (ssPostInstall) para controlar el orden. Aqui solo se abre la interfaz al terminar.
Filename: "{app}\ui\{#MyAppExeName}"; Description: "{cm:LaunchProgram,LockSuite}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Detiene, desinstala el servicio y limpia filtros WFP + NRPT antes de borrar archivos.
Filename: "{app}\service\{#MyServiceExeName}"; Parameters: "--stop"; RunOnceId: "StopLockSuiteService"; Flags: runhidden
Filename: "{app}\service\{#MyServiceExeName}"; Parameters: "--uninstall"; RunOnceId: "UninstallLockSuiteService"; Flags: runhidden
Filename: "{app}\service\{#MyServiceExeName}"; Parameters: "--reset-wfp"; RunOnceId: "ResetLockSuiteWfp"; Flags: runhidden

[UninstallDelete]
; "Borra todo": elimina la config (lista blanca, auth, trial) al desinstalar.
Type: filesandordirs; Name: "{commonappdata}\LockSuite"

[Code]
var
  PwPage: TInputQueryWizardPage;
  TrialOptPage: TInputOptionWizardPage;
  TrialHoursPage: TInputQueryWizardPage;

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

  TrialOptPage := CreateInputOptionPage(PwPage.ID,
    'Modo de prueba (opcional)',
    'Probar LockSuite por un tiempo antes de dejarlo permanente',
    'Si activa el modo de prueba, LockSuite funcionara durante el tiempo que elija y, al terminar, ' +
    'preguntara si desea conservarlo o quitarlo por completo. Si no activa nada, se instala de forma permanente.',
    False, False);
  TrialOptPage.Add('Activar modo de prueba');
  TrialOptPage.Add('Al terminar, quitar automaticamente sin preguntar (prueba descartable)');
  TrialOptPage.Add('Pedir la contrasena del tutor para poder quitarlo al terminar');
  TrialOptPage.Values[0] := False;
  TrialOptPage.Values[1] := False;
  TrialOptPage.Values[2] := False;

  TrialHoursPage := CreateInputQueryPage(TrialOptPage.ID,
    'Duracion de la prueba',
    'Solo aplica si activo el modo de prueba',
    'Ingrese la duracion de la prueba en horas (entre 1 y 96). Por defecto: 24.');
  TrialHoursPage.Add('Horas:', False);
  TrialHoursPage.Values[0] := '24';
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  h: Integer;
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
  end
  else if CurPageID = TrialHoursPage.ID then
  begin
    if TrialOptPage.Values[0] then
    begin
      h := StrToIntDef(TrialHoursPage.Values[0], -1);
      if (h < 1) or (h > 96) then
      begin
        MsgBox('La duracion de la prueba debe ser un numero entre 1 y 96 horas.', mbError, MB_OK);
        Result := False;
        Exit;
      end;
    end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  svc, tmp, action, extra: String;
  rc, hours, minutes: Integer;
begin
  if CurStep = ssPostInstall then
  begin
    svc := ExpandConstant('{app}\service\{#MyServiceExeName}');

    { 1) Fijar la contrasena maestra del tutor. Solo si se ingreso (en modo silencioso,
         p. ej. una auto-actualizacion, no hay pagina y Values[0] queda vacio: no se toca). }
    if PwPage.Values[0] <> '' then
    begin
      tmp := ExpandConstant('{tmp}\lsinit.txt');
      if SaveStringToFile(tmp, PwPage.Values[0], False) then
      begin
        Exec(svc, '--set-password-file "' + tmp + '"', '', SW_HIDE, ewWaitUntilTerminated, rc);
        DeleteFile(tmp);
      end
      else
        MsgBox('No se pudo escribir el archivo temporal de contrasena. Podra definirla luego desde la aplicacion.', mbError, MB_OK);
    end;

    { 2) Configurar el modo de prueba ANTES de iniciar el servicio, para que lo lea al arrancar.
         En modo silencioso Values[0]=False, por lo que una auto-actualizacion NO reinicia la prueba. }
    if TrialOptPage.Values[0] then
    begin
      hours := StrToIntDef(TrialHoursPage.Values[0], 24);
      if hours < 1 then hours := 1;
      if hours > 96 then hours := 96;
      minutes := hours * 60;
      if TrialOptPage.Values[1] then action := 'autoremove' else action := 'prompt';
      if TrialOptPage.Values[2] then extra := ' tutorlock' else extra := '';
      Exec(svc, '--set-trial ' + IntToStr(minutes) + ' ' + action + extra, '', SW_HIDE, ewWaitUntilTerminated, rc);
    end;

    { 3) Registrar e iniciar el servicio de filtrado (--install es idempotente en upgrades) }
    Exec(svc, '--install', '', SW_HIDE, ewWaitUntilTerminated, rc);
    Exec(svc, '--start', '', SW_HIDE, ewWaitUntilTerminated, rc);
  end;
end;

{ En una actualizacion, detener el servicio y cerrar la GUI ANTES de copiar archivos,
  para poder sobrescribir los binarios que estan en uso. En una instalacion limpia,
  estos comandos simplemente no encuentran nada que detener (inofensivo). }
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  rc: Integer;
begin
  Result := '';
  NeedsRestart := False;
  Exec('sc.exe', 'stop LockSuiteService', '', SW_HIDE, ewWaitUntilTerminated, rc);
  Sleep(2000);
  Exec('taskkill.exe', '/F /IM LockSuite.UI.exe', '', SW_HIDE, ewWaitUntilTerminated, rc);
  Sleep(500);
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
