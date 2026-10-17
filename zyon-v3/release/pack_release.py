# Python 3 / package verified local Windows publishes and source.
from pathlib import Path
import hashlib
import json
import os
import zipfile
import xml.etree.ElementTree as ET

PROJECT = Path(__file__).resolve().parents[1]
VERSION = ET.parse(PROJECT / "src/ZyonMenu.Windows/ZyonMenu.Windows.csproj").findtext(".//Version")
if not VERSION:
    raise RuntimeError("Application version is missing.")
PREFIX = f"ZyonMenu-v{VERSION}"
OUTPUT = PROJECT.parent / "downloads"
OUTPUT.mkdir(parents=True, exist_ok=True)
source_files = [PROJECT / name for name in ("README.md", "global.json", ".gitignore", "Directory.Build.props")]
for part in ("src", "tests", "scripts", "docs", "release", "fivem", "native"):
    source_files.extend(path for path in (PROJECT / part).rglob("*")
                        if path.is_file() and not {"bin", "obj", "__pycache__", "dist", "build", ".build", ".zig-cache"}.intersection(path.parts))
nuget = Path(os.environ.get("NUGET_PACKAGES", PROJECT / ".build/nuget"))
dotnet_root = Path(os.environ.get("ZYON_DOTNET_ROOT", PROJECT / "tools/dotnet"))
zig_root = Path(os.environ.get("ZYON_ZIG_ROOT", PROJECT / "tools/zig"))
native_module = PROJECT / "native/zyon-client/dist/ZyonClient.asi"
if not native_module.is_file() or native_module.read_bytes()[:2] != b"MZ":
    raise RuntimeError("Build and verify the native Windows client module before packaging.")
license_files = {
    "licencas/DotNet-LICENSE.txt": nuget / "microsoft.netcore.app.runtime.win-x64/8.0.32/LICENSE.TXT",
    "licencas/WindowsDesktop-LICENSE.txt": nuget / "microsoft.windowsdesktop.app.runtime.win-x64/8.0.32/LICENSE",
    "licencas/ThirdPartyNotices.txt": dotnet_root / "ThirdPartyNotices.txt",
    "licencas/native/Zig-LICENSE.txt": zig_root / "LICENSE",
    "licencas/native/libcxx-LICENSE.txt": zig_root / "lib/libcxx/LICENSE.TXT",
    "licencas/native/libcxxabi-LICENSE.txt": zig_root / "lib/libcxxabi/LICENSE.TXT",
    "licencas/native/libunwind-LICENSE.txt": zig_root / "lib/libunwind/LICENSE.TXT",
    "licencas/native/mingw-COPYING.txt": zig_root / "lib/libc/mingw/COPYING",
}
validation_path = PROJECT / ".build/validation.json"
validation = (json.loads(validation_path.read_text()) if validation_path.is_file() else {
    "automatedChecks": "not-recorded-for-this-build",
    "windowsRuntime": "not-run", "fiveMRuntime": "not-run",
})
if validation_path.is_file() and validation.get("version") != VERSION:
    raise RuntimeError("Validation record belongs to a different application version.")
results = []
for flavor, folder in (("completo", "win-x64"), ("compacto", "compacto")):
    binary_files = sorted(path for path in (PROJECT / "dist" / folder).iterdir() if path.is_file())
    executable = PROJECT / "dist" / folder / "ZyonMenu.exe"
    if not executable.is_file():
        raise FileNotFoundError(executable)
    if flavor == "completo" and [path.name for path in binary_files] != ["ZyonMenu.exe"]:
        raise RuntimeError("Complete package must use a single locally published executable.")
    manifest = {
        "product": "Zyon client menu with optional local ASI module",
        "version": VERSION,
        "target": "Windows 10 x64 / FiveM b3258",
        "releaseStage": "Windows and FiveM gameplay validation pending",
        "runtimeIncluded": flavor == "completo",
        "windowsRuntimeTested": False,
        "fiveMRuntimeTested": False,
        "serverInstallationRequired": False,
        "defaultBackend": "client-module",
        "externalOnlyArgument": "--external",
        "implementedExternalFunctions": ["aim", "esp", "godMode", "noRecoil", "noSpread", "suicideHealthWrite"],
        "implementedClientModuleFunctions": ["godMode", "noclip", "invisibility", "freezeAmmo", "nativeRevive", "suicide", "lockVehicle", "unlockVehicle"],
        "experimentalClientModuleFunctions": ["directedNativeFire"],
        "clientModuleProtocolVersion": 2,
        "directedNativeFireRedirectsOriginalProjectile": False,
        "vehicleLocksGrantFrameworkKeys": False,
        "clientModuleRequirements": ["local FiveM.app/plugins module", "game build 3258", "server already permits ScriptHook", "pure level below 2"],
        "nativeReviveResetsMedicalFrameworkState": False,
        "nativeModule": {"file": "ZyonClient.asi", "embeddedInExecutable": True,
                         "bytes": native_module.stat().st_size,
                         "sha256": hashlib.sha256(native_module.read_bytes()).hexdigest()},
        "implementationDoesNotEstablishGameplayValidation": True,
        "unavailableFunctions": ["originalProjectileSilentAim", "frameworkMedicalRevive", "muteReceivedVoice", "adminEsp", "playerNames", "espLineOfSight"],
        "verification": validation,
        "binaries": [{"file": path.name, "bytes": path.stat().st_size,
                      "sha256": hashlib.sha256(path.read_bytes()).hexdigest()} for path in binary_files],
    }
    runtime_note = ("Runtime incluido: abra ZyonMenu.exe.\n" if flavor == "completo" else
                    "Requer .NET Desktop Runtime 8 x64: https://dotnet.microsoft.com/download/dotnet/8.0\n")
    quick_start = (
        f"ZYON {VERSION} - EXECUTAVEL EXTERNO / SEM RECURSO NO SERVIDOR\n\n"
        "1. Extraia o ZIP completo para pasta nova e feche o menu anterior.\n"
        "2. Com o FiveM fechado, abra ZyonMenu.exe. O modo inicial e CLIENTE LOCAL.\n"
        "3. Na engrenagem/Conexao, use Preparar modulo local e selecione FiveM.app da sua instalacao.\n"
        "4. Abra o FiveM novamente e entre no servidor. Aguarde a confirmacao do modulo e das capacidades.\n"
        "5. Noclip usa W/A/S/D, Espaco, Ctrl e Shift; velocidade1-50m/s muda durante o voo.\n"
        "6. Veiculo: bloqueia/desbloqueia o carro ocupado ou mais proximo ate8m, se houver controle nativo.\n"
        "7. Silent Aim experimental: solte o gatilho, mire e pressione LMB+ativacao. Uma tentativa por pressionamento; somente jogadores.\n"
        "   Enquanto ativo, suprime a entrada normal de tiro inclusive sem alvo. Nao comprova redirecionamento do tiro original nem acerto.\n"
        "8. Apos testar, use Salvar diagnostico e envie o JSON com o resultado de cada funcao.\n\n"
        + runtime_note + "\n"
        "Nenhum arquivo e instalado no servidor. O modulo local so funciona onde plugins/ScriptHook ja sao permitidos e pure<2.\n"
        "O modulo implementa GodMode, noclip/voar, invisibilidade, Freeze Ammo, revive nativo e suicidio; teste no Windows ainda pendente.\n"
        "Reviver nativo NAO limpa morte/hospital ESX/QBCore. Bloqueio nativo NAO concede chaves do framework.\n"
        "Silent Aim oferece apenas disparo direcionado experimental. Voz recebida, ADMs/nomes e oclusao ESP continuam pendentes.\n"
        "Aim/ESP/recoil/spread usam o backend de memoria b3258 e suas validacoes.\n"
        "Para usar somente o backend externo anterior: Abrir-somente-externo.cmd ou ZyonMenu.exe --external.\n"
        "Compilacao e verificacoes automatizadas registradas no manifest. Windows/FiveM real nao executados aqui.\n"
        "Se abrir o modo externo, use Usar cliente local no banner. Preparar modulo e Verificar novamente ficam visiveis em todas as abas.\n"
        "Consulte codigo-fonte/README.md e codigo-fonte/docs/client-module-3.3.2.md.\n"
    )
    archive = OUTPUT / f"{PREFIX}-{flavor}.zip"
    with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED, compresslevel=6) as bundle:
        for path in binary_files:
            bundle.write(path, PREFIX + "/" + path.name)
        bundle.writestr(PREFIX + "/LEIA-ME.txt", quick_start.replace("\n", "\r\n"))
        bundle.writestr(PREFIX + "/Abrir-modulo-local.cmd", '@echo off\r\ncd /d "%~dp0"\r\nstart "" "ZyonMenu.exe" --client-module\r\n')
        bundle.writestr(PREFIX + "/Abrir-somente-externo.cmd", '@echo off\r\ncd /d "%~dp0"\r\nstart "" "ZyonMenu.exe" --external\r\n')
        bundle.writestr(PREFIX + "/manifest.json", json.dumps(manifest, ensure_ascii=False, indent=2) + "\n")
        for path in sorted(source_files):
            bundle.write(path, PREFIX + "/codigo-fonte/" + path.relative_to(PROJECT).as_posix())
        for destination, path in license_files.items():
            bundle.write(path, PREFIX + "/" + destination)
    with zipfile.ZipFile(archive) as bundle:
        if bundle.testzip() is not None:
            raise RuntimeError("ZIP failed integrity check.")
        forbidden = {"gta-game-five.dll", "gta-net-five.dll", "rage-scripting-five.dll", "scripting-gta.dll",
                     "citizen-resources-gta.dll", "citizen-playernames-five.dll", "Midgard.exe", "Astrix Loader.exe"}
        if any(Path(name).name in forbidden for name in bundle.namelist()):
            raise RuntimeError("Private analysis binary in package.")
    results.append({"file": archive.name, "bytes": archive.stat().st_size,
                    "sha256": hashlib.sha256(archive.read_bytes()).hexdigest()})
(OUTPUT / f"{PREFIX}-packages.json").write_text(json.dumps(results, indent=2) + "\n")
print(json.dumps(results, indent=2))
