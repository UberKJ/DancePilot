param(
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$dotnet = Join-Path $env:TEMP "codex-dotnet-sdk-9\dotnet.exe"
$exe = Join-Path $projectRoot "DancePilot.UI\bin\x64\Debug\net9.0-windows10.0.19041.0\win-x64\DancePilot.UI.exe"

Get-Process DancePilot.UI -ErrorAction SilentlyContinue | Stop-Process -Force

if (-not $NoBuild) {
    & $dotnet build (Join-Path $projectRoot "DancePilot.sln") -c Debug -p:Platform=x64
}

Start-Process -FilePath $exe
