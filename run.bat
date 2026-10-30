@echo off
set "DOTNET_ROOT=%LocalAppData%\dotnet"
echo Starting Aia Dinamic for Windows (Top Docked Notch)...
start "" "%~dp0publish\AiaDinamic.exe"
