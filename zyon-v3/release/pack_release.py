# Python 3 / package verified local Windows publishes and source.
from pathlib import Path
import hashlib
import json
import os
import zipfile

PROJECT = Path(__file__).resolve().parents[1]
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
results = []
for flavor, folder in (("completo", "win-x64"), ("compacto", "compacto")):
    binary_files = sorted(path for path in (PROJECT / "dist" / folder).iterdir() if path.is_file())
    executable = PROJECT / "dist" / folder / "ZyonMenu.exe"
    if not executable.is_file():
        raise FileNotFoundError(executable)
    if flavor == "completo" and [path.name for path in binary_files] != ["ZyonMenu.exe"]:
        raise RuntimeError("Complete package must use a single locally published executable.")
    manifest = {
        "product": "Zyon external menu + optional administrative resource",
        "version": "3.1.1",
        "target": "Windows 10 x64 / standalone FiveM b3258 or authorized FiveM server",
        "runtimeIncluded": flavor == "completo",
        "windowsRuntimeTested": False,
        "fiveMRuntimeTested": False,
        "serverResourceOptional": True,
        "requiresAdministrativeResource": ["revive", "suicide", "noclip", "invisibility", "muteReceivedVoice", "adminEsp", "playerNames"],
        "supportedMedicalProviders": ["standalone", "esx", "qbcore"],
        "medicalSelection": "auto or explicit server configuration",
        "unavailableFunctions": ["silentAim", "customMedicalResourceAdapter", "esxIntermediateWithoutDeathStatusCallback"],
        "verification": validation,
        "binaries": [{"file": path.name, "bytes": path.stat().st_size,
                      "sha256": hashlib.sha256(path.read_bytes()).hexdigest()} for path in binary_files],
    }
    runtime_note = ("Runtime incluido: abra ZyonMenu.exe.\n" if flavor == "completo" else
                    "Requer .NET Desktop Runtime 8 x64: https://dotnet.microsoft.com/download/dotnet/8.0\n")
    quick_start = (
        "ZYON 3.1.1 - ESP ATUALIZADO E MODO ADMINISTRATIVO OPCIONAL\n\n"
        "1. Extraia o ZIP completo para pasta nova e feche o menu anterior.\n"
        "2. Abra ZyonMenu.exe para o modo externo b3258.\n"
        "3. Para funcoes nativas, instale fivem/zyon_admin_menu no seu servidor, conceda ACE zyon.modmenu e ensure o recurso.\n"
        "4. Entre no servidor autorizado e, com o menu fechado, abra Abrir-modo-administrativo.cmd. Se ja estiver aberto, use o botao da aba Noclip.\n"
        "5. Abra a aba Noclip para velocidade1-50m/s; W/A/S/D movem, Espaco sobe, Ctrl desce, Shift acelera.\n\n"
        + runtime_note + "\n"
        "O modo administrativo inclui revive nativo, suicidio, invisibilidade, voz recebida e ADMs no ESP.\n"
        "Servidor em outro computador exige zyon_menu_public_url HTTPS no server.cfg.\n"
        "Reviver detecta standalone/ESX/QBCore em cada servidor. Hospitais customizados e ESX sem API medica exigem adaptacao. Silent Aim indisponivel.\n"
        "Compilacao e verificacoes automatizadas registradas no manifest. Windows/FiveM real nao executados aqui.\n"
        "Consulte codigo-fonte/README.md e fivem/zyon_admin_menu/README.md.\n"
    )
    archive = OUTPUT / f"ZyonMenu-v3.1.1-{flavor}.zip"
    with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED, compresslevel=6) as bundle:
        for path in binary_files:
            bundle.write(path, "ZyonMenu-v3.1.1/" + path.name)
        bundle.writestr("ZyonMenu-v3.1.1/LEIA-ME.txt", quick_start.replace("\n", "\r\n"))
        bundle.writestr("ZyonMenu-v3.1.1/Abrir-modo-administrativo.cmd", '@echo off\r\ncd /d "%~dp0"\r\nstart "" "ZyonMenu.exe" --admin\r\n')
        bundle.writestr("ZyonMenu-v3.1.1/manifest.json", json.dumps(manifest, ensure_ascii=False, indent=2) + "\n")
        for path in sorted(source_files):
            bundle.write(path, "ZyonMenu-v3.1.1/codigo-fonte/" + path.relative_to(PROJECT).as_posix())
        for path in sorted((PROJECT / "fivem").rglob("*")):
            if path.is_file():
                bundle.write(path, "ZyonMenu-v3.1.1/" + path.relative_to(PROJECT).as_posix())
        for destination, path in license_files.items():
            bundle.write(path, "ZyonMenu-v3.1.1/" + destination)
    with zipfile.ZipFile(archive) as bundle:
        if bundle.testzip() is not None:
            raise RuntimeError("ZIP failed integrity check.")
        forbidden = {"gta-game-five.dll", "gta-net-five.dll", "rage-scripting-five.dll", "scripting-gta.dll",
                     "citizen-resources-gta.dll", "citizen-playernames-five.dll", "Midgard.exe", "Astrix Loader.exe"}
        if any(Path(name).name in forbidden for name in bundle.namelist()):
            raise RuntimeError("Private analysis binary in package.")
    results.append({"file": archive.name, "bytes": archive.stat().st_size,
                    "sha256": hashlib.sha256(archive.read_bytes()).hexdigest()})
(OUTPUT / "ZyonMenu-v3.1.1-packages.json").write_text(json.dumps(results, indent=2) + "\n")
print(json.dumps(results, indent=2))
