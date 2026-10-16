# Local client bridge protocol, version 2

Transport: `\\.\pipe\ZyonClientBridge-v2-<GTA process ID>`. One controller connection is accepted at a time. The pipe DACL grants access to the current Windows user and LocalSystem, and `PIPE_REJECT_REMOTE_CLIENTS` rejects remote connections. All integers are little endian. Messages have fixed sizes; no object pointers, native identifiers, code or strings are accepted as commands.

Version 1 peers are incompatible and rejected before payload parsing. The desktop creates a fresh nonzero 16-byte random token per connection and sends Hello first. Every following command and every response contains that same token. The token is connection scoped and is never saved. The pipe name binds the connection to the game process; a ped field is a GTA script handle, never a Windows PID or a server player ID.

## Header: 16 bytes

| Offset | Size | Field |
| --- | --- | --- |
| 0 | 4 | Magic `0x4E4F595A`, bytes `ZYON` |
| 4 | 2 | Version, `2` |
| 6 | 2 | Kind |
| 8 | 4 | Payload length |
| 12 | 4 | Reserved, zero |

Kinds: Hello `1`, Settings `2`, Action `3`, Status `4`, Disconnect `5`, Poll `6`. Only Status is sent by the plugin. Hello, Poll and Disconnect have a 16-byte token payload. Hello may occur only once per connection.

## Settings: 48 bytes

| Offset | Size | Field |
| --- | --- | --- |
| 0 | 16 | Connection token |
| 16 | 8 | Revision, nonzero and strictly increasing per connection |
| 24 | 4 | Flags |
| 28 | 4 | Noclip speed, integer 1..50 m/s |
| 32 | 4 | Experimental directed-fire FOV, integer 10..500 pixels |
| 36 | 4 | Experimental target bone: head `0`, neck `1`, chest `2` |
| 40 | 4 | Experimental activation: RMB `0`, LMB `1`, Alt `2`, Shift `3`, Ctrl `4` |
| 44 | 4 | Experimental options: players-only bit `0` required; visible-only bit `1` optional |

Flag bits: GodMode `0`, Noclip `1`, Invisible `2`, FreezeAmmo `3`, experimental directed fire `8`. All other bits must be zero. Settings replace the whole snapshot. Changing speed does not advance the character or recreate the noclip state; movement happens only during game frames.

## Action: 32 bytes

| Offset | Size | Field |
| --- | --- | --- |
| 0 | 16 | Connection token |
| 16 | 8 | Nonce, nonzero and strictly increasing per connection |
| 24 | 4 | Action: native Revive `1`, Suicide `2`, LockVehicle `3`, UnlockVehicle `4` |
| 28 | 4 | Expected ped handle, nonzero and captured from a recent Status |

Only one action may be pending. A new action does not replace its pending predecessor, including before the game thread has observed a new connection epoch. The game thread revalidates the expected ped before changing health or vehicle door state, including while waiting for native confirmation. Revive confirms the native GTA death state; it does not confirm ESX/QBCore medical state. Suicide disables this module's GodMode and noclip. Each stays suppressed until its toggle has explicitly been submitted off and then on again.

Vehicle actions capture the occupied vehicle, or use the native nearest-road-vehicle query within eight metres. They revalidate entity existence, type, model and local ped before a write and its acknowledgement. Nearby targets must remain within the full three-dimensional radius; leaving a captured occupied vehicle cancels the action. A nearer vehicle appearing later never replaces the captured target. The engine requires observed network control. It may request control through `NETWORK_REQUEST_CONTROL_OF_ENTITY` during at most two seconds; a true request return alone never permits a write. It sets `SET_VEHICLE_DOORS_LOCKED` only once per nonce, using `2` for locked or `1` for unlocked, and acknowledges only matching `GET_VEHICLE_DOOR_LOCK_STATUS` readback on the same validated vehicle. Vehicle confirmation is independent of the ped alive flag. No framework keys, server authority, per-player lock flags, entity position or seating state are changed. An authoritative server script can subsequently restore its own door state.

## Status: 240 bytes

| Offset | Size | Field |
| --- | --- | --- |
| 0 | 16 | Connection token |
| 16 | 8 | Tick: Windows `GetTickCount64` at the last game script frame |
| 24 | 8 | Desired settings revision accepted by the worker |
| 32 | 8 | Applied revision reported by the game thread |
| 40 | 8 | Latest action nonce |
| 48 | 4 | Capability mask |
| 52 | 4 | State flags |
| 56 | 4 | Latest action kind |
| 60 | 4 | Action result |
| 64 | 4 | Current native ped handle |
| 68 | 4 | Actual game build |
| 72 | 4 | Error code |
| 76 | 4 | Reserved, zero |
| 80 | 160 | NUL-terminated UTF-8 message, unused bytes zero |

Capability bits: GodMode `0`, Noclip `1`, Invisible `2`, FreezeAmmo `3`, native Revive `4`, Suicide `5`, LockVehicle `6`, UnlockVehicle `7`, experimental directed fire `8`. The experimental capability is separate from redirecting an original projectile. Generic ADM identification and framework medical integration are not advertised. State bits: Ready `0`, SettingsActive `1`, PedAlive `2`, Blocked `3`. Ready requires a validated ped, build 3258, a successful common-native preflight and no Blocked flag. An optional missing handler removes only dependent capability bits. A requested unavailable feature is not executed; supported features can still run, but the whole snapshot is not acknowledged SettingsActive until a compatible revision is submitted. SettingsActive requires a submitted revision applied by the game thread, even if its selected flags are all zero. Applied revision is zero whenever the snapshot is not active; this can be normal while noclip waits for an on-foot, controlled ped.

Action results: None `0`, Pending `1`, Observed `2`, Failed `3`. The worker may acknowledge an accepted action as Pending before the next game frame. Only the engine may report Observed after checking its native outcome. The final nonce/result remains visible until another action is accepted. A Status response by itself does not prove that the command in the preceding request was applied.

Error codes: None `0`; WaitingNative `10`; Unavailable `11`; InvalidPed `12`; EffectFailure `13`; ActionFailure `14`; LeaseOrOffline `15`; ProtocolInvalid `1001`; TokenInvalid `1002`; FrameStale `1003`; ActionBusy `1004`; StaleRevisionOrNonce `1005`. Error responses do not imply successful mutation. Invalid framing, tokens or setting ranges may close the connection. A token mismatch is not answered with the correct token.

## Lifecycle

Valid Hello, Poll, Settings and Action requests renew a three-second lease. The desktop should Poll at least once a second. A closed connection immediately marks the snapshot disconnected; the next authorized game frame restores the effects owned by this module. A timed-out lease invalidates its session epoch: reconnect with a new token and submit fresh settings deliberately.

The pipe worker never calls game natives. The script fiber copies the latest snapshot, applies effects and publishes status. A game frame older than one second clears Ready/SettingsActive and marks FrameStale. Pipe availability alone is not native readiness: the CFX scheduler stops the script when ScriptHook is unauthorized or the network session is not initialized. After revocation the same CFX gate also prevents cleanup natives; restoration after such a policy change cannot be promised. Normal game/process teardown removes that game's transient state.
