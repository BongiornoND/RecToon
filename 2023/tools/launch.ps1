# Launch the patched build in desktop mode, skipping the EasyAntiCheat launcher.
# -Second starts another client alongside the running one (multiplayer testing).
param([switch]$Second)
$root = Split-Path $PSScriptRoot -Parent
$work = Join-Path $root "work"
$logDir = Join-Path $root "logs"
New-Item -ItemType Directory -Force $logDir | Out-Null
$tag = if ($Second) { "player2" } else { "player" }
$log = Join-Path $logDir "$tag-$(Get-Date -Format yyyyMMdd-HHmmss).log"

if (-not $Second) { Get-Process RecRoom -ErrorAction SilentlyContinue | Stop-Process -Force -Confirm:$false }
$p = Start-Process -FilePath (Join-Path $work "RecRoom.exe") -WorkingDirectory $work -PassThru -ArgumentList @(
    '-screen-fullscreen', '0', '-screen-width', '1280', '-screen-height', '720',
    '-vrmode', 'None', '-logFile', "`"$log`""
)
"pid $($p.Id)"
$log
