; Instalador de VortexPad (Inno Setup 6)
;
; Compilar (después de "dotnet publish VortexPad.App -c Release -r win-x64 -o publish"):
;   iscc /DAppVersion=0.1.0 /DPublishDir=..\publish installer\VortexPad.iss
;
; El instalador:
; - Instala el exe self-contained (no necesita .NET) en Archivos de programa, o en la carpeta del
;   usuario si se elige "solo para mí" (sin administrador).
; - Crea accesos directos en el menú Inicio (y opcionalmente en el escritorio).
; - Si falta ViGEmBus, ofrece abrir su página de descarga al terminar (el driver no se redistribuye aquí).
; - Al desinstalar no borra perfiles, ajustes ni logs de %LOCALAPPDATA%\VortexPad salvo que se pida.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\publish"
#endif

#define AppName "VortexPad"
#define AppExe "VortexPad.App.exe"
#define AppPublisher "VortexPad"
#define AppUrl "https://github.com/TomasVargas22/VortexPad"

[Setup]
; AppId fijo: identifica la instalación para actualizar y desinstalar. NO cambiarlo entre versiones.
AppId={{6F1D2B7A-3C4E-4E1B-9A57-5B0E8C2D4F61}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
; Por defecto pide administrador (Archivos de programa), pero deja elegir "solo para mí" sin UAC
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir=..\dist
OutputBaseFilename=VortexPad-{#AppVersion}-setup-win-x64
SetupIconFile=..\VortexPad.App\Assets\vortexpad.ico
UninstallDisplayIcon={app}\{#AppExe}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\Desinstalar {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "https://github.com/nefarius/ViGEmBus/releases/latest"; Description: "Descargar ViGEmBus (necesario para el mando virtual Xbox / DualShock 4)"; Flags: postinstall shellexec skipifsilent unchecked; Check: not IsViGEmBusInstalled
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Quita la entrada de "Iniciar con Windows" si el usuario la había activado
Filename: "{cmd}"; Parameters: "/C reg delete ""HKCU\Software\Microsoft\Windows\CurrentVersion\Run"" /v VortexPad /f"; Flags: runhidden; RunOnceId: "RemoveRunEntry"

[Code]
function IsViGEmBusInstalled(): Boolean;
begin
  Result := RegKeyExists(HKLM, 'SYSTEM\CurrentControlSet\Services\ViGEmBus');
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    DataDir := ExpandConstant('{localappdata}\VortexPad');
    if DirExists(DataDir) then
      if MsgBox('¿Borrar también tus perfiles, ajustes y logs de VortexPad?' #13#10 + DataDir,
                mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
        DelTree(DataDir, True, True, True);
  end;
end;
