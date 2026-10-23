# Builds dist\Lemon-Setup.exe: a per-user installer (no admin prompt) with Start menu shortcut, uninstaller,
# and optional right-click menu and start-with-Windows. Needs the .NET 10 SDK and
# Inno Setup 6 (winget install JRSoftware.InnoSetup).
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$iscc = @((Get-Command iscc -ErrorAction SilentlyContinue).Source,
          "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
          "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe") | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 not found. Install it with: winget install JRSoftware.InnoSetup' }

$version = ([xml](Get-Content src\Lemon.App\Lemon.App.csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1

& .\make-app.ps1
if ($LASTEXITCODE) { exit $LASTEXITCODE }

# Unsigned: SmartScreen warns on other PCs until Setup and Lemon.exe are code-signed.
& $iscc /Qp "/DAppVersion=$version" installer\Lemon.iss
if ($LASTEXITCODE) { exit $LASTEXITCODE }
Write-Host "Built $PSScriptRoot\dist\Lemon-Setup.exe"
