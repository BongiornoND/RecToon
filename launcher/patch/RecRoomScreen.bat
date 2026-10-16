@echo off
setlocal
cd /d "%~dp0"
if not exist "Injector.exe" (
    echo Injector.exe is missing. Extract all files from the patch ZIP.
    pause
    exit /b 1
)
"%~dp0Injector.exe" --check-install
if errorlevel 1 (
    pause
    exit /b 1
)
set "PATCH26_GAME_DIR=%~dp0"
powershell.exe -NoLogo -NoProfile -NonInteractive -Command "$ErrorActionPreference = 'Stop'; $injectorStart = New-Object System.Diagnostics.ProcessStartInfo; $injectorStart.FileName = Join-Path $env:PATCH26_GAME_DIR 'Injector.exe'; $injectorStart.WorkingDirectory = $env:PATCH26_GAME_DIR; $injectorStart.UseShellExecute = $false; $injectorStart.CreateNoWindow = $true; [System.Diagnostics.Process]::Start($injectorStart).Dispose()"
if errorlevel 1 (
    echo Injector.exe could not start in the background. Run it manually for diagnostics.
    pause
    exit /b 1
)
"%~dp0Injector.exe" --wait-ready
if errorlevel 1 (
    echo Run Injector.exe manually to view its diagnostics.
    pause
    exit /b 1
)
start "" "%~dp0RecRoom.exe" +forcemode:screen
