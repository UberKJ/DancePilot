$ErrorActionPreference = "Stop"

Get-Process DancePilot.UI -ErrorAction SilentlyContinue | Stop-Process -Force
Write-Host "Stopped DancePilot if it was running."
