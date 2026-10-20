# Monta o release em dist/release:
#   AQWInventoryThumbs-Setup.exe   instalador (acha o jogo pela Steam, instala o
#                                  BepInEx se faltar, grava a DLL em plugins)
#   AQWInventoryThumbs.zip         instalacao manual: extrair na pasta do jogo
#   AQWInventoryThumbs.dll         so a DLL, para quem ja tem o mod
# e tambem dist/release-notes.md (secao da versao no CHANGELOG.md + como
# instalar) e dist/release-tag.txt (v<Version>-<VersionLabel>).
#
# A DLL NAO e compilada aqui: o codigo depende das DLLs do jogo
# (Assembly-CSharp etc.), que nao podem ir para o repositorio. Ela vem de
# versions/v<Version>-<VersionLabel>/, que o build local (dotnet build) gera e
# que vai no commit de cada versao. Com -Build, compila antes (so no PC com o
# jogo).
#
# O BepInEx 5.4.23.5 e baixado do release oficial e conferido por SHA-256.
# Sem o Inno Setup 6 o instalador nao e gerado; com -RequireInstaller isso e
# erro (CI).
#
#   powershell -ExecutionPolicy Bypass -File tools\build-release.ps1 [-Build] [-RequireInstaller]
#
# (Arquivo em ASCII de proposito: o PowerShell 5 le scripts sem BOM como ANSI.)

param(
    [switch]$Build,
    [string]$Iscc = '',
    [switch]$RequireInstaller
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$BepInExVersion = '5.4.23.5'
$BepInExUrl     = "https://github.com/BepInEx/BepInEx/releases/download/v$BepInExVersion/BepInEx_win_x64_$BepInExVersion.zip"
$BepInExSha256  = '82f9878551030f54657792c0740d9d51a09500eeae1fba21106b0c441e6732c4'

# 1. versao: .csproj (Version + VersionLabel) e PluginInfo.Version precisam bater
$csproj = Get-Content inventory_mod.csproj -Raw
$version = [regex]::Match($csproj, '<Version>([^<]+)</Version>').Groups[1].Value
$label   = [regex]::Match($csproj, '<VersionLabel>([^<]+)</VersionLabel>').Groups[1].Value
if (-not $version -or -not $label) { throw 'Nao achei Version/VersionLabel em inventory_mod.csproj' }
$pluginVersion = [regex]::Match((Get-Content Plugin.cs -Raw), 'const string Version = "([^"]+)"').Groups[1].Value
if (-not $version.StartsWith($pluginVersion)) {
    throw "Versao do .csproj ($version) nao comeca com PluginInfo.Version ($pluginVersion)"
}
$tag = "v$version-$label"
Write-Host "== Release $tag"

if ($Build) {
    & dotnet build -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Falha no dotnet build' }
}
$dll = Join-Path $root "versions\$tag\AQWInventoryThumbs.dll"
if (-not (Test-Path $dll)) {
    throw "DLL da versao nao encontrada: versions\$tag\AQWInventoryThumbs.dll (rode 'dotnet build -c Release' no PC com o jogo e faca commit)"
}

$out     = Join-Path $root 'dist\release'
$staging = Join-Path $root 'dist\staging'
$cache   = Join-Path $root 'dist\cache'
foreach ($d in $out, $staging) { if (Test-Path $d) { Remove-Item $d -Recurse -Force } }
New-Item -ItemType Directory -Force $out, $staging, $cache, (Join-Path $staging 'mod') | Out-Null
Copy-Item $dll (Join-Path $staging 'mod') -Force
Copy-Item $dll $out -Force

# 2. BepInEx oficial, conferido por hash
$bepZip = Join-Path $cache "BepInEx_win_x64_$BepInExVersion.zip"
if (-not (Test-Path $bepZip)) {
    Write-Host "== Baixando BepInEx $BepInExVersion"
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest $BepInExUrl -OutFile $bepZip -UseBasicParsing
}
$hash = (Get-FileHash $bepZip -Algorithm SHA256).Hash.ToLowerInvariant()
if ($hash -ne $BepInExSha256) {
    Remove-Item $bepZip -Force
    throw "SHA-256 do BepInEx nao confere ($hash)."
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$bepDir = Join-Path $staging 'bepinex'
[IO.Compression.ZipFile]::ExtractToDirectory($bepZip, $bepDir)
Remove-Item (Join-Path $bepDir 'changelog.txt') -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force (Join-Path $bepDir 'BepInEx\plugins') | Out-Null

# 3. .zip para instalacao manual: extrair na pasta do jogo e pronto
$tree = Join-Path $staging 'zip'
Copy-Item $bepDir $tree -Recurse
Copy-Item $dll (Join-Path $tree 'BepInEx\plugins') -Force
$zip = Join-Path $out 'AQWInventoryThumbs.zip'
[IO.Compression.ZipFile]::CreateFromDirectory($tree, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)
Write-Host "OK: $zip"

# 4. instalador
if (-not $Iscc) {
    foreach ($c in "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
                   "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe") {
        if (Test-Path $c) { $Iscc = $c; break }
    }
}
if (-not $Iscc -or -not (Test-Path $Iscc)) {
    if ($RequireInstaller) { throw 'Inno Setup 6 (ISCC.exe) nao encontrado.' }
    Write-Host 'AVISO: Inno Setup 6 nao encontrado; instalador nao gerado (o .zip foi).' -ForegroundColor Yellow
} else {
    & $Iscc /Q "/DAppVersion=$pluginVersion" "/DAppLabel=$label" "/DBuildDir=$staging" "/O$out" `
        (Join-Path $PSScriptRoot 'installer\AQWInventoryThumbs.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar o instalador.' }
    Write-Host "OK: $(Join-Path $out 'AQWInventoryThumbs-Setup.exe')"
}

# 5. notas do release: a secao "## v<Version> ..." do CHANGELOG.md (UTF-8)
$changelog = Get-Content CHANGELOG.md -Raw -Encoding UTF8
$m = [regex]::Match($changelog, "(?ms)^## v$([regex]::Escape($version))\b[^\r\n]*\r?\n(.*?)(?=^## v|\z)")
if (-not $m.Success) { throw "CHANGELOG.md sem a secao '## v$version'" }
$notes = $m.Groups[1].Value.Trim()
$template = Get-Content (Join-Path $PSScriptRoot 'release-notes.md') -Raw -Encoding UTF8
$utf8 = New-Object Text.UTF8Encoding($false)
[IO.File]::WriteAllText((Join-Path $root 'dist\release-notes.md'), $template.Replace('{{NOTES}}', $notes), $utf8)
[IO.File]::WriteAllText((Join-Path $root 'dist\release-tag.txt'), $tag, $utf8)

Write-Host "Release $tag pronto em $out" -ForegroundColor Green
