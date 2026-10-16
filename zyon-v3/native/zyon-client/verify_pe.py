#!/usr/bin/env python3
"""Reject a successful compiler exit that did not actually produce an x64 ASI."""

import hashlib
import json
from pathlib import Path
import struct
import sys


def inspect(path: Path) -> dict:
    data = path.read_bytes()

    def integer(at: int, format_: str = "I") -> int:
        return struct.unpack_from("<" + format_, data, at)[0]

    def require(condition: bool, message: str) -> None:
        if not condition:
            raise ValueError(message)

    require(data[:2] == b"MZ", "Output is not a PE image (MZ missing)")
    pe = integer(0x3C)
    require(data[pe:pe + 4] == b"PE\0\0", "PE signature missing")
    require(integer(pe + 4, "H") == 0x8664, "ASI must target Windows AMD64")
    require(integer(pe + 22, "H") & 0x2000, "PE must have IMAGE_FILE_DLL set")
    optional = pe + 24
    require(integer(optional, "H") == 0x20B, "PE32+ optional header missing")
    require(integer(optional + 16) != 0, "DLL initialization entry point missing")
    sections = pe + 24 + integer(pe + 20, "H")
    section_count = integer(pe + 6, "H")

    def offset(rva: int) -> int:
        for index in range(section_count):
            section = sections + index * 40
            virtual = integer(section + 12)
            raw_size = integer(section + 16)
            if virtual <= rva < virtual + raw_size:
                result = integer(section + 20) + rva - virtual
                require(result < len(data), "RVA points outside image file")
                return result
        raise ValueError(f"RVA 0x{rva:x} has no file-backed section")

    def string(at: int) -> str:
        return data[at:data.index(0, at)].decode("ascii")

    imports = []
    import_rva = integer(optional + 112 + 8)
    if import_rva:
        descriptor = offset(import_rva)
        while any(data[descriptor:descriptor + 20]):
            imports.append(string(offset(integer(descriptor + 12))))
            descriptor += 20
    # Zig bundles its C++ support; no separately distributed compiler runtime
    # or replacement ScriptHook DLL may become a hidden installation step.
    allowed = {"kernel32.dll", "advapi32.dll", "user32.dll", "msvcrt.dll", "ntdll.dll"}
    # Universal CRT contracts are system components on Windows 10/11, the
    # supported FiveM client OS. They do not require shipping a compiler DLL.
    allowed.update("api-ms-win-crt-" + contract + "-l1-1-0.dll" for contract in
                   ("private", "runtime", "string", "stdio", "heap", "convert", "locale", "math", "utility"))
    require(bool(imports) and all(name.lower() in allowed for name in imports),
            "Unexpected non-system DLL import: " + ", ".join(imports))

    resource = offset(integer(optional + 112 + 16))

    def entries(directory: int):
        count = integer(resource + directory + 12, "H") + integer(resource + directory + 14, "H")
        for index in range(count):
            at = resource + directory + 16 + index * 8
            key, child = integer(at), integer(at + 4)
            if key & 0x80000000:
                at = resource + (key & 0x7FFFFFFF)
                length = integer(at, "H")
                key = data[at + 2:at + 2 + length * 2].decode("utf-16le")
            yield key, child

    build_type = next((child for key, child in entries(0) if key == 3258), None)
    require(build_type is not None and build_type & 0x80000000,
            "FX_ASI_BUILD resource type 3258 missing")
    build_name = next((child for key, child in entries(build_type & 0x7FFFFFFF)
                       if key == "FX_ASI_BUILD"), None)
    require(build_name is not None and build_name & 0x80000000,
            "FX_ASI_BUILD resource name missing")
    languages = list(entries(build_name & 0x7FFFFFFF))
    require(bool(languages), "Build resource language/data missing")
    for _, child in languages:
        require(not child & 0x80000000, "Invalid build resource data entry")
        at = resource + child
        require(integer(at + 4) > 0, "Build resource payload is empty")
        offset(integer(at))
    return {"path": str(path), "bytes": len(data), "machine": "AMD64", "dll": True,
            "buildResource": {"type": 3258, "name": "FX_ASI_BUILD"},
            "imports": sorted(imports), "sha256": hashlib.sha256(data).hexdigest()}


if __name__ == "__main__":
    try:
        print(json.dumps(inspect(Path(sys.argv[1])), indent=2))
    except (IndexError, ValueError, OSError, struct.error) as error:
        sys.exit("ASI validation failed: " + str(error))
