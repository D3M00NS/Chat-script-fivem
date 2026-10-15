#!/usr/bin/env bash
# Bash / scripts/test.sh / .NET 8 SDK, Lua 5.4 and Node / portable and administrative checks.
set -euo pipefail
zyon_project_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
zyon_dotnet_command="${ZYON_DOTNET_COMMAND:-dotnet}"
zyon_lua_command="${ZYON_LUA_COMMAND:-lua5.4}"
zyon_node_command="${ZYON_NODE_COMMAND:-node}"
cd "$zyon_project_root"
mkdir -p .build/dotnet-home .build/nuget .build/xdg-data .build/runtime .build/tmp
chmod 700 .build/runtime
export DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-$zyon_project_root/.build/dotnet-home}"
export NUGET_PACKAGES="${NUGET_PACKAGES:-$zyon_project_root/.build/nuget}"
export XDG_DATA_HOME="${XDG_DATA_HOME:-$zyon_project_root/.build/xdg-data}"
export XDG_RUNTIME_DIR="${XDG_RUNTIME_DIR:-$zyon_project_root/.build/runtime}"
export TMPDIR="${TMPDIR:-$zyon_project_root/.build/tmp}"
export DOTNET_NOLOGO=1 DOTNET_CLI_TELEMETRY_OPTOUT=1
"$zyon_dotnet_command" run --project tests/ZyonMenu.Standalone.Tests/ZyonMenu.Standalone.Tests.csproj -c Release
"$zyon_dotnet_command" run --project tests/ZyonMenu.Core.Tests/ZyonMenu.Core.Tests.csproj -c Release
"$zyon_lua_command" tests/admin_server.lua
"$zyon_lua_command" tests/admin_client.lua
"$zyon_lua_command" tests/admin_medical.lua
"$zyon_lua_command" tests/admin_pairing_client.lua
"$zyon_node_command" --test tests/admin_nui_pairing.test.mjs
