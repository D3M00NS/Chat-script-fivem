#!/usr/bin/env bash
set -euo pipefail
zyon_project_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
zyon_cxx_command="${ZYON_CXX_COMMAND:-g++}"
mkdir -p "$zyon_project_root/.build"
"$zyon_cxx_command" -std=c++17 -Wall -Wextra -Werror -pedantic \
    -I "$zyon_project_root/native/zyon-client/include" \
    "$zyon_project_root/native/zyon-client/src/Engine.cpp" \
    "$zyon_project_root/native/zyon-client/src/DirectedFire.cpp" \
    "$zyon_project_root/native/zyon-client/tests/EngineTests.cpp" \
    -o "$zyon_project_root/.build/zyon-native-engine-tests"
"$zyon_project_root/.build/zyon-native-engine-tests"
"$zyon_cxx_command" -std=c++17 -Wall -Wextra -Werror -pedantic \
    -I "$zyon_project_root/native/zyon-client/include" \
    "$zyon_project_root/native/zyon-client/src/DirectedFire.cpp" \
    "$zyon_project_root/native/zyon-client/tests/DirectedFireTests.cpp" \
    -o "$zyon_project_root/.build/zyon-native-directed-fire-tests"
"$zyon_project_root/.build/zyon-native-directed-fire-tests"
