; CONVERTit installer (Inno Setup 6). Built by build\publish.ps1:
;   ISCC /DAppVersion=0.0.5 /DSourceDir=..\..\artifacts\publish\CONVERTit /DOutputDir=..\..\artifacts convertit.iss
;
; Installs for the current user by default (no administrator rights); "for all users" can be chosen in the dialog.

#ifndef AppVersion
  #define AppVersion "0.0.5"
#endif
#ifndef SourceDir
  #define SourceDir "..\..\artifacts\publish\CONVERTit"
#endif
#ifndef OutputDir
  #define OutputDir "..\..\artifacts"
#endif
#define AppUserModelId "sksdesign.CONVERTit"

[Setup]
AppId={{3C7A9E1B-5D2F-4B8A-9E61-0F4C2A7D5B93}
AppName=CONVERTit
AppVersion={#AppVersion}
AppVerName=CONVERTit {#AppVersion}
AppPublisher=sksdesign | complicatiion
AppPublisherURL=https://sksdesign.de
AppSupportURL=https://github.com/complicatiion/CONVERTit
DefaultDirName={autopf}\CONVERTit
DefaultGroupName=CONVERTit
DisableProgramGroupPage=yes
LicenseFile=..\..\licenses\LICENSE.md
OutputDir={#OutputDir}
OutputBaseFilename=convertit_v{#AppVersion}_setup
SetupIconFile=..\..\src\CONVERTit.App\Assets\convertit.ico
UninstallDisplayIcon={app}\CONVERTit.exe
UninstallDisplayName=CONVERTit {#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "de"; MessagesFile: "compiler:Languages\German.isl"

[CustomMessages]
en.GroupExtras=Additional options:
de.GroupExtras=Weitere Optionen:
en.TaskDesktopIcon=Create a desktop shortcut
de.TaskDesktopIcon=Desktop-Verknüpfung erstellen
en.PurgeQuestion=Also delete the CONVERTit settings and the downloaded FFmpeg (data folder)? Converted files are not touched.
de.PurgeQuestion=Auch die CONVERTit-Einstellungen und das geladene FFmpeg (Datenordner) löschen? Konvertierte Dateien bleiben erhalten.

[Tasks]
Name: "desktopicon"; Description: "{cm:TaskDesktopIcon}"; GroupDescription: "{cm:GroupExtras}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
; The AppUserModelID links the shortcut with the app's taskbar button and its notifications.
Name: "{group}\CONVERTit"; Filename: "{app}\CONVERTit.exe"; AppUserModelID: "{#AppUserModelId}"
Name: "{autodesktop}\CONVERTit"; Filename: "{app}\CONVERTit.exe"; AppUserModelID: "{#AppUserModelId}"; Tasks: desktopicon

[Registry]
; Written by the app at start as well (per user); removed on uninstall.
Root: HKCU; Subkey: "Software\Classes\AppUserModelId\{#AppUserModelId}"; Flags: uninsdeletekey dontcreatekey
Root: HKCU; Subkey: "Software\Classes\convertit"; Flags: uninsdeletekey dontcreatekey

[Run]
Filename: "{app}\CONVERTit.exe"; Description: "{cm:LaunchProgram,CONVERTit}"; Flags: nowait postinstall skipifsilent

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
begin
  if CurUninstallStep = usUninstall then
  begin
    Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM CONVERTit.exe /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    { Removes the Explorer context menu and the notification registration of the current user }
    Exec(ExpandConstant('{app}\CONVERTit.exe'), '--unregister', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    if (not UninstallSilent) and
       (MsgBox(CustomMessage('PurgeQuestion'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES) then
      DelTree(ExpandConstant('{localappdata}\CONVERTit'), True, True, True);
  end;
end;
