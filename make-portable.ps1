# Builds dist\Lemon.exe: the whole app in one self-contained file, nothing to install. Needs the .NET 10 SDK.
# WinUI needs its DLLs and .pri resources on disk, so the first launch unpacks them to %TEMP%\.net\Lemon
# (about 2 seconds); later launches start straight away. Settings live in %LOCALAPPDATA%\Lemon, as when installed.
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

# Unsigned: SmartScreen warns on other PCs until it's signed with a code-signing certificate.
dotnet publish src\Lemon.App\Lemon.App.csproj -c Release -r win-x64 --self-contained -p:Platform=x64 -p:DebugType=none `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:IncludeAllContentForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true -o dist
if ($LASTEXITCODE) { exit $LASTEXITCODE }
Write-Host "Built $PSScriptRoot\dist\Lemon.exe"
