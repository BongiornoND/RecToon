@echo off
rem Opens the Rec Room offline launcher (host a server or join one by address).
start "" powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "%~dp0launcher\Launcher.ps1"
