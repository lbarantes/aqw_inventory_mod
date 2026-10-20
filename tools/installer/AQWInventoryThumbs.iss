; Instalador do AQW Inventory Thumbnails (Inno Setup 6).
;
; Gerado por tools/build-release.ps1, que passa:
;   /DAppVersion=0.4.0          versão do mod (PluginInfo.Version)
;   /DAppLabel=full-set-preview nome da versão (VersionLabel do .csproj)
;   /DBuildDir=<pasta>          com bepinex\ e mod\AQWInventoryThumbs.dll prontos
;
; O que faz:
;   - acha a pasta do jogo pela Steam (app 2094530), em qualquer biblioteca;
;   - instala o BepInEx 5.4.23.5 só se ele ainda não estiver na pasta (outros
;     mods, como o de tradução, podem já ter instalado);
;   - grava BepInEx\plugins\AQWInventoryThumbs.dll.
; Reinstalar atualiza a DLL. Desinstalar remove só a DLL do mod: BepInEx,
; configurações (.cfg) e o cache de miniaturas ficam.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef AppLabel
  #define AppLabel ""
#endif
#ifndef BuildDir
  #define BuildDir "..\..\dist\staging"
#endif

#define GameExe "AdventureQuest Worlds Infinity.exe"
#define SteamAppId "2094530"
#define RepoUrl "https://github.com/lbarantes/aqw_inventory_mod"

[Setup]
AppId={{9CB65D31-887D-440B-925D-D70448BD137A}
AppName=AQW Inventory Thumbnails
AppVersion={#AppVersion}
AppVerName=AQW Inventory Thumbnails {#AppVersion} ({#AppLabel})
AppPublisher=lbarantes
AppPublisherURL={#RepoUrl}
AppSupportURL={#RepoUrl}/issues
AppUpdatesURL={#RepoUrl}/releases
DefaultDirName={code:GetDefaultGameDir}
AppendDefaultDirName=no
DirExistsWarning=no
UsePreviousAppDir=yes
DisableProgramGroupPage=yes
DisableReadyMemo=no
PrivilegesRequired=admin
; /CURRENTUSER só pra testar numa pasta sem permissão de admin
PrivilegesRequiredOverridesAllowed=commandline
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputBaseFilename=AQWInventoryThumbs-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallFilesDir={app}\BepInEx\AQWInventoryThumbs-uninstall
UninstallDisplayName=AQW Inventory Thumbnails
CloseApplications=yes

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Messages]
SelectDirLabel3=Escolha a pasta do AdventureQuest Worlds Infinity (a que tem o "{#GameExe}").
SelectDirBrowseLabel=Se a pasta abaixo não for a do jogo, clique em Procurar.

[Files]
; BepInEx: nunca sobrescreve nem desinstala (outros mods podem depender dele)
Source: "{#BuildDir}\bepinex\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs onlyifdoesntexist uninsneveruninstall
Source: "{#BuildDir}\mod\AQWInventoryThumbs.dll"; DestDir: "{app}\BepInEx\plugins"; Flags: ignoreversion

[Code]
// valor entre aspas de uma linha VDF/ACF: "chave"   "valor"
function VdfValue(Line, Key: String): String;
var
  P: Integer;
begin
  Result := '';
  P := Pos('"' + Key + '"', Lowercase(Line));
  if P = 0 then Exit;
  Line := Copy(Line, P + Length(Key) + 2, MaxInt);
  P := Pos('"', Line);
  if P = 0 then Exit;
  Line := Copy(Line, P + 1, MaxInt);
  P := Pos('"', Line);
  if P = 0 then Exit;
  Result := Copy(Line, 1, P - 1);
  StringChangeEx(Result, '\\', '\', True);
end;

// pasta do jogo dentro de uma biblioteca da Steam, ou '' se não estiver nela
function GameDirInLibrary(Lib: String): String;
var
  Lines: TArrayOfString;
  I: Integer;
  InstallDir: String;
begin
  Result := '';
  StringChangeEx(Lib, '/', '\', True);
  if not LoadStringsFromFile(AddBackslash(Lib) + 'steamapps\appmanifest_{#SteamAppId}.acf', Lines) then Exit;
  for I := 0 to GetArrayLength(Lines) - 1 do
  begin
    InstallDir := VdfValue(Lines[I], 'installdir');
    if InstallDir <> '' then
    begin
      Result := AddBackslash(Lib) + 'steamapps\common\' + InstallDir;
      Exit;
    end;
  end;
end;

function FindGameDir: String;
var
  Steam, Lib: String;
  Lines: TArrayOfString;
  I: Integer;
begin
  Result := '';
  if not RegQueryStringValue(HKCU, 'Software\Valve\Steam', 'SteamPath', Steam) then
    if not RegQueryStringValue(HKLM, 'SOFTWARE\WOW6432Node\Valve\Steam', 'InstallPath', Steam) then
      Exit;
  StringChangeEx(Steam, '/', '\', True);

  Result := GameDirInLibrary(Steam);
  if Result <> '' then Exit;

  // bibliotecas extras (outros discos) ficam listadas aqui
  if LoadStringsFromFile(AddBackslash(Steam) + 'steamapps\libraryfolders.vdf', Lines) then
    for I := 0 to GetArrayLength(Lines) - 1 do
    begin
      Lib := VdfValue(Lines[I], 'path');
      if Lib <> '' then
      begin
        Result := GameDirInLibrary(Lib);
        if Result <> '' then Exit;
      end;
    end;
end;

function GetDefaultGameDir(Param: String): String;
begin
  Result := FindGameDir;
  Log('Pasta do jogo detectada: ' + Result);
  if Result = '' then
    Result := ExpandConstant('{commonpf32}\Steam\steamapps\common\AdventureQuest Worlds Infinity');
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID = wpSelectDir) and
     not FileExists(AddBackslash(WizardDirValue) + '{#GameExe}') then
    Result := MsgBox('Não encontrei "{#GameExe}" nesta pasta.' + #13#10 + #13#10 +
                     'Instalar mesmo assim?', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES;
end;

function UpdateReadyMemo(Space, NewLine, MemoUserInfoInfo, MemoDirInfo, MemoTypeInfo,
  MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
begin
  Result := MemoDirInfo + NewLine + NewLine +
            'Mod:' + NewLine + Space + 'AQW Inventory Thumbnails {#AppVersion} ({#AppLabel})' + NewLine + NewLine +
            'BepInEx:' + NewLine + Space;
  if FileExists(AddBackslash(WizardDirValue) + 'BepInEx\core\BepInEx.dll') then
    Result := Result + 'já instalado, não será alterado'
  else
    Result := Result + 'será instalado (5.4.23.5)';
end;
