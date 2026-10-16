# Optional FiveM client module

This directory contains a native Windows x64 ASI and its local IPC protocol. The desktop installer places only `ZyonClient.asi` in the effective `FiveM.app/plugins` directory while FiveM is closed. Restart FiveM after installation. No server resource, remote thread, executable patch or replacement `scripthookv.dll` is used.

The connected server must already authorize ScriptHook (`sv_scriptHookAllowed`) and use a pure level below 2. The plugin honors those existing CFX checks. It cannot enable server permission from the client. Its PE resource declares compatibility with build 3258; that declaration is not a gameplay test or support for another build.

The ASI resolves the public CFX ScriptHookV entry points dynamically and registers a script fiber. It also resolves the unique public `rage::scrEngine::GetNativeHandler` export from `rage-scripting-five.dll` by its exact MSVC class/namespace prefix. The lookup is used only to verify handler existence. Every native invocation still goes through `nativeInit`/`nativePush64`/`nativeCall` in the authorized script fiber. There is no raw handler invocation, guessed code address or replacement permission gate. An unavailable common handler blocks readiness; a missing optional handler removes only the capabilities that depend on it. The pipe worker receives typed, fixed-size commands and publishes snapshots; it does not execute natives. The module reports readiness only after a game timer advances between frames and the local ped/model are validated. Missing exports, stale frames and rejected commands remain visible through [the protocol](PROTOCOL.md).

Implemented native features are GodMode, noclip with live speed, invisibility, observed ammo replenishment, native revive and suicide. Revive's confirmation covers the GTA native death state. Medical frameworks and custom hospitals can keep separate state. No generic ADM status is inferred, and no Silent Aim capability is advertised by this module.

The module stores observable state before changing it and restores its own effects on disconnect, lease expiry, ped replacement and configuration changes while authorized frames remain available. Failed restoration keeps the original snapshot for later retries; a snapshot is discarded only after confirmed restoration, a newer observable external value, or an explicitly gone entity/changed model. Cleanup attempts every owned property independently. Noclip snapshots both collision and frozen position, freezes for idle hover, reads the current position each frame and ignores movement keys while another application has focus. Turning it off restores the original collision/frozen values. Movement and clip-ammo writes are read back before being considered observed. Suicide keeps this module's protection and noclip off until the corresponding toggles have explicitly been submitted off and then on again. An ASI cannot promise cleanup after the server has revoked permission and CFX has stopped its script. Live DLL unloading is unsupported; the module stays loaded until FiveM exits.

## Build and validation

Use Zig 0.15.2 and a C++17 toolchain for portable tests:

```sh
ZYON_ZIG_COMMAND=/path/to/zig bash native/zyon-client/build.sh
g++ -std=c++17 -Wall -Wextra -Werror -O2 -I native/zyon-client/include \
  native/zyon-client/src/Engine.cpp native/zyon-client/tests/EngineTests.cpp -o /tmp/zyon-engine-tests
/tmp/zyon-engine-tests
```

The canonical artifact is `native/zyon-client/dist/ZyonClient.asi`. `build.sh` keeps its local/global Zig caches under the output directory unless explicitly configured. The build links a native DLL and embeds the exact build resource required by CFX; it does not load or execute that DLL. Python 3 verifies the resulting PE32+ AMD64 DLL, initialization entry point, exact build-resource type/name and system-only DLL imports. A compiler success that produces a static archive is rejected.

Portable engine tests use fake natives to verify motion and lifecycle invariants. Cross compilation and PE inspection cannot validate the Windows loader, the actual ScriptHook permission, game-native behavior or damage replication. Before considering a feature verified, use an authorized Windows/FiveM session to check pairing, successful and rejected actions, ped replacement, restoration on disconnect and loss of permission. Do not interpret an installed file or successful compiler result as an active game session.

## Authoritative interfaces

Inspected CFX source commit: `f5c46b3f1a6941982bdbe303185b13cbccb74e5d`.

- `code/components/asi-five/src/Component.cpp`: client ASI path, pure-mode gate and `FX_ASI_BUILD` resource check.
- `code/components/scripthookv/src/VishCompat.cpp`: public exports, script fibers, authorization gates and shared native result buffer.
- `code/components/rage-scripting-five/src/scrThread.cpp`: active script-thread setup and restoration.
- `code/components/rage-scripting-five/include/scrThread.h`: RAGE argument/result layout, including vector slots.
- `code/components/rage-scripting-five/src/scrEngine.cpp`: public original-hash handler lookup and internal native mapping.
- `ext/native-decls/IsEntityPositionFrozen.md`: observable CFX frozen-state getter, paired with `FREEZE_ENTITY_POSITION`.

No source or binary from a third-party mod menu is executed by this build.
