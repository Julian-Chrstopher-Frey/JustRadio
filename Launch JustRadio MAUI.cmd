@echo off
setlocal
cd /d "%~dp0"
dotnet build ".\src\RadioBloom.Maui\RadioBloom.Maui.csproj" -f net10.0-windows10.0.19041.0 -t:Run
endlocal
