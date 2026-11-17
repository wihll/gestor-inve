; Instalador de gestor-inve para Windows (Inno Setup 6).
;
; Lleva dentro .NET (publicación self-contained) y PostgreSQL: el equipo no necesita tener nada
; instalado. La configuración real —clúster, rol, secretos, servicios— la hace instalar.ps1.

#ifndef Version
  #define Version "0.1.0"
#endif
#ifndef Origen
  #define Origen "..\..\dist\windows"
#endif

[Setup]
AppId={{6F0B8E0A-5C1D-4C6E-9B7A-3E2B1D4F7A11}
AppName=gestor-inve
AppVersion={#Version}
AppPublisher=gestor-inve
AppPublisherURL=https://github.com/wihll/gestor-inve
DefaultDirName={autopf}\GestorInve
DefaultGroupName=gestor-inve
DisableProgramGroupPage=yes
LicenseFile=..\..\LICENSE
OutputDir=..\..\dist
OutputBaseFilename=gestor-inve-{#Version}-setup
SetupIconFile=..\comun\gestor-inve.ico
UninstallDisplayIcon={app}\gestor-inve.ico
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
MinVersion=10.0
WizardStyle=modern
; Los servicios bloquean sus archivos: se paran antes de copiar (ver [Code]).
CloseApplications=no

[Languages]
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"

[Files]
Source: "{#Origen}\app\*"; DestDir: "{app}\app"; Flags: recursesubdirs ignoreversion
Source: "{#Origen}\pgsql\*"; DestDir: "{app}\pgsql"; Flags: recursesubdirs ignoreversion
Source: "instalar.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "desinstalar.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "respaldar.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\comun\gestor-inve.ico"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autodesktop}\gestor-inve"; Filename: "{app}\app\GestorInve.exe"; Parameters: "http://127.0.0.1:48620"; IconFilename: "{app}\gestor-inve.ico"
Name: "{group}\gestor-inve"; Filename: "{app}\app\GestorInve.exe"; Parameters: "http://127.0.0.1:48620"; IconFilename: "{app}\gestor-inve.ico"
Name: "{group}\Copia de seguridad"; Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\respaldar.ps1"" -Raiz ""{app}"""; IconFilename: "{app}\gestor-inve.ico"

[Run]
Filename: "{app}\app\GestorInve.exe"; Parameters: "http://127.0.0.1:48620"; Description: "Abrir gestor-inve"; Flags: postinstall nowait skipifsilent runasoriginaluser

[UninstallRun]
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\desinstalar.ps1"" -Raiz ""{app}"""; Flags: runhidden waituntilterminated; RunOnceId: "QuitarServicios"

[Code]
procedure PararServicios;
var
  Codigo: Integer;
begin
  { En una actualización los servicios tienen abiertos sus ejecutables. Si no existen, `sc` falla y no importa. }
  Exec(ExpandConstant('{sys}\sc.exe'), 'stop GestorInve', '', SW_HIDE, ewWaitUntilTerminated, Codigo);
  Exec(ExpandConstant('{sys}\sc.exe'), 'stop GestorInvePostgres', '', SW_HIDE, ewWaitUntilTerminated, Codigo);
  Sleep(4000);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  PararServicios;
  Result := '';
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Codigo: Integer;
  Parametros: String;
begin
  if CurStep = ssPostInstall then
  begin
    Parametros := '-NoProfile -ExecutionPolicy Bypass -File "' + ExpandConstant('{app}\instalar.ps1') + '" -Raiz "' + ExpandConstant('{app}') + '"';
    if (not Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'), Parametros, '', SW_HIDE, ewWaitUntilTerminated, Codigo)) or (Codigo <> 0) then
    begin
      { Un instalador que dice «listo» con la base sin crear es peor que uno que avisa. }
      MsgBox('La configuración de gestor-inve no terminó bien.' + #13#10 + #13#10 +
             'El detalle está en C:\ProgramData\GestorInve\instalacion.log', mbError, MB_OK);
      { En modo silencioso (el CI) el fallo tiene que verse en el código de salida. }
      if WizardSilent then
        Abort;
    end;
  end;
end;
