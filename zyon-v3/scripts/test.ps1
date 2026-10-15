# PowerShell / scripts/test.ps1 / .NET 8 SDK, Lua 5.4 and Node / all portable checks.
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Push-Location $projectRoot
try {
    & dotnet run --project tests/ZyonMenu.Standalone.Tests/ZyonMenu.Standalone.Tests.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw "Os testes falharam com código $LASTEXITCODE." }
    & dotnet run --project tests/ZyonMenu.Core.Tests/ZyonMenu.Core.Tests.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw "Os testes HTTP falharam com código $LASTEXITCODE." }
    foreach ($test in @('admin_server.lua', 'admin_client.lua', 'admin_medical.lua', 'admin_pairing_client.lua')) {
        & lua "tests/$test"
        if ($LASTEXITCODE -ne 0) { throw "O teste $test falhou com código $LASTEXITCODE." }
    }
    & node --test tests/admin_nui_pairing.test.mjs
    if ($LASTEXITCODE -ne 0) { throw "Os testes NUI falharam com código $LASTEXITCODE." }
} finally {
    Pop-Location
}
