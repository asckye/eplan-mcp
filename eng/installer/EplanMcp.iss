; Compile with ISCC /DAppVersion=0.1.0 /DPayloadDir=<staged app> /DOutputDir=<artifacts>.
#ifndef AppVersion
  #error AppVersion is required
#endif
#ifndef PayloadDir
  #error PayloadDir is required
#endif
#ifndef OutputDir
  #error OutputDir is required
#endif
#define ProductId "EplanMcp"

; ArchitecturesAllowed=x64compatible needs Inno Setup 6.3 or later.
#if Ver < EncodeVer(6, 3, 0)
  #error Inno Setup 6.3 or later is required.
#endif

[Setup]
AppId={#ProductId}
AppName=EplanMcp
AppVersion={#AppVersion}
AppPublisher=EplanMcp contributors
AppPublisherURL=https://github.com/asckye/eplan-mcp
DefaultDirName={%USERPROFILE}\EplanMcp\app
UsePreviousAppDir=no
DisableDirPage=yes
DisableProgramGroupPage=yes
DefaultGroupName=EplanMcp
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
CloseApplications=no
RestartApplications=no
RestartIfNeededByRun=no
ChangesEnvironment=no
SetupLogging=yes
OutputDir={#OutputDir}
OutputBaseFilename=EplanMcp-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\workbench\EplanMcp.Workbench.exe

[Tasks]
Name: autostart; Description: "Start Workbench when I sign in"; Flags: checkedonce

[Files]
Source: "{#PayloadDir}\eplanmcp.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PayloadDir}\workbench\*"; DestDir: "{app}\workbench"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#PayloadDir}\bridge\net481\*"; DestDir: "{app}\bridge\net481"; Flags: ignoreversion recursesubdirs createallsubdirs
; Repository source: packaging/claude-plugin/ (M5 step 10). Missing content must fail packaging.
Source: "{#PayloadDir}\claude-plugin\*"; DestDir: "{app}\claude-plugin"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#PayloadDir}\docs\*"; DestDir: "{app}\docs"; Flags: ignoreversion recursesubdirs createallsubdirs

[InstallDelete]
; Replace program directories on upgrade; never delete anything outside app.
Type: filesandordirs; Name: "{app}\workbench"
Type: filesandordirs; Name: "{app}\bridge"
Type: filesandordirs; Name: "{app}\claude-plugin"
Type: filesandordirs; Name: "{app}\docs"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "EplanMcp Workbench"; ValueData: """{app}\workbench\EplanMcp.Workbench.exe"""; Tasks: autostart; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "EplanMcp Workbench"; Tasks: not autostart; Flags: deletevalue

[Icons]
Name: "{group}\EplanMcp Workbench"; Filename: "{app}\workbench\EplanMcp.Workbench.exe"
Name: "{group}\Getting started"; Filename: "{app}\docs\getting-started\zh-CN.md"
Name: "{group}\Getting started (English)"; Filename: "{app}\docs\getting-started\en.md"
Name: "{group}\Uninstall EplanMcp"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\eplanmcp.exe"; Parameters: "{code:RegistrationArguments}"; WorkingDir: "{app}"; Check: HasVariants; Flags: waituntilterminated logoutput
Filename: "{app}\eplanmcp.exe"; Parameters: "doctor"; WorkingDir: "{app}"; Flags: waituntilterminated logoutput
Filename: "{app}\docs\getting-started\zh-CN.md"; Description: "Open the getting started guide"; Flags: postinstall shellexec skipifsilent

[UninstallRun]
; InitializeUninstall already checked success before any deletion. This hook is idempotent.
Filename: "{app}\eplanmcp.exe"; Parameters: "uninstall-addin"; WorkingDir: "{app}"; RunOnceId: "UnregisterAddin"; Flags: waituntilterminated logoutput

[Code]
const
  UninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#ProductId}_is1';
var
  Variants: TInputOptionWizardPage;
  Upgrade: Boolean;

function FixedApp: String;
begin
  Result := ExpandConstant('{%USERPROFILE}\EplanMcp\app');
end;

function RunningProblem: String;
var
  Service, Processes, Process: Variant;
  I: Integer;
  Path: String;
begin
  Result := '';
  try
    Service := CreateOleObject('WbemScripting.SWbemLocator');
    Service := Service.ConnectServer('', 'root\cimv2');
    Processes := Service.ExecQuery('SELECT Name, ExecutablePath FROM Win32_Process WHERE Name=''eplanmcp.exe'' OR Name=''EplanMcp.Workbench.exe''');
    for I := 0 to Processes.Count - 1 do begin
      Process := Processes.ItemIndex(I);
      if VarIsNull(Process.ExecutablePath) then
        Result := Result + Process.Name + ' (path unavailable)' + #13#10
      else begin
        Path := Process.ExecutablePath;
        if Pos(Lowercase(FixedApp + '\'), Lowercase(Path)) = 1 then
          Result := Result + Path + #13#10;
      end;
    end;
    if Result <> '' then
      Result := 'Close MCP client sessions and exit Workbench, then try again:' + #13#10 + Result;
  except
    Result := 'Cannot check running programs. No files will be replaced or removed. ' + GetExceptionMessage;
  end;
end;

function InitializeSetup: Boolean;
var
  Previous, Problem: String;
  OldVersion, NewVersion: Int64;
begin
  Upgrade := RegQueryStringValue(HKCU, UninstallKey, 'DisplayVersion', Previous);
  Result := False;
  if Upgrade then begin
    if not StrToVersion(Previous, OldVersion) or not StrToVersion('{#AppVersion}', NewVersion) then begin
      MsgBox('Cannot compare installed versions. Uninstall the existing version first.', mbError, MB_OK);
      Exit;
    end;
    if ComparePackedVersion(OldVersion, NewVersion) > 0 then begin
      MsgBox('Downgrades are not supported. Uninstall the newer version first; your data will be retained.', mbError, MB_OK);
      Exit;
    end;
  end;
  Problem := RunningProblem;
  if Problem <> '' then begin
    MsgBox(Problem, mbError, MB_OK);
    Exit;
  end;
  Result := True;
end;

procedure InitializeWizard;
var
  Product, Version: TFindRec;
  Root, Platform, Exe, FileVersion: String;
  Supported: Boolean;
begin
  WizardForm.DirEdit.Text := FixedApp;
  Variants := CreateInputOptionPage(wpSelectTasks, 'EPLAN variants',
    'Select installed variants to register',
    'Registration starts EPLAN. Close EPLAN first. On upgrade, existing registrations keep working after an EPLAN restart; leave these unchecked.', False, False);
  Root := ExpandConstant('{commonpf64}\EPLAN\');
  Platform := Root + 'Platform\';
  if FindFirst(Root + '*', Product) then begin
    try
      repeat
        if (Product.Name <> '.') and (Product.Name <> '..') and (Product.Name <> 'Platform') then begin
          Supported := False;
          if FindFirst(Platform + '*', Version) then begin
            try
              repeat
                Exe := Platform + Version.Name + '\Bin\EPLAN.exe';
                if FileExists(Exe) and GetVersionNumbersString(Exe, FileVersion) then
                  if (StrToIntDef(Copy(FileVersion, 1, 4), 0) >= 2024) and
                     (StrToIntDef(Copy(FileVersion, 1, 4), 0) <= 2027) then
                    Supported := Supported or FileExists(Root + Product.Name + '\' + Version.Name + '\Bin\W3u.exe');
              until not FindNext(Version);
            finally
              FindClose(Version);
            end;
          end;
          if Supported then Variants.Values[Variants.Add(Product.Name)] := not Upgrade and not WizardSilent;
        end;
      until not FindNext(Product);
    finally
      FindClose(Product);
    end;
  end;
  // Preserve changes made by Workbench settings rather than an old installer's task selection.
  if Upgrade then
    WizardForm.TasksList.Checked[0] := RegValueExists(HKCU,
      'Software\Microsoft\Windows\CurrentVersion\Run', 'EplanMcp Workbench');
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  // Force the fixed location even when /DIR was supplied on the command line.
  WizardForm.DirEdit.Text := FixedApp;
  Result := True;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := RunningProblem;
end;

function RegistrationArguments(Param: String): String;
var
  I: Integer;
begin
  Result := 'install-addin';
  for I := 0 to Variants.CheckListBox.Items.Count - 1 do
    if Variants.Values[I] then
      Result := Result + ' --variant "' + Variants.CheckListBox.Items[I] + '"';
end;

function HasVariants: Boolean;
begin
  Result := RegistrationArguments('') <> 'install-addin';
end;

function InitializeUninstall: Boolean;
var
  Problem: String;
  ExitCode, RemoveClients: Integer;
begin
  Result := False;
  Problem := RunningProblem;
  if Problem <> '' then begin
    MsgBox(Problem, mbError, MB_OK);
    Exit;
  end;
  RemoveClients := IDNO;
  if not UninstallSilent then
    RemoveClients := MsgBox('Also remove the client configuration entries recorded by client-config? Other settings and backups are retained. Choose No to keep these entries, or Cancel to stop uninstall.',
      mbConfirmation, MB_YESNOCANCEL or MB_DEFBUTTON2);
  if RemoveClients = IDCANCEL then Exit;
  if RemoveClients = IDYES then
    if not Exec(FixedApp + '\eplanmcp.exe', 'client-config --remove', FixedApp,
      SW_SHOW, ewWaitUntilTerminated, ExitCode) or (ExitCode <> 0) then begin
      MsgBox('Client configuration removal failed. Resolve the reported problem and retry; program files and data have been retained.', mbError, MB_OK);
      Exit;
    end;
  // Refuse deletion if unregistering failed or EPLAN still needs the engineer's attention.
  if not Exec(FixedApp + '\eplanmcp.exe', 'uninstall-addin', FixedApp,
    SW_SHOW, ewWaitUntilTerminated, ExitCode) or (ExitCode <> 0) then begin
    MsgBox('Add-in removal failed. Close EPLAN or resolve its dialogs, then retry uninstall. Program files and data have been retained.', mbError, MB_OK);
    Exit;
  end;
  Result := True;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssDone then
    MsgBox('Installation finished. Check doctor in the setup log. If registration was not confirmed, close EPLAN and run install-addin again. Restart EPLAN to load the updated add-in.', mbInformation, MB_OK);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataRoot: String;
begin
  // Workbench can enable this after an install whose autostart task was unchecked.
  if CurUninstallStep = usPostUninstall then
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'EplanMcp Workbench');
  if CurUninstallStep = usDone then begin
    DataRoot := GetEnv('EPLANMCP_HOME');
    if DataRoot = '' then DataRoot := ExpandConstant('{%USERPROFILE}\EplanMcp');
    // A line must not start with #: the preprocessor would read #13#10 as a directive.
    MsgBox('Configuration, audit, logs, scratch, exports and backups are retained in:' + #13#10 + DataRoot + #13#10 +
      'Remove the Claude Code plugin with its plugin commands.', mbInformation, MB_OK);
  end;
end;
