# Click inside the Rec Room window. X/Y are pixels in logs\screen.png (window-relative).
param([Parameter(Mandatory)][int]$X, [Parameter(Mandatory)][int]$Y, [int]$ProcId = 0, [string]$ProcessName = "RecRoom")

Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class WinClick {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, UIntPtr e);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
}
"@
[void][WinClick]::SetProcessDPIAware()

$p = Get-Process $ProcessName -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 -and ($ProcId -eq 0 -or $_.Id -eq $ProcId) } | Select-Object -First 1
if (-not $p) { "game window not found"; exit 1 }
$r = New-Object WinClick+RECT
[void][WinClick]::GetWindowRect($p.MainWindowHandle, [ref]$r)
[void][WinClick]::SetForegroundWindow($p.MainWindowHandle)
Start-Sleep -Milliseconds 300
[void][WinClick]::SetCursorPos($r.L + $X, $r.T + $Y)
Start-Sleep -Milliseconds 100
[WinClick]::mouse_event(0x2, 0, 0, 0, [UIntPtr]::Zero)  # left down
Start-Sleep -Milliseconds 80
[WinClick]::mouse_event(0x4, 0, 0, 0, [UIntPtr]::Zero)  # left up
"clicked ($X, $Y)"
