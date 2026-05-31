@echo off
cd /d "%~dp0"
dotnet run --no-restore --project server --urls http://localhost:5106
pause
