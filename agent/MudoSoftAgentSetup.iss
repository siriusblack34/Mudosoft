; MudoSoft Agent + Tray Installer Script
; Inno Setup 6.x required
; Build with: ISCC.exe MudoSoftAgentSetup.iss

#define MyAppName "MudoSoft Agent"
#define MyAppVersion "1.0.3.0"
#define MyAppPublisher "MudoSoft"
#define MyAppExeName "MudoSoft.Agent.exe"
#define TrayExeName "MudoSoft.Tray.exe"
#define OldHelperExeName "MudoSoft.RDHelper.exe"
#define ServiceName "MudosoftAgentService"
#define HelperTaskName "MudoSoftRDHelper"

[Setup]
AppId={{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\MudoSoft\Agent
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=.\installer_output
OutputBaseFilename=MudoSoftAgentSetup_{#MyAppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
; All Agent binaries, libraries and dependencies
Source: "C:\AgentDeploy_v31\*"; DestDir: "{app}"; Excludes: "appsettings*.json"; Flags: ignoreversion recursesubdirs createallsubdirs

; Settings files (only copy if they don't exist so we don't overwrite user configs)
Source: "C:\AgentDeploy_v31\appsettings.json"; DestDir: "{app}"; Flags: ignoreversion onlyifdoesntexist
Source: "C:\AgentDeploy_v31\appsettings.Development.json"; DestDir: "{app}"; Flags: ignoreversion onlyifdoesntexist

; NOTE: RDHelper is now integrated into MudoSoft.Agent.exe via --desktop-helper arg.
; We no longer deploy a separate MudoSoft.RDHelper.exe.

[Icons]
; Start Menu shortcut for Tray
Name: "{group}\MudoSoft Tray"; Filename: "{app}\{#TrayExeName}"
Name: "{group}\Uninstall MudoSoft Agent"; Filename: "{uninstallexe}"

[Registry]
; Auto-start Tray on Windows login (current user)
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "MudoSoftTray"; ValueData: """{app}\{#TrayExeName}"""; Flags: uninsdeletevalue
; Remove old RDHelper registry entry if exists (now using scheduled task)
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "MudoSoftRDHelper"; Flags: deletevalue

[Run]
; Install and start the service after installation
Filename: "{app}\{#MyAppExeName}"; Parameters: "/Install"; Flags: runhidden waituntilterminated; StatusMsg: "Windows Servisi kuruluyor..."
; Start Tray after installation
Filename: "{app}\{#TrayExeName}"; Flags: nowait postinstall skipifsilent; Description: "MudoSoft Tray'ı başlat"

[UninstallRun]
; Delete RDHelper scheduled task
Filename: "schtasks.exe"; Parameters: "/Delete /TN ""{#HelperTaskName}"" /F"; Flags: runhidden; RunOnceId: "DeleteHelperTask"
; Stop and remove the service before uninstallation
Filename: "sc.exe"; Parameters: "stop {#ServiceName}"; Flags: runhidden waituntilterminated; RunOnceId: "StopService"
Filename: "sc.exe"; Parameters: "delete {#ServiceName}"; Flags: runhidden waituntilterminated; RunOnceId: "DeleteService"
; Kill Tray and Helper if running
Filename: "taskkill.exe"; Parameters: "/F /IM {#TrayExeName}"; Flags: runhidden; RunOnceId: "KillTray"
Filename: "taskkill.exe"; Parameters: "/F /IM {#MyAppExeName}"; Flags: runhidden; RunOnceId: "KillAgent"
Filename: "taskkill.exe"; Parameters: "/F /IM {#OldHelperExeName}"; Flags: runhidden; RunOnceId: "KillOldHelper"

[Code]
// Helper to delete the old RDHelper exe if it exists
procedure DeleteOldHelper();
var
  OldPath: String;
begin
  OldPath := ExpandConstant('{app}\{#OldHelperExeName}');
  if FileExists(OldPath) then
  begin
    Log('Deleting old helper file: ' + OldPath);
    DeleteFile(OldPath);
  end;
end;

procedure CreateRDHelperScheduledTask();
var
  ResultCode: Integer;
  TaskXml: String;
  XmlPath: String;
  AgentPath: String;
begin
  AgentPath := ExpandConstant('{app}\{#MyAppExeName}');
  XmlPath := ExpandConstant('{tmp}\rdhelper_task.xml');
  
  // Create XML for scheduled task
  // IMPORTANT: We now run the MAIN Agent EXE with the --desktop-helper argument!
  TaskXml := '<?xml version="1.0" encoding="UTF-16"?>' + #13#10 +
    '<Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">' + #13#10 +
    '  <RegistrationInfo>' + #13#10 +
    '    <Description>MudoSoft RDHelper - Remote Desktop Screen Capture</Description>' + #13#10 +
    '  </RegistrationInfo>' + #13#10 +
    '  <Triggers>' + #13#10 +
    '    <LogonTrigger>' + #13#10 +
    '      <Enabled>true</Enabled>' + #13#10 +
    '    </LogonTrigger>' + #13#10 +
    '  </Triggers>' + #13#10 +
    '  <Principals>' + #13#10 +
    '    <Principal id="Author">' + #13#10 +
    '      <GroupId>S-1-5-32-545</GroupId>' + #13#10 +
    '      <RunLevel>HighestAvailable</RunLevel>' + #13#10 +
    '    </Principal>' + #13#10 +
    '  </Principals>' + #13#10 +
    '  <Settings>' + #13#10 +
    '    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>' + #13#10 +
    '    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>' + #13#10 +
    '    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>' + #13#10 +
    '    <AllowHardTerminate>true</AllowHardTerminate>' + #13#10 +
    '    <StartWhenAvailable>true</StartWhenAvailable>' + #13#10 +
    '    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>' + #13#10 +
    '    <AllowStartOnDemand>true</AllowStartOnDemand>' + #13#10 +
    '    <Enabled>true</Enabled>' + #13#10 +
    '    <Hidden>true</Hidden>' + #13#10 +
    '    <RunOnlyIfIdle>false</RunOnlyIfIdle>' + #13#10 +
    '    <WakeToRun>false</WakeToRun>' + #13#10 +
    '    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>' + #13#10 +
    '    <Priority>7</Priority>' + #13#10 +
    '  </Settings>' + #13#10 +
    '  <Actions Context="Author">' + #13#10 +
    '    <Exec>' + #13#10 +
    '      <Command>"' + AgentPath + '"</Command>' + #13#10 +
    '      <Arguments>--desktop-helper</Arguments>' + #13#10 +
    '    </Exec>' + #13#10 +
    '  </Actions>' + #13#10 +
    '</Task>';
  
  // Save XML to temp file
  SaveStringToFile(XmlPath, TaskXml, False);
  
  // Delete existing task if exists
  Exec('schtasks.exe', '/Delete /TN "{#HelperTaskName}" /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  
  // Create new task from XML
  if Exec('schtasks.exe', '/Create /TN "{#HelperTaskName}" /XML "' + XmlPath + '" /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    Log('RDHelper scheduled task created successfully');
    // Run the task immediately
    Exec('schtasks.exe', '/Run /TN "{#HelperTaskName}"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end
  else
    Log('Failed to create RDHelper scheduled task');
    
  // Clean up XML file
  DeleteFile(XmlPath);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
  begin
     // Delete old helper before installing new files (to avoid conflicts)
     DeleteOldHelper();
  end;

  if CurStep = ssPostInstall then
  begin
    CreateRDHelperScheduledTask();
  end;
end;

// Check if service exists and stop it before upgrade
function InitializeSetup(): Boolean;
var
  ResultCode: Integer;
begin
  // Delete existing scheduled task
  Exec('schtasks.exe', '/Delete /TN "{#HelperTaskName}" /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  // Try to stop existing service (ignore errors if not exists)
  Exec('sc.exe', 'stop {#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  // Kill existing Tray and RDHelper
  Exec('taskkill.exe', '/F /IM {#TrayExeName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec('taskkill.exe', '/F /IM {#MyAppExeName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec('taskkill.exe', '/F /IM {#OldHelperExeName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(2000); // Wait for processes to stop
  Result := True;
end;
