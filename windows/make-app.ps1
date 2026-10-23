# Publishes a self-contained Lemon into dist\app (no .NET or Windows App SDK install needed on the target PC).
# Used by make-installer.ps1 and make-portable.ps1. Needs the .NET 10 SDK.
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

if (Test-Path dist\app) { Remove-Item -Recurse -Force dist\app }
# A plain folder rather than a single-file exe: WinUI needs its .pri resource files on disk, and the
# installer compresses a folder better anyway.
dotnet publish src\Lemon.App\Lemon.App.csproj -c Release -r win-x64 --self-contained -p:Platform=x64 -p:DebugType=none -o dist\app
if ($LASTEXITCODE) { exit $LASTEXITCODE }
Write-Host "Built $PSScriptRoot\dist\app"
