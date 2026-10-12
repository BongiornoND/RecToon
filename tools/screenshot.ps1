# Capture the Rec Room game window to logs\screen.png (works even if other windows overlap it).
param([string]$Out = (Join-Path (Split-Path $PSScriptRoot -Parent) "logs\screen.png"), [int]$ProcId = 0, [string]$ProcessName = "RecRoom")

Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class Win {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
}
"@
[void][Win]::SetProcessDPIAware()  # physical pixels, so the capture isn't cropped by display scaling

$p = Get-Process $ProcessName -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 -and ($ProcId -eq 0 -or $_.Id -eq $ProcId) } | Select-Object -First 1
if (-not $p) { "game window not found"; exit 1 }
$r = New-Object Win+RECT
[void][Win]::GetWindowRect($p.MainWindowHandle, [ref]$r)
$bmp = New-Object System.Drawing.Bitmap ($r.R - $r.L), ($r.B - $r.T)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc()
[void][Win]::PrintWindow($p.MainWindowHandle, $hdc, 2)  # PW_RENDERFULLCONTENT
$g.ReleaseHdc($hdc)
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
$Out
