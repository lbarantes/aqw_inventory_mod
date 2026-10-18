# Troca a versão instalada do AQW Inventory Thumbnails.
#
#   powershell -ExecutionPolicy Bypass -File tools\use-version.ps1         -> lista as versões
#   powershell -ExecutionPolicy Bypass -File tools\use-version.ps1 v0.1.0  -> instala a que começa com "v0.1.0"
#
# (-ExecutionPolicy Bypass: o Windows bloqueia .ps1 por padrão. Arquivo salvo
#  com BOM para o PowerShell 5.1 ler os acentos.)
#
# Feche o jogo antes (a DLL fica travada enquanto ele roda).
param([string]$Name)

$root     = Split-Path -Parent $PSScriptRoot
$versions = Join-Path $root 'versions'
$plugins  = Join-Path (Split-Path -Parent $root) 'BepInEx\plugins'

$all = Get-ChildItem $versions -Directory | Sort-Object Name
if (-not $Name) {
    'Versões disponíveis:'
    $all | ForEach-Object { '  ' + $_.Name }
    return
}

$match = @($all | Where-Object { $_.Name -like "$Name*" })
if ($match.Count -ne 1) {
    "Nenhuma ou mais de uma versão corresponde a '$Name':"
    $match | ForEach-Object { '  ' + $_.Name }
    exit 1
}

Copy-Item (Join-Path $match[0].FullName 'AQWInventoryThumbs.dll') $plugins -Force
"Instalado: $($match[0].Name) -> $plugins"
