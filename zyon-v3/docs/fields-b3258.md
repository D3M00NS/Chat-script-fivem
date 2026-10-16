# Field evidence for the external b3258 client

This document describes the memory backend selected by `--external`. The separate 3.2.0 client module executes a fixed set of natives through the official client scheduler; see [client module](client-module-3.2.0.md).

Target requested in the conversation: Windows 10 x64, process `FiveM_b3258_GTAProcess.exe`. All evidence below is static source or binary inspection. None is a gameplay test. A process name selects the candidate profile; it does not prove that every address is valid. Reads, signatures, pointer chains and numeric values must pass validation before a feature runs.

No code from the third-party ESP implementations was copied. Their documented field locations are cited as factual compatibility references. They have no identified license for their own implementation; this document is not permission to redistribute that implementation. The original uploaded executables are not part of this project.

## Address and entity profile

The uploaded Midgard binary was statically inspected by a separate task. Its reported profile agrees with the public b3258 ESP source for the following module-relative globals and fields. This agreement is corroboration, not validation against a running game.

| Location | Interpretation | Evidence |
| --- | --- | --- |
| Module `+0x25B14B0`, read pointer | World object | Public b3258 source, lines 38–41; static Midgard report |
| World `+0x8`, read pointer | Local ped | Same |
| Module `+0x1FBD4F0`, read pointer | Replay interface | Public b3258 source, line 40; static Midgard report |
| Replay `+0x18`, read pointer | Ped replay interface | Public source, line 61; static Midgard report |
| Ped replay interface `+0x100`, read pointer | Ped list | Public source, line 66; static Midgard report |
| Ped list `+index*0x10`, read pointer | Ped in one list slot | Public source, lines 70–73; static Midgard report |
| Module `+0x201DBA0`, read pointer | Viewport | Public source, line 39; static Midgard report |
| Viewport `+0x24C`, read 64 bytes | 4×4 floating point projection data | Public source, line 100; static Midgard report |
| Module `+0x201E7D0`, read pointer | Camera global | Public source, line 48; static Midgard report; camera subfields require separate evidence |
| Ped `+0x90`, read 12 bytes | Position vector | Public header and static Midgard report |
| Ped `+0x60`, read 64 bytes | Ped transformation matrix | Public source, lines 90–92; static Midgard report |
| Ped `+0x280`, read/write float | Health | Public source, line 47; CFX `Entity.HealthFloat` getter/setter at `MemoryAddress+640`; static Midgard report. Do not substitute `0x240`. |
| Ped `+0x284`, read/write float | Maximum health | CFX `Entity.MaxHealthFloat` getter/setter at `MemoryAddress+644`. Numeric range and target identity still need runtime validation. |
| Ped `+0x10A8`, read pointer | Player info candidate | Public b3258 source, line 42. Validate before using a nonzero pointer as a player filter. |
| Ped `+0x10B8`, read pointer | Weapon manager candidate | Public b3258 source, line 45. Its following links need validation. |

The third-party loop reads 200 slots without establishing the interface's capacity from an authoritative structure. Treat that as a bounded sample limit, not proof of a universal ped-list capacity. Bad or unreadable slots must be skipped; a failed local ped or global validation must suspend the feature.

Sources, pinned to the inspected revision:

- [CFX health and maximum-health float accessors](https://github.com/citizenfx/fivem/blob/1bc76290982a77829f95b4267be7a493412cdc17/code/client/clrcore/External/Entity.cs#L82)
- [b3258 setup and entity reads](https://github.com/vizu1234/fivem-external-esp-base/blob/b1134563b9912d25e2dc53fcaf89c59980db4390/FiveM%20External/src/game/game.cpp#L25)
- [Profile declarations, including position](https://github.com/vizu1234/fivem-external-esp-base/blob/b1134563b9912d25e2dc53fcaf89c59980db4390/FiveM%20External/src/game/game.h#L10)

## Root bones and identifiers

The public b3258 example reads each root bone's local `Vector3` from ped `+0x410 + index*0x10`, then transforms it by the matrix at ped `+0x60`. These indexes identify the small root-bone array; they are not GTA bone-tag hashes or general skeleton indexes.

| Root-array index | Public example's interpretation |
| --- | --- |
| 0 | Head |
| 1 | Left foot |
| 2 | Right foot |
| 3 | Left ankle |
| 4 | Right ankle |
| 5 | Left hand |
| 6 | Right hand |
| 7 | Neck |
| 8 | Hip |

Its six root skeleton connections are head–neck, neck–hip, neck–left hand, neck–right hand, hip–left foot and hip–right foot. This is a root skeleton only; elbows and knees are not represented. Matrix and resulting coordinates require finite/range validation. The older `+0x430` bone location in older external examples must not be imported into this profile.

The same b3258 source assigns `playerNetID=0xE8` and `visFlag=0x145C` but does not use either value in its ESP implementation. This assignment alone does not prove their read type, semantics or accuracy. In particular, `playerInfo+0xE8` is not established here as a FiveM server ID. Names and server IDs must remain unavailable until their mapping is independently derived. A list slot may be displayed only as a local entity index with that exact meaning.

Likewise, an arbitrary visibility byte must not be advertised as a line-of-sight test. A visibility filter requires separately established semantics.

Sources:

- [Root-bone reads](https://github.com/vizu1234/fivem-external-esp-base/blob/b1134563b9912d25e2dc53fcaf89c59980db4390/FiveM%20External/src/game/game.cpp#L87)
- [Root-bone indexes and connections](https://github.com/vizu1234/fivem-external-esp-base/blob/b1134563b9912d25e2dc53fcaf89c59980db4390/FiveM%20External/src/game/game.h#L29)
- [CFX distinction between player index and server ID](https://github.com/citizenfx/fivem-docs/blob/c2b2125759dfb8d0040492692aa1ac81319efa62/content/docs/scripting-manual/networking/ids.md#L9)

## Weapon info, recoil shake and spread

The b3258 source identifies ped `+0x10B8` as weapon manager. An older public external implementation follows weapon manager `+0x20` to a weapon-info pointer. That second link is corroborating historical evidence only, not an authoritative b3258 structure definition. Validate the resulting pointer and, if available, confirm it against the weapon-info table described below.

Primary CFX code establishes these facts about weapon-info metadata:

- Weapon-info `+0x10` is a 32-bit weapon hash.
- Weapon-info `+0x60` is an ammo-info pointer; `+0x70` is a 32-bit maximum clip size in the declared class.
- A `CWeapon` object has its weapon-info pointer at `+0x40`. `CWeapon` and the weapon-manager object are different structures; do not confuse their chains.
- CFX locates weapon recoil-shake and accuracy-spread fields by signatures and extracts the displacement from the instruction. It does not rely on a fixed numeric recoil offset.

| Feature | Signature bytes, `??` wildcard | Extracted displacement |
| --- | --- | --- |
| Recoil shake amplitude | `48 8B 47 40 F3 0F 10 B0 ?? ?? ?? ??` | Little-endian signed `int32` at match `+8`; field belongs to the weapon-info object. |
| Accuracy spread | `F3 0F 10 43 ?? F3 0F 59 05 ?? ?? ?? ?? F3 0F 2C C0` | Sign-extended `disp8` byte at match `+4`; negative values are rejected. Field belongs to weapon-info. |

The setters write 32-bit floating point values at those resolved fields. In this source revision, the recoil getter exposes the bits as an integer while its setter accepts a float; preserve exact original bytes for restoration. Recoil-shake amplitude represents weapon camera shake. Setting it to zero does not by itself establish removal of every aiming or ballistic recoil mechanic.

The `CWeaponInfo` table gives an independent way to validate the candidate weapon-info pointer/hash. CFX finds it using `45 33 D2 85 C0 74 ?? 44 0F B7 05`; the count reference is resolved around match `+11` and the list reference around match `+26`. These are instruction positions for CFX's relative-address helper, not fields in a weapon object. An external implementation must decode the actual instructions and RIP-relative operands. CFX reads the count as `uint16` and compares the hash at each table entry `+16`.

Do not hard-code a candidate `0x2E8`, `0x2F4` or the interpretation of `0x74` based on unrelated older cheat labels. Resolve the displacement from an executable-section match, require a unique valid match, validate the operand range and verify finite plausible original values. Missing or ambiguous signatures disable these two features. Record originals by weapon-info pointer and weapon hash; restore only values owned by the controller, and discard stale object references on process replacement.

Primary sources, inspected revision `1bc76290982a77829f95b4267be7a493412cdc17`:

- [WeaponInfo and CWeapon declarations](https://github.com/citizenfx/fivem/blob/1bc76290982a77829f95b4267be7a493412cdc17/code/components/extra-natives-five/src/WeaponExtraNatives.cpp#L52)
- [Weapon-table hash lookup](https://github.com/citizenfx/fivem/blob/1bc76290982a77829f95b4267be7a493412cdc17/code/components/extra-natives-five/src/WeaponExtraNatives.cpp#L286)
- [Signature extraction for weapon table and fields](https://github.com/citizenfx/fivem/blob/1bc76290982a77829f95b4267be7a493412cdc17/code/components/extra-natives-five/src/WeaponExtraNatives.cpp#L344)
- [Recoil and spread setter/getter field semantics](https://github.com/citizenfx/fivem/blob/1bc76290982a77829f95b4267be7a493412cdc17/code/components/extra-natives-five/src/WeaponExtraNatives.cpp#L421)
- [Historical manager-to-weapon-info link; not b3258 proof](https://github.com/Device9999/FiveM-External-ESP/blob/master/gta_external/features/features.cpp#L131)

## Invulnerability flags

The CFX native documentation for `SET_ENTITY_INVINCIBLE` describes a 32-bit flags word at ped `+0x188` and identifies bits 8 and 9 as invincibility indicators. It also describes bit 9 as a way to retain ragdoll when making a ped invincible. This is authoritative published native documentation, but does not provide a runtime signature proving the location on this particular process.

On a little-endian word, byte ped `+0x189` contains those bits: its bit 0 corresponds to word bit 8 and its bit 1 to word bit 9. Therefore a blanket byte write of `1` discards other bits in that byte. If an implementation uses a validated flags word, it must modify only its owned bit, preserving unrelated bits during enable, update and restoration. A static value in the uploaded program's profile that has no confirmed access is not evidence of actual behavior.

Source: [CFX SET_ENTITY_INVINCIBLE documentation](https://github.com/citizenfx/natives/blob/7263f2118982857a08787f81045fbdc3defa1d70/ENTITY/SetEntityInvincible.md#L12).

## Features without established external implementation

Writing health alone is not a verified resurrection operation. This document establishes no externally callable revive flow, framework medical reset, arbitrary game-native execution, frozen-ammo chain, noclip collision state, silent aim or player-name/server-ID mapping. Such controls must not claim applied functionality solely because their UI setting changes.

Live validation on Windows/FiveM remains outstanding for all fields and effects. Original uploaded binaries were inspected without execution. No anticheat bypass, kernel component, driver, injection or concealment technique is included in this research.
