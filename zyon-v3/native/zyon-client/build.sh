#!/usr/bin/env bash
set -euo pipefail
module_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
module_zig="${ZYON_ZIG_COMMAND:-zig}"
module_output="${ZYON_CLIENT_OUTPUT_DIR:-$module_root/dist}"
mkdir -p "$module_output"
export ZIG_GLOBAL_CACHE_DIR="${ZIG_GLOBAL_CACHE_DIR:-$module_output/zig-global-cache}"
export ZIG_LOCAL_CACHE_DIR="${ZIG_LOCAL_CACHE_DIR:-$module_output/zig-local-cache}"
"$module_zig" rc /:auto-includes none /:output-format coff /:target x86_64 /fo "$module_output/zyon-client.res" -- "$module_root/zyon-client.rc"
"$module_zig" c++ -target x86_64-windows-gnu -std=c++17 -O2 -shared \
    -Wall -Wextra -Werror -I "$module_root/include" \
    "$module_root/src/Plugin.cpp" "$module_root/src/Engine.cpp" "$module_root/src/DirectedFire.cpp" "$module_output/zyon-client.res" \
    -lkernel32 -ladvapi32 -luser32 -o "$module_output/ZyonClient.asi"
python3 "$module_root/verify_pe.py" "$module_output/ZyonClient.asi"
