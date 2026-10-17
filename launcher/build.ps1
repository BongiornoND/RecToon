param([string]$OutFile, [string]$PackageIndex)
$ErrorActionPreference = 'Stop'
$source = Join-Path $PSScriptRoot 'RecRoomLauncher.cs'
$installer = Join-Path $PSScriptRoot 'Installer.cs'
$setupView = Join-Path $PSScriptRoot 'SetupView.cs'
$serverAddons = Join-Path $PSScriptRoot 'ServerAddons.cs'
$serverSettings = Join-Path $PSScriptRoot 'ServerSettingsDialog.cs'
$probe = Join-Path $PSScriptRoot 'probe-server.js'
$cdnProbe = Join-Path $PSScriptRoot 'probe-cdn.js'
$hostsHelper = Join-Path $PSScriptRoot 'LocalCdnHosts.cs'
$manifest = Join-Path $PSScriptRoot 'app.manifest'
$assets = Join-Path $PSScriptRoot 'assets'
$icon = Join-Path $assets 'launcher.ico'
$buildManifest = Join-Path $assets 'build-20260323.14.manifest'
$referee = Join-Path $PSScriptRoot 'referee'
$output = Join-Path (Split-Path $PSScriptRoot -Parent) 'dist\RecToon-Launcher.exe'
if ($OutFile) { $output = $OutFile }
$output = [IO.Path]::GetFullPath($output)
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw "C# compiler not found: $compiler" }
if (!(Test-Path -LiteralPath $buildManifest)) { throw "Missing $buildManifest (run tools\make_build_manifest.py)" }
# The server package list (file names, sizes, SHA-256) published with the GitHub release.
if (!$PackageIndex) { $PackageIndex = Join-Path $PSScriptRoot 'server-packages.json' }
$PackageIndex = [IO.Path]::GetFullPath($PackageIndex)
if (!(Test-Path -LiteralPath $PackageIndex)) { throw "Missing server package index: $PackageIndex" }
New-Item -ItemType Directory -Force -Path (Split-Path $output -Parent) | Out-Null

# The Referee patch payload (community Referee.dll, its bootstrap, the private patch core and the
# 2026patch.ini template) plus their SHA-256 list, which setup checks before writing anything into
# a game folder. See referee/README.md for where the DLLs come from.
$patchFiles = 'Referee.dll', 'RefereeBootstrap.dll', 'CommunityPatchCore.dll', '2026patch.ini'
$obj = Join-Path $PSScriptRoot 'obj'
New-Item -ItemType Directory -Force -Path $obj | Out-Null
$sums = Join-Path $obj 'referee.SHA256SUMS'
$lines = foreach ($name in $patchFiles) {
    $file = Join-Path $referee $name
    if (!(Test-Path -LiteralPath $file)) { throw "Missing patch payload file: $file" }
    '{0} *{1}' -f (Get-FileHash -Algorithm SHA256 -LiteralPath $file).Hash.ToLowerInvariant(), $name
}
[IO.File]::WriteAllLines($sums, $lines, (New-Object Text.ASCIIEncoding))

$resources = @(
    "/resource:$probe,RecRoomLauncher.probe-server.js",
    "/resource:$cdnProbe,RecRoomLauncher.probe-cdn.js",
    "/resource:$buildManifest,RecRoomLauncher.build-20260323.14.manifest",
    "/resource:$sums,RecRoomLauncher.referee.SHA256SUMS",
    "/resource:$PackageIndex,RecRoomLauncher.server-packages.json"
)
foreach ($name in $patchFiles) { $resources += "/resource:$(Join-Path $referee $name),RecRoomLauncher.referee.$name" }
foreach ($name in 'banner.jpg', 'logo-full.png', 'logo-squircle.png', 'launcher.ico') {
    $resources += "/resource:$(Join-Path $assets $name),RecRoomLauncher.$name"
}
# Optional: drop Ubuntu-Regular.ttf / Ubuntu-Medium.ttf / Ubuntu-Bold.ttf into fonts\ to embed the design's typeface.
$fonts = Join-Path $PSScriptRoot 'fonts'
if (Test-Path -LiteralPath $fonts) {
    foreach ($font in Get-ChildItem -LiteralPath $fonts -Filter '*.ttf') {
        $resources += "/resource:$($font.FullName),RecRoomLauncher.fonts.$($font.Name)"
    }
}
& $compiler /nologo /target:winexe /optimize+ /platform:anycpu `
    "/out:$output" "/win32manifest:$manifest" "/win32icon:$icon" @resources `
    /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll /reference:System.Net.Http.dll `
    /reference:System.Web.Extensions.dll /reference:System.Xml.dll `
    /reference:System.ServiceProcess.dll /reference:System.IO.Compression.dll `
    /reference:System.IO.Compression.FileSystem.dll `
    $source $installer $setupView $hostsHelper $serverAddons $serverSettings
if ($LASTEXITCODE -ne 0) { throw "Launcher build failed with exit code $LASTEXITCODE" }
Write-Host "Built $output"
