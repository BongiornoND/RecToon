# Press a key in the Rec Room window. -Key is a virtual-key name: Tab, Escape, Enter, A..Z, D0..D9.
param([Parameter(Mandatory)][string]$Key, [int]$HoldMs = 80, [int]$ProcId = 0)

Add-Type -AssemblyName System.Windows.Forms
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class WinKey {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern uint MapVirtualKey(uint code, uint mapType);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
}
"@

$p = Get-Process RecRoom -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 -and ($ProcId -eq 0 -or $_.Id -eq $ProcId) } | Select-Object -First 1
if (-not $p) { "game window not found"; exit 1 }
[void][WinKey]::SetForegroundWindow($p.MainWindowHandle)
Start-Sleep -Milliseconds 300
$vk = [byte][System.Windows.Forms.Keys]::$Key
$scan = [byte][WinKey]::MapVirtualKey($vk, 0)
[WinKey]::keybd_event($vk, $scan, 0, [UIntPtr]::Zero)      # key down
Start-Sleep -Milliseconds $HoldMs
[WinKey]::keybd_event($vk, $scan, 2, [UIntPtr]::Zero)      # key up
"pressed $Key"
