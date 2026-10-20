; Instalador de Audio Pitcher (Inno Setup 6). Se arma con installer\build.ps1.
; vbcable\ = contenido de VBCABLE_Driver_Pack45.zip (vb-audio.com/Cable). Redistribución permitida
; por VB-Audio bajo su modelo donationware, identificándolo como producto de VB-Audio.

#define AppName      "Audio Pitcher"
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#define AppExe       "AudioPitcher.exe"
#define AppUrl       "https://github.com/Bombaxz/audio-pitcher"
#define PublishDir   "..\AudioPitcher\bin\Publish\win-x64"

[Setup]
AppId={{77F47897-E454-47F1-9214-48F977852EAC}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppName}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
WizardStyle=modern
WizardSmallImageFile=wizard-small.bmp,wizard-small2x.bmp
SetupIconFile=..\AudioPitcher\icon.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
DisableWelcomePage=yes
DisableDirPage=auto
DisableReadyPage=yes
OutputDir=Output
OutputBaseFilename=AudioPitcher-Setup
Compression=lzma2/ultra64
SolidCompression=yes
CloseApplications=yes
RestartApplications=no
SetupLogging=yes
VersionInfoVersion={#AppVersion}

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
spanish.VBCableOption=Instalar VB-CABLE (necesario para que funcione)
english.VBCableOption=Install VB-CABLE (required)
spanish.VBCableStatus=Instalando VB-CABLE de VB-Audio...
english.VBCableStatus=Installing VB-CABLE by VB-Audio...
spanish.LaunchApp=Abrir Audio Pitcher
english.LaunchApp=Launch Audio Pitcher
spanish.RestartAsk=¿Quieres reiniciar la PC ahora? Guarda tu trabajo antes de aceptar.
english.RestartAsk=Do you want to restart your PC now? Save your work before accepting.
spanish.RestartInfo=Se instaló VB-CABLE. Para que Audio Pitcher funcione hay que reiniciar la PC una vez: si no reinicias, puede que no se escuche nada.
english.RestartInfo=VB-CABLE was installed. Audio Pitcher needs one restart to work: if you don't restart, you may hear nothing.
spanish.DeleteSettings=¿Borrar también tu configuración de Audio Pitcher (tema, colores, apps favoritas)?
english.DeleteSettings=Also delete your Audio Pitcher settings (theme, colors, favorite apps)?

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "vbcable\*"; DestDir: "{tmp}\vbcable"; Flags: deleteafterinstall; Check: WantVBCable

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Check: WantDesktopIcon

[Run]
Filename: "{tmp}\vbcable\VBCABLE_Setup_x64.exe"; Parameters: "-i -h"; StatusMsg: "{cm:VBCableStatus}"; Flags: waituntilterminated; Check: WantVBCable; AfterInstall: MarkVBCableInstalled
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchApp}"; Flags: nowait postinstall skipifsilent runasoriginaluser
; Actualización silenciosa desde la app: reabrirla al terminar
Filename: "{app}\{#AppExe}"; Flags: nowait runasoriginaluser; Check: WizardSilent

; Limpia restos de versiones con .NET suelto (cientos de DLL) antes de instalar
; Nunca borrar *.dll: si la copia falla, la versión anterior tiene que seguir abriendo
[InstallDelete]
Type: files; Name: "{app}\*.json"
Type: files; Name: "{app}\createdump.exe"
Type: filesandordirs; Name: "{app}\cs"
Type: filesandordirs; Name: "{app}\de"
Type: filesandordirs; Name: "{app}\es"
Type: filesandordirs; Name: "{app}\fr"
Type: filesandordirs; Name: "{app}\it"
Type: filesandordirs; Name: "{app}\ja"
Type: filesandordirs; Name: "{app}\ko"
Type: filesandordirs; Name: "{app}\pl"
Type: filesandordirs; Name: "{app}\pt-BR"
Type: filesandordirs; Name: "{app}\ru"
Type: filesandordirs; Name: "{app}\tr"
Type: filesandordirs; Name: "{app}\zh-Hans"
Type: filesandordirs; Name: "{app}\zh-Hant"

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
var
  VBCableJustInstalled: Boolean;
  VBCableCheck, DesktopCheck: TNewCheckBox;

// En Windows 10/11 el driver corre desde DriverStore: se detecta por su servicio
function VBCableInstalled: Boolean;
begin
  Result := RegKeyExists(HKLM64, 'SYSTEM\CurrentControlSet\Services\VBAudioVACMME')
    or FileExists(ExpandConstant('{sys}\drivers\vbaudio_cable64_win10.sys'));
end;

// Opciones en la misma página que la carpeta de instalación
function AddCheck(Caption: String; Checked: Boolean; Top: Integer): TNewCheckBox;
begin
  Result := TNewCheckBox.Create(WizardForm);
  Result.Parent  := WizardForm.SelectDirPage;
  Result.Left    := WizardForm.DirEdit.Left;
  Result.Top     := Top;
  Result.Width   := WizardForm.SelectDirPage.ClientWidth - Result.Left;
  Result.Height  := ScaleY(20);
  Result.Caption := Caption;
  Result.Checked := Checked;
end;

procedure InitializeWizard;
var
  Top: Integer;
begin
  Top := WizardForm.DirEdit.Top + WizardForm.DirEdit.Height + ScaleY(24);
  if not VBCableInstalled then
  begin
    VBCableCheck := AddCheck(CustomMessage('VBCableOption'), True, Top);
    Top := Top + ScaleY(26);
  end;
  DesktopCheck := AddCheck(ExpandConstant('{cm:CreateDesktopIcon}'), False, Top);
end;

// Las actualizaciones (silenciosas) nunca tocan VB-CABLE
function WantVBCable: Boolean;
begin
  Result := (not WizardSilent) and (VBCableCheck <> nil) and VBCableCheck.Checked and (not VBCableInstalled);
end;

function AppRunning: Boolean;
var
  Locator, Wmi, Procs: Variant;
begin
  Result := False;
  try
    Locator := CreateOleObject('WbemScripting.SWbemLocator');
    Wmi := Locator.ConnectServer('', 'root\CIMV2', '', '');
    Procs := Wmi.ExecQuery('SELECT ProcessId FROM Win32_Process WHERE Name="{#AppExe}"');
    Result := Procs.Count > 0;
  except
  end;
end;

// Al actualizar, la app se está cerrando sola: esperar a que termine antes de tocar archivos
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  I: Integer;
begin
  Result := '';
  for I := 1 to 60 do
  begin
    if not AppRunning then Break;
    Log('Esperando a que se cierre {#AppExe}...');
    Sleep(500);
  end;
end;

function WantDesktopIcon: Boolean;
begin
  Result := DesktopCheck.Checked;
end;

procedure MarkVBCableInstalled;
begin
  VBCableJustInstalled := True;
end;

// Reinicio: solo si se instaló VB-CABLE, solo con pantallas, y siempre preguntando
procedure CurStepChanged(CurStep: TSetupStep);
var
  Code: Integer;
begin
  if (CurStep = ssDone) and VBCableJustInstalled and (not WizardSilent) then
    if MsgBox(CustomMessage('RestartInfo') + #13#10#13#10 + CustomMessage('RestartAsk'),
              mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      Exec(ExpandConstant('{sys}\shutdown.exe'), '/r /t 5', '', SW_HIDE, ewNoWait, Code);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and not UninstallSilent then
    if MsgBox(CustomMessage('DeleteSettings'), mbConfirmation, MB_YESNO) = IDYES then
      DelTree(ExpandConstant('{userappdata}\AudioPitcher'), True, True, True);
end;

