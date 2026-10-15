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
for part in ("src", "tests", "scripts", "docs", "release", "fivem"):
    source_files.extend(path for path in (PROJECT / part).rglob("*")
                        if path.is_file() and not {"bin", "obj", "__pycache__"}.intersection(path.parts))
nuget = Path(os.environ.get("NUGET_PACKAGES", PROJECT / ".build/nuget"))
dotnet_root = Path(os.environ.get("ZYON_DOTNET_ROOT", PROJECT / "tools/dotnet"))
license_files = {
    "licencas/DotNet-LICENSE.txt": nuget / "microsoft.netcore.app.runtime.win-x64/8.0.32/LICENSE.TXT",
    "licencas/WindowsDesktop-LICENSE.txt": nuget / "microsoft.windowsdesktop.app.runtime.win-x64/8.0.32/LICENSE",
    "licencas/ThirdPartyNotices.txt": dotnet_root / "ThirdPartyNotices.txt",
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
        "product": "Zyon external standalone menu",
        "version": VERSION,
        "target": "Windows 10 x64 / standalone FiveM b3258",
        "runtimeIncluded": flavor == "completo",
        "windowsRuntimeTested": False,
        "fiveMRuntimeTested": False,
        "serverInstallationRequired": False,
        "implementedExternalFunctions": ["aim", "esp", "godMode", "noRecoil", "noSpread"],
        "implementationDoesNotEstablishGameplayValidation": True,
        "unavailableFunctions": ["silentAim", "revive", "suicide", "noclip", "invisibility", "muteReceivedVoice", "adminEsp", "playerNames", "freezeAmmo", "lineOfSight"],
        "verification": validation,
        "binaries": [{"file": path.name, "bytes": path.stat().st_size,
                      "sha256": hashlib.sha256(path.read_bytes()).hexdigest()} for path in binary_files],
    }
    runtime_note = ("Runtime incluido: abra ZyonMenu.exe.\n" if flavor == "completo" else
                    "Requer .NET Desktop Runtime 8 x64: https://dotnet.microsoft.com/download/dotnet/8.0\n")
    quick_start = (
        f"ZYON {VERSION} - EXECUTAVEL EXTERNO / SEM RECURSO NO SERVIDOR\n\n"
        "1. Extraia o ZIP completo para pasta nova e feche o menu anterior.\n"
        "2. Abra ZyonMenu.exe para o modo externo b3258.\n"
        f"3. Confirme Zyon {VERSION} no titulo. Se houver aviso, feche a copia anterior, inclusive se estiver oculta com Insert.\n"
        "4. Ative a funcao que pretende testar. Consulte Situacao das funcoes para ver a leitura ou falha especifica.\n"
        "5. Com FiveM aberto e apos reproduzir a falha, use Salvar diagnostico e envie o arquivo JSON para analise.\n\n"
        + runtime_note + "\n"
        "Corrige a abertura que reutilizava silenciosamente uma versao antiga e o descarte de ossos por padding invalido.\n"
        "Aim, ESP, GodMode, No Recoil e No Spread dependem das leituras do perfil b3258. Leitura confirmada nao comprova efeito no jogo.\n"
        "Silent Aim, reviver, suicidio, noclip, invisibilidade, silenciar, ADMs/nomes e Freeze Ammo continuam sem implementacao externa.\n"
        "Compilacao e verificacoes automatizadas registradas no manifest. Windows/FiveM real nao executados aqui.\n"
        "Consulte codigo-fonte/README.md e codigo-fonte/docs/external-3.1.2.md.\n"
    )
    archive = OUTPUT / f"{PREFIX}-{flavor}.zip"
    with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED, compresslevel=6) as bundle:
        for path in binary_files:
            bundle.write(path, PREFIX + "/" + path.name)
        bundle.writestr(PREFIX + "/LEIA-ME.txt", quick_start.replace("\n", "\r\n"))
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
