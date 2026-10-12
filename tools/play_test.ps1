# Wait for the newest game log to reach the title screen, press PLAY, wait,
# then screenshot and list non-200 requests made after the click.
param([int]$WaitAfter = 25)
$root = Split-Path $PSScriptRoot -Parent
$log = Get-ChildItem "$root\logs\player-*.log" | Sort-Object LastWriteTime | Select-Object -Last 1

for ($i = 0; $i -lt 45; $i++) {
    if (Select-String -Path $log.FullName -Pattern "Account Info:|Login failed|Application.quit" -Quiet) { break }
    Start-Sleep -Seconds 2
}
if (Select-String -Path $log.FullName -Pattern "Login failed" -Quiet) {
    "LOGIN FAILED:"; Select-String -Path $log.FullName -Pattern "Login failed" | Select-Object -First 1 | ForEach-Object Line
}
Start-Sleep -Seconds 10
$clickedAt = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss")
& "$PSScriptRoot\click.ps1" -X 923 -Y 472 | Out-Null
Start-Sleep -Seconds 2
& "$PSScriptRoot\click.ps1" -X 923 -Y 472 | Out-Null
Start-Sleep -Seconds $WaitAfter

if (Get-Process RecRoom -ErrorAction SilentlyContinue) { & "$PSScriptRoot\screenshot.ps1" | Out-Null; "screenshot: logs\screen.png" } else { "GAME EXITED" }
"non-200 requests since PLAY:"
Get-Content "$root\server\requests.jsonl" | ForEach-Object { $_ | ConvertFrom-Json } |
    Where-Object { $_.t -gt $clickedAt -and $_.status -ne 200 } |
    Group-Object { "$($_.status) $($_.method) $(($_.url -split '\?')[0])" } |
    ForEach-Object { "{0,4}x {1}" -f $_.Count, $_.Name }
