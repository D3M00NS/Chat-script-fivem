# PowerShell / scripts/build.ps1 / PowerShell 5.1+ and .NET 8 SDK / Windows 10 x64 executable.
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Push-Location $projectRoot
try {
    & dotnet publish src/ZyonMenu.Windows/ZyonMenu.Windows.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:TreatWarningsAsErrors=true -p:RequireClientModuleAsset=true -o dist/win-x64
    if ($LASTEXITCODE -ne 0) { throw "A compilação falhou com código $LASTEXITCODE." }
    & dotnet publish src/ZyonMenu.Windows/ZyonMenu.Windows.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=None -p:TreatWarningsAsErrors=true -p:RequireClientModuleAsset=true -o dist/compacto
    if ($LASTEXITCODE -ne 0) { throw "A compilação compacta falhou com código $LASTEXITCODE." }
    Get-FileHash dist/win-x64/ZyonMenu.exe -Algorithm SHA256
} finally {
    Pop-Location
}
