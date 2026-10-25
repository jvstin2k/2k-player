# Downloads the latest libmpv build (shinchiro/mpv-winbuild-cmake) into src/UhfPlayer.App/libmpv.
param([ValidateSet('x64', 'ARM64')][string]$Platform = 'x64')
$ErrorActionPreference = 'Stop'

$pattern = if ($Platform -eq 'ARM64') { '^mpv-dev-aarch64-\d.*\.7z$' } else { '^mpv-dev-x86_64-\d.*\.7z$' }
# In GitHub Actions the token avoids the low anonymous API rate limit shared by all runners.
$headers = if ($env:GITHUB_TOKEN) { @{ Authorization = "Bearer $env:GITHUB_TOKEN" } } else { @{} }
$release = Invoke-RestMethod 'https://api.github.com/repos/shinchiro/mpv-winbuild-cmake/releases/latest' -Headers $headers
$asset = $release.assets | Where-Object name -Match $pattern | Select-Object -First 1
if (-not $asset) { throw "No libmpv asset matching $pattern in release $($release.tag_name)." }

$target = Join-Path $PSScriptRoot '..\src\UhfPlayer.App\libmpv'
$archive = Join-Path $env:TEMP $asset.name
New-Item -ItemType Directory -Force $target | Out-Null
Write-Host "Downloading $($asset.name) ..."
Invoke-WebRequest $asset.browser_download_url -OutFile $archive

# Windows' built-in tar (libarchive) reads .7z; 7-Zip is used when installed.
if (Get-Command 7z -ErrorAction SilentlyContinue) { 7z e $archive libmpv-2.dll "-o$target" -y | Out-Null }
else { tar -xf $archive -C $target libmpv-2.dll }
Remove-Item $archive
# Native tools do not throw on failure, so check the result instead of trusting the exit.
if (-not (Test-Path (Join-Path $target 'libmpv-2.dll'))) { throw "Entpacken fehlgeschlagen. Bitte 7-Zip installieren (https://www.7-zip.org) und das Skript erneut starten." }
# Names the exact build, so release notes can point to its source (GPL).
"$($release.tag_name) $($asset.name) $($asset.browser_download_url)" | Set-Content (Join-Path $target 'VERSION.txt')
Write-Host "libmpv-2.dll -> $target"
