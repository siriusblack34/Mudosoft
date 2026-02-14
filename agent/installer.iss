; MudoSoft Agent Installer Script - Inno Setup
[Setup]
AppName=MudoSoft Agent
AppVersion=1.0.0.19
AppPublisher=MudoSoft
DefaultDirName=C:\MudoSoft
DefaultGroupName=MudoSoft
OutputDir=.\installer_output
OutputBaseFilename=MudoSoft-Agent-Setup-v1.0.0.19
Compression=lzma2
SolidCompression=yes
PrivilegesRequired=admin
DisableProgramGroupPage=yes
DisableDirPage=yes

[Files]
Source: "publish_win_x86\MudoSoft.Agent.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "publish_win_x86\appsettings.json"; DestDir: "{app}"; Flags: ignoreversion

[Run]
; Install as Windows Service after copying files
Filename: "{app}\MudoSoft.Agent.exe"; Parameters: "/Install"; Flags: runhidden waituntilterminated; StatusMsg: "Windows Servisi kuruluyor..."
; Start the service immediately
Filename: "sc"; Parameters: "start MudosoftAgentService"; Flags: runhidden waituntilterminated; StatusMsg: "Servis başlatılıyor..."

[UninstallRun]
; Uninstall service before removing files
Filename: "{app}\MudoSoft.Agent.exe"; Parameters: "/Uninstall"; Flags: runhidden waituntilterminated

[Code]
var
  ResultCode: Integer;
  
procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
  begin
    // Stop existing service before install
    Exec('sc', 'stop MudosoftAgentService', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec('taskkill', '/F /IM MudoSoft.Agent.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Sleep(2000);
  end;
end;
