# Builds a release version and installs it for the current user: %LOCALAPPDATA%\Programs\UhfPlayer plus a
# Start menu entry "UHF Player". Run it again after a git pull to update. -Uninstall removes both again;
# sources, favorites and caches in %LOCALAPPDATA%\UhfPlayer stay.
param(
    [ValidateSet('x64', 'ARM64')][string]$Platform = 'x64',
    [switch]$Uninstall
)
$ErrorActionPreference = 'Stop'

$target = Join-Path $env:LOCALAPPDATA 'Programs\UhfPlayer'
$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'UHF Player.lnk'

if (Get-Process UhfPlayer.App -ErrorAction SilentlyContinue) { throw 'UHF Player laeuft noch. Bitte erst schliessen.' }

if ($Uninstall) {
    Remove-Item $target -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item $shortcut -Force -ErrorAction SilentlyContinue
    Write-Host 'UHF Player wurde entfernt.'
    return
}

$root = Join-Path $PSScriptRoot '..'
if (-not (Test-Path (Join-Path $root 'src\UhfPlayer.App\libmpv\libmpv-2.dll'))) {
    & (Join-Path $PSScriptRoot 'fetch-libmpv.ps1') -Platform $Platform
}

$rid = if ($Platform -eq 'ARM64') { 'win-arm64' } else { 'win-x64' }
# A fresh folder, so files from an older version do not linger.
Remove-Item $target -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish (Join-Path $root 'src\UhfPlayer.App') -c Release -r $rid -p:Platform=$Platform --self-contained -o $target
if ($LASTEXITCODE -ne 0) { throw "dotnet publish fehlgeschlagen (Exit-Code $LASTEXITCODE)." }

$shell = New-Object -ComObject WScript.Shell
$link = $shell.CreateShortcut($shortcut)
$link.TargetPath = Join-Path $target 'UhfPlayer.App.exe'
$link.WorkingDirectory = $target
$link.Description = 'IPTV-Player fuer M3U, Xtream Codes und EPG'
$link.Save()

Write-Host "Installiert nach $target. Im Startmenue unter 'UHF Player'."
