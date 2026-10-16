# Rec Room (build 20231207) offline launcher: host a local server and play, or join a host by address.
#
#   Host & Play  starts the REST + Photon servers on this PC (needs Node.js), points the game at
#                localhost and launches it. Friends join with one of the addresses shown.
#   Join & Play  points the game at the host's address and launches it (needs only Windows + Steam).
#
# Headless use: Launcher.ps1 -Patch <host>   (point the game at <host> and exit)
param([string]$Patch)

$ErrorActionPreference = "Stop"
$Root = Split-Path $PSScriptRoot -Parent
$Here = $PSScriptRoot
$Work = Join-Path $Root "work"
$Game = Join-Path $Work "RecRoom.exe"
$Meta = Join-Path $Work "RecRoom_Data\il2cpp_data\Metadata\global-metadata.dat"
$Pristine = Join-Path $Here "global-metadata.pristine.dat"
if (-not (Test-Path $Pristine)) { $Pristine = Join-Path $Root "original\RecRoom_Data\il2cpp_data\Metadata\global-metadata.dat" }
$Logs = Join-Path $Root "logs"
$SettingsFile = Join-Path $Here "settings.json"
$ServerPorts = @{ "REST" = 9443; "Photon" = 9093 }

Add-Type -Path (Join-Path $Here "MetaPatcher.cs")

function Write-Log([string]$message) {
    try {
        New-Item -ItemType Directory -Force $Logs | Out-Null
        Add-Content -Path (Join-Path $Logs "launcher.log") -Value "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') $message"
    } catch {}
}

function Set-ServerHost([string]$address) {
    $n = [MetaPatcher]::Patch($Pristine, $Meta, $address)
    Write-Log "game pointed at $address ($n literals)"
}

if ($Patch) { Set-ServerHost $Patch; "game now points at $Patch"; exit 0 }

# --- helpers ---------------------------------------------------------------------------------
function Get-Settings {
    try { return Get-Content $SettingsFile -Raw | ConvertFrom-Json } catch { return [pscustomobject]@{ LastHost = ""; VR = $false } }
}
function Save-Settings($s) { try { $s | ConvertTo-Json | Set-Content -Encoding utf8 $SettingsFile } catch {} }

function Test-Listening([int]$port) {
    [bool](Get-NetTCPConnection -State Listen -LocalPort $port -ErrorAction SilentlyContinue)
}
function Test-ServerRunning { (Test-Listening 9443) -and (Test-Listening 9093) }

function Test-Reachable([string]$address, [int]$port, [int]$timeoutMs = 3000) {
    $client = New-Object System.Net.Sockets.TcpClient
    try {
        $task = $client.ConnectAsync($address, $port)
        return ($task.Wait($timeoutMs) -and $client.Connected)
    } catch { return $false } finally { $client.Close() }
}

# Addresses friends can use to reach this PC (LAN and VPNs such as Tailscale/ZeroTier/Radmin).
function Get-HostAddresses {
    Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
        Where-Object { $_.IPAddress -notlike "127.*" -and $_.IPAddress -notlike "169.254.*" } |
        Sort-Object { if ($_.InterfaceAlias -match "Tailscale|ZeroTier|Radmin|Hamachi") { 1 } else { 0 } } |
        ForEach-Object {
            $kind = if ($_.InterfaceAlias -match "Tailscale|ZeroTier|Radmin|Hamachi") { "$($_.InterfaceAlias), over the internet" } else { "$($_.InterfaceAlias), same network" }
            [pscustomobject]@{ Address = $_.IPAddress; Kind = $kind }
        }
}

function Start-Servers {
    if (Test-ServerRunning) { return $true }
    $node = Get-Command node -ErrorAction SilentlyContinue
    if (-not $node) {
        [System.Windows.Forms.MessageBox]::Show("Hosting needs Node.js. Install it from nodejs.org, then try again.", "Rec Room", "OK", "Warning") | Out-Null
        return $false
    }
    New-Item -ItemType Directory -Force $Logs | Out-Null
    $pids = @()
    foreach ($s in @(@{ Name = "rest"; Script = "server\server.js"; Port = 9443 }, @{ Name = "photon"; Script = "photon\server.js"; Port = 9093 })) {
        if (Test-Listening $s.Port) { continue }
        $p = Start-Process -FilePath $node.Source -ArgumentList "`"$(Join-Path $Root $s.Script)`"" -WorkingDirectory $Root `
            -WindowStyle Hidden -PassThru `
            -RedirectStandardOutput (Join-Path $Logs "server-$($s.Name).log") -RedirectStandardError (Join-Path $Logs "server-$($s.Name).err.log")
        $pids += $p.Id
    }
    if ($pids) { $pids -join "," | Set-Content (Join-Path $Here "server.pids") }
    for ($i = 0; $i -lt 50 -and -not (Test-ServerRunning); $i++) { Start-Sleep -Milliseconds 200; [System.Windows.Forms.Application]::DoEvents() }
    return (Test-ServerRunning)
}

function Stop-Servers {
    $file = Join-Path $Here "server.pids"
    if (Test-Path $file) {
        foreach ($id in (Get-Content $file) -split ",") { if ($id) { Stop-Process -Id ([int]$id) -Force -ErrorAction SilentlyContinue } }
        Remove-Item $file -Force
    }
    # Servers started some other way: stop whichever node processes own the ports.
    foreach ($port in 9443, 9093, 9090, 9091) {
        Get-NetTCPConnection -State Listen -LocalPort $port -ErrorAction SilentlyContinue | ForEach-Object {
            $proc = Get-Process -Id $_.OwningProcess -ErrorAction SilentlyContinue
            if ($proc -and $proc.ProcessName -eq "node") { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
        }
    }
}

function Start-Game([string]$serverAddress, [bool]$vr) {
    if (Get-Process RecRoom -ErrorAction SilentlyContinue) {
        $answer = [System.Windows.Forms.MessageBox]::Show("Rec Room is already running. Close it and start again?", "Rec Room", "YesNo", "Question")
        if ($answer -ne "Yes") { return $false }
        Get-Process RecRoom | Stop-Process -Force
        Start-Sleep -Milliseconds 800
    }
    if (-not (Get-Process steam -ErrorAction SilentlyContinue)) {
        $answer = [System.Windows.Forms.MessageBox]::Show("Steam isn't running. Rec Room signs you in with your Steam account, so start Steam first.`n`nLaunch anyway?", "Rec Room", "YesNo", "Warning")
        if ($answer -ne "Yes") { return $false }
    }
    Set-ServerHost $serverAddress
    New-Item -ItemType Directory -Force $Logs | Out-Null
    $log = Join-Path $Logs "player-$(Get-Date -Format yyyyMMdd-HHmmss).log"
    $gameArgs = @("-logFile", "`"$log`"")
    if (-not $vr) { $gameArgs = @("-screen-fullscreen", "0", "-screen-width", "1280", "-screen-height", "720", "-vrmode", "None") + $gameArgs }
    $p = Start-Process -FilePath $Game -WorkingDirectory $Work -ArgumentList $gameArgs -PassThru
    Write-Log "started Rec Room (pid $($p.Id), VR=$vr), log $log"
    return $true
}

# --- UI --------------------------------------------------------------------------------------
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()
# Not DPI-aware on purpose: the fixed 96-dpi layout is scaled as a whole by Windows.
$Scale = 1

$Orange = [System.Drawing.Color]::FromArgb(242, 101, 34)
$OrangeDark = [System.Drawing.Color]::FromArgb(214, 78, 18)
$Cream = [System.Drawing.Color]::FromArgb(250, 243, 234)
$Ink = [System.Drawing.Color]::FromArgb(51, 41, 36)
$Muted = [System.Drawing.Color]::FromArgb(122, 108, 98)
$Font = { param($size, $style = "Regular") New-Object System.Drawing.Font("Segoe UI", $size, [System.Drawing.FontStyle]$style) }

$settings = Get-Settings
$form = New-Object System.Windows.Forms.Form
$form.Text = "Rec Room"
$form.ClientSize = New-Object System.Drawing.Size(840, 640)
$form.FormBorderStyle = "FixedSingle"
$form.MaximizeBox = $false
$form.StartPosition = "CenterScreen"
$form.BackColor = $Cream
$form.Font = & $Font 10
$form.AutoScaleMode = "None"
try { $form.Icon = New-Object System.Drawing.Icon (Join-Path $Here "assets\icon.ico") } catch {}

# Banner: Steam hero art cropped to fit, with the Rec Room logo over it.
$banner = New-Object System.Windows.Forms.PictureBox
$banner.SetBounds(0, 0, 840, 230)
$banner.SizeMode = "StretchImage"
try {
    $bmp = New-Object System.Drawing.Bitmap ([int](840 * $Scale)), ([int](230 * $Scale))
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.ScaleTransform($Scale, $Scale)
    $g.InterpolationMode = "HighQualityBicubic"
    $hero = [System.Drawing.Image]::FromFile((Join-Path $Here "assets\hero.jpg"))
    $scale = 840 / $hero.Width
    $g.DrawImage($hero, 0, [int]((230 - $hero.Height * $scale) / 2), 840, [int]($hero.Height * $scale))
    $shade = New-Object System.Drawing.Drawing2D.LinearGradientBrush((New-Object System.Drawing.Point(0, 0)), (New-Object System.Drawing.Point(520, 0)), [System.Drawing.Color]::FromArgb(170, 30, 20, 15), [System.Drawing.Color]::FromArgb(0, 30, 20, 15))
    $g.FillRectangle($shade, 0, 0, 840, 230)
    $logo = [System.Drawing.Image]::FromFile((Join-Path $Here "assets\logo.png"))
    $g.DrawImage($logo, 18, 8, 300, 169)
    $g.TextRenderingHint = "AntiAliasGridFit"
    $g.DrawString("Offline edition  -  build 20231207", (& $Font 11 "Bold"), [System.Drawing.Brushes]::White, 34, 188)
    $g.Dispose()
    $banner.Image = $bmp
} catch { $banner.BackColor = $Orange }
$form.Controls.Add($banner)

function New-Card($x, $title) {
    $card = New-Object System.Windows.Forms.Panel
    $card.SetBounds($x, 250, 390, 318)
    $card.BackColor = [System.Drawing.Color]::White
    $label = New-Object System.Windows.Forms.Label
    $label.Text = $title; $label.Font = & $Font 15 "Bold"; $label.ForeColor = $Ink
    $label.SetBounds(20, 16, 350, 34)
    $card.Controls.Add($label)
    $form.Controls.Add($card)
    return $card
}
function New-Text($parent, $text, $x, $y, $w, $h, $color = $Muted, $size = 10) {
    $l = New-Object System.Windows.Forms.Label
    $l.Text = $text; $l.ForeColor = $color; $l.Font = & $Font $size
    $l.SetBounds($x, $y, $w, $h)
    $parent.Controls.Add($l)
    return $l
}
function New-BigButton($parent, $text, $y) {
    $b = New-Object System.Windows.Forms.Button
    $b.Text = $text; $b.Font = & $Font 13 "Bold"
    $b.ForeColor = [System.Drawing.Color]::White; $b.BackColor = $Orange
    $b.FlatStyle = "Flat"; $b.FlatAppearance.BorderSize = 0
    $b.FlatAppearance.MouseOverBackColor = $OrangeDark
    $b.Cursor = [System.Windows.Forms.Cursors]::Hand
    $b.SetBounds(20, $y, 350, 52)
    $parent.Controls.Add($b)
    return $b
}

# Host card
$hostCard = New-Card 20 "Host a game"
New-Text $hostCard "Runs the server on this PC and you play too. Friends join with one of these addresses:" 20 54 350 42 | Out-Null
$y = 100
$addresses = @(Get-HostAddresses)
if (-not $addresses) { New-Text $hostCard "No network connection found." 20 $y 350 22 $Ink | Out-Null; $y += 28 }
foreach ($a in $addresses | Select-Object -First 3) {
    New-Text $hostCard $a.Address 20 $y 160 22 $Ink 11 | Out-Null
    New-Text $hostCard $a.Kind 20 ($y + 21) 250 18 $Muted 8.5 | Out-Null
    $copy = New-Object System.Windows.Forms.LinkLabel
    $copy.Text = "Copy"; $copy.LinkColor = $OrangeDark; $copy.Tag = $a.Address
    $copy.SetBounds(320, $y + 2, 50, 20)
    $copy.Add_LinkClicked({ param($s) [System.Windows.Forms.Clipboard]::SetText($s.Tag); $script:status.Text = "Copied $($s.Tag)" })
    $hostCard.Controls.Add($copy)
    $y += 46
}
$serverState = New-Text $hostCard "" 20 210 230 22 $Muted 9.5
$stopLink = New-Object System.Windows.Forms.LinkLabel
$stopLink.Text = "Stop server"; $stopLink.LinkColor = $OrangeDark
$stopLink.SetBounds(285, 210, 90, 22)
$hostCard.Controls.Add($stopLink)
$hostButton = New-BigButton $hostCard "HOST && PLAY" 246

# Join card
$joinCard = New-Card 430 "Join a friend"
New-Text $joinCard "Type the address your host gave you. They need to be hosting first." 20 54 350 42 | Out-Null
New-Text $joinCard "Host address" 20 104 200 20 $Ink 9.5 | Out-Null
$addressBox = New-Object System.Windows.Forms.TextBox
$addressBox.Font = & $Font 14
$addressBox.SetBounds(20, 126, 350, 34)
$addressBox.Text = $settings.LastHost
$joinCard.Controls.Add($addressBox)
New-Text $joinCard "For example 192.168.1.20, or a Tailscale address like 100.64.0.5 to play over the internet." 20 168 350 40 $Muted 8.5 | Out-Null
$joinButton = New-BigButton $joinCard "JOIN && PLAY" 246

# Footer
$vrBox = New-Object System.Windows.Forms.CheckBox
$vrBox.Text = "Play in VR (SteamVR)"; $vrBox.ForeColor = $Ink
$vrBox.Checked = [bool]$settings.VR
$vrBox.SetBounds(22, 584, 200, 24)
$form.Controls.Add($vrBox)
$script:status = New-Text $form "" 230 586 480 22 $Muted 9.5
$logsLink = New-Object System.Windows.Forms.LinkLabel
$logsLink.Text = "Logs"; $logsLink.LinkColor = $OrangeDark; $logsLink.TextAlign = "MiddleRight"
$logsLink.SetBounds(740, 586, 80, 22)
$logsLink.Add_LinkClicked({ New-Item -ItemType Directory -Force $Logs | Out-Null; Start-Process explorer.exe $Logs })
$form.Controls.Add($logsLink)

function Update-ServerState {
    if (Test-ServerRunning) {
        $serverState.Text = [char]0x25CF + " Server running on this PC"; $serverState.ForeColor = [System.Drawing.Color]::FromArgb(46, 140, 70)
        $stopLink.Visible = $true
    } else {
        $serverState.Text = [char]0x25CB + " Server not running"; $serverState.ForeColor = $Muted
        $stopLink.Visible = $false
    }
}
Update-ServerState

function Set-Busy([bool]$busy) {
    $hostButton.Enabled = -not $busy; $joinButton.Enabled = -not $busy
    $form.Cursor = if ($busy) { [System.Windows.Forms.Cursors]::WaitCursor } else { [System.Windows.Forms.Cursors]::Default }
    [System.Windows.Forms.Application]::DoEvents()
}

$hostButton.Add_Click({
    Write-Log "Host & Play clicked"
    Set-Busy $true
    try {
        $script:status.Text = "Starting the server..."
        [System.Windows.Forms.Application]::DoEvents()
        if (-not (Start-Servers)) {
            $script:status.Text = "The server didn't start. See logs\server-*.log."
            return
        }
        Update-ServerState
        $script:status.Text = "Starting Rec Room..."
        [System.Windows.Forms.Application]::DoEvents()
        if (Start-Game "localhost" $vrBox.Checked) {
            $script:status.Text = "Rec Room is starting. Windows may ask to allow Node.js on your network: allow it so friends can join."
        } else { $script:status.Text = "" }
    } catch {
        $script:status.Text = "Error: $($_.Exception.Message)"
        Write-Log "error: $($_ | Out-String)"
    } finally { Set-Busy $false }
})

$joinButton.Add_Click({
    Write-Log "Join & Play clicked: '$($addressBox.Text)'"
    $address = $addressBox.Text.Trim() -replace "^https?://", "" -replace "[:/].*$", ""
    if ($address -notmatch "^[A-Za-z0-9][A-Za-z0-9.\-]*$") {
        $script:status.Text = "Type the host's address first (like 192.168.1.20)."
        return
    }
    Set-Busy $true
    try {
        $script:status.Text = "Checking $address..."
        [System.Windows.Forms.Application]::DoEvents()
        foreach ($name in "REST", "Photon") {
            if (-not (Test-Reachable $address $ServerPorts[$name])) {
                $script:status.Text = "Can't reach $address (port $($ServerPorts[$name]))."
                Write-Log "can't reach $($address):$($ServerPorts[$name])"
                [System.Windows.Forms.MessageBox]::Show(
                    "Couldn't connect to $address on port $($ServerPorts[$name]).`n`nCheck that:`n" +
                    "  - your host clicked Host & Play and is still running it`n" +
                    "  - the address is right (same Wi-Fi, or both on the same Tailscale network)`n" +
                    "  - the host allowed Node.js through Windows Firewall",
                    "Rec Room", "OK", "Warning") | Out-Null
                return
            }
        }
        $settings.LastHost = $address; $settings.VR = $vrBox.Checked; Save-Settings $settings
        $script:status.Text = "Starting Rec Room..."
        [System.Windows.Forms.Application]::DoEvents()
        if (Start-Game $address $vrBox.Checked) { $script:status.Text = "Rec Room is starting, connected to $address." } else { $script:status.Text = "" }
    } catch {
        $script:status.Text = "Error: $($_.Exception.Message)"
        Write-Log "error: $($_ | Out-String)"
    } finally { Set-Busy $false }
})

$stopLink.Add_LinkClicked({
    $answer = [System.Windows.Forms.MessageBox]::Show("Stop the server? Anyone playing on it will be disconnected.", "Rec Room", "YesNo", "Question")
    if ($answer -eq "Yes") { Stop-Servers; Start-Sleep -Milliseconds 500; Update-ServerState; $script:status.Text = "Server stopped." }
})
$vrBox.Add_CheckedChanged({ $settings.VR = $vrBox.Checked; Save-Settings $settings })

[void]$form.ShowDialog()
