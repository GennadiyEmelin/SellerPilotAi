$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$backend = Join-Path $root "server"
$frontend = Join-Path $root "client"

Start-Process powershell -ArgumentList @(
  "-NoExit",
  "-ExecutionPolicy", "Bypass",
  "-Command",
  "Set-Location '$backend'; dotnet run --no-restore --urls http://localhost:5106"
)

Start-Process powershell -ArgumentList @(
  "-NoExit",
  "-ExecutionPolicy", "Bypass",
  "-Command",
  "Set-Location '$frontend'; npm run dev"
)

Write-Host "SellerPilot is starting."
Write-Host "Frontend: http://127.0.0.1:5173"
Write-Host "Backend:  http://localhost:5106/api/health"
