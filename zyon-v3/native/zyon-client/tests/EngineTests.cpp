#include "Engine.h"
#include "PeExportLookup.h"
#include <cmath>
#include <cstdlib>
#include <iostream>
#include <map>
#include <set>
#include <stdexcept>
#include <vector>

using namespace zyon;
namespace {
int assertions = 0;
constexpr std::uint64_t Timer = 0x9CD27B0045628463ULL, LocalPed = 0xD80958FC74E988A6ULL;
constexpr std::uint64_t GameBuild = 0x804B9F7BULL;
constexpr std::uint64_t Exists = 0x7239B21A38F536BAULL, IsPed = 0x524AC5ECEA15343EULL, Model = 0x9F47B058362C84B5ULL;
constexpr std::uint64_t Dead = 0x3317DEDB88C95038ULL, Damageable = 0xD95CC5D2AB15A09FULL, SetDamageable = 0x1760FFA8AB074D66ULL;
constexpr std::uint64_t Visible = 0x47D6F43D77935C75ULL, SetVisible = 0xEA1C610A04DB6BBBULL;
constexpr std::uint64_t Collision = 0xCCF1E97BEFDAE480ULL, SetCollision = 0x1A9205C1B9EE827FULL;
constexpr std::uint64_t InVehicle = 0x997ABD671D25CA0BULL, Attached = 0xB346476EF1A64897ULL;
constexpr std::uint64_t Control = 0x01BF60A500E28887ULL, Disable = 0xFE99B66D079CF6BCULL;
constexpr std::uint64_t Normal = 0x11E65974A982637CULL, Pressed = 0xE2587F8CBBD87B1DULL;
constexpr std::uint64_t Rotation = 0x837765A25378F0BBULL, Frame = 0x15C40837039FFAF7ULL;
constexpr std::uint64_t Coords = 0x3FEF770D40960D5AULL, SetCoords = 0x239A3351AC1DA385ULL, Velocity = 0x1C99BB7B6E96D16FULL;
constexpr std::uint64_t Weapon = 0x0A6DB4965674D243ULL, Total = 0x015A522136D7F951ULL, Clip = 0x2E1202248937775CULL;
constexpr std::uint64_t SetTotal = 0x14E56BC5B5DB6A19ULL, SetClip = 0xDCD2A934D65CB497ULL;
constexpr std::uint64_t Heading = 0xE83D4F9BA2A38914ULL, ReviveNative = 0xEA23C49EAA83ACFBULL;
constexpr std::uint64_t Maximum = 0x15D757606D170C3CULL, Health = 0x6B76DC1F3AE6E6A3ULL, Blood = 0x8FE22675A5A45817ULL;
constexpr std::uint64_t FreezePosition = 0x428CA6DBD1094446ULL;
constexpr std::uint64_t PositionFrozen = 0xEDBE6ADDULL;

void Check(bool condition, const char* description) {
    ++assertions;
    if (!condition) throw std::runtime_error(description);
}
void Near(float actual, float expected, const char* description) {
    Check(std::abs(actual - expected) < 0.0001f, description);
}
struct Entity {
    std::uint32_t model = 101;
    bool exists = true, ped = true, dead = false, damageable = true, visible = true, collision = false, frozen = false;
    float x = 0, y = 0, z = 0;
};
struct FakeNatives final : INativeInvoker {
    std::map<std::uint32_t, Entity> entities{{1, {}}, {2, {}}};
    std::map<std::uint64_t, int> calls;
    std::uint32_t timer = 100, localPed = 1, weapon = 123, build = 3258;
    int total = 50, clip = 10;
    bool enabled = true, vehicle = false, attached = false, controlled = true, up = false, down = false, fast = false;
    bool ignoreGod = false, ignoreHealth = false, replaceOnRevive = false;
    bool ignoreCoordinates = false, ignoreClip = false;
    float forward = 0, right = 0, dt = 0.016f, pitch = 0, yaw = 0;
    std::uint64_t failedHash = 0;
    std::set<std::uint64_t> missing;
    std::vector<std::uint32_t> healthTargets;
    bool HasNative(std::uint64_t hash) const noexcept override {
        if (!enabled || missing.count(hash)) return false;
        switch (hash) {
        case Timer: case LocalPed: case GameBuild: case Exists: case IsPed: case Model: case Dead:
        case Damageable: case SetDamageable: case Visible: case SetVisible: case Collision: case SetCollision:
        case PositionFrozen: case FreezePosition:
        case InVehicle: case Attached: case Control: case Disable: case Normal: case Pressed: case Rotation: case Frame:
        case Coords: case SetCoords: case Velocity: case Weapon: case Total: case Clip: case SetTotal: case SetClip:
        case Heading: case ReviveNative: case Maximum: case Health: case Blood: return true;
        default: return false;
        }
    }
    bool Invoke(std::uint64_t hash, const std::uint64_t* args, std::size_t count, NativeResult& result) noexcept override {
        ++calls[hash];
        result = {};
        // Like CFX, an unknown hash can still return a non-null stale buffer.
        // Engine must check HasNative rather than treating this as execution.
        if (!HasNative(hash)) return true;
        if (hash == failedHash) return false;
        const auto integer = [&](std::uint32_t value) { result.words[0] = value; return true; };
        const auto scalar = [&](float value) { result.words[0] = NativeArg(value); return true; };
        const auto vector = [&](float x, float y, float z) {
            result.words[0] = NativeArg(x); result.words[1] = NativeArg(y); result.words[2] = NativeArg(z); return true;
        };
        if (hash == Timer) return integer(timer);
        if (hash == GameBuild) return integer(build);
        if (hash == LocalPed) return integer(localPed);
        if (hash == Rotation) return vector(pitch, 0, yaw);
        if (hash == Frame) return scalar(dt);
        if (hash == ReviveNative) {
            if (count != 9 || !args[6] || args[7] != 6 || args[8] != 0) return false;
            entities[localPed].dead = false;
            if (replaceOnRevive) localPed = 2;
            return true;
        }
        if (hash == Disable) return count == 3;
        if (hash == Normal) return count == 2 && scalar(args[1] == 31 ? forward : right);
        if (hash == Pressed) return count == 2 && integer(args[1] == 22 ? up : args[1] == 36 ? down : fast);
        if (!count) return false;
        const auto ped = static_cast<std::uint32_t>(args[0]);
        const auto found = entities.find(ped);
        if (hash == Exists) return integer(found != entities.end() && found->second.exists);
        if (found == entities.end()) return false;
        auto& entity = found->second;
        switch (hash) {
        case IsPed: return integer(entity.ped);
        case Model: return integer(entity.model);
        case Dead: return count == 2 && args[1] == 0 && integer(entity.dead);
        case Damageable: return integer(entity.damageable);
        case SetDamageable: if (!ignoreGod) entity.damageable = args[1] != 0; return true;
        case Visible: return integer(entity.visible);
        case SetVisible: if (count != 3) return false; entity.visible = args[1] != 0; return true;
        case Collision: return integer(entity.collision);
        case SetCollision: if (count != 3) return false; entity.collision = args[1] == 0; return true;
        case PositionFrozen: return integer(entity.frozen);
        case FreezePosition: if (count != 2) return false; entity.frozen = args[1] != 0; return true;
        case InVehicle: return integer(vehicle);
        case Attached: return integer(attached);
        case Control: return integer(controlled);
        case Coords: return vector(entity.x, entity.y, entity.z);
        case SetCoords:
            if (count != 7) return false;
            if (!ignoreCoordinates) {
                entity.x = NativeFloat(args[1]); entity.y = NativeFloat(args[2]); entity.z = NativeFloat(args[3]);
            }
            return true;
        case Velocity: return count == 4;
        case Weapon: return integer(weapon);
        case Total: return integer(static_cast<std::uint32_t>(total));
        case Clip:
            if (count != 3 || !args[2]) return false;
            *reinterpret_cast<int*>(static_cast<std::uintptr_t>(args[2])) = clip;
            return integer(true);
        case SetTotal: if (count != 4 || args[3]) return false; total = static_cast<int>(args[2]); return true;
        case SetClip: if (count != 3) return false; if (!ignoreClip) clip = static_cast<int>(args[2]); return integer(true);
        case Heading: return scalar(90.0f);
        case Maximum: return integer(200);
        case Health:
            if (count != 3 || args[2]) return false;
            healthTargets.push_back(ped);
            if (!ignoreHealth) entity.dead = args[1] == 0;
            return true;
        case Blood: return true;
        default: return false;
        }
    }
};
struct Fixture {
    FakeNatives fake;
    Engine engine{fake};
    ControlSnapshot input;
    std::uint64_t now = 1000;
    Fixture() { input.connected = true; input.sessionEpoch = 1; input.settings.revision = 1; }
    RuntimeStatus Step(std::uint64_t milliseconds = 16) {
        now += milliseconds; input.lastSeenMs = now; fake.timer += static_cast<std::uint32_t>(milliseconds);
        engine.Step(input, now); return engine.Status();
    }
    RuntimeStatus ReadyNow() { Step(); return Step(); }
    void ActionNow(std::uint32_t action, std::uint64_t nonce = 1, std::uint32_t expectedPed = 1) {
        input.action = action; input.actionNonce = nonce; input.expectedPed = expectedPed;
    }
};

void PreflightAndQueuedSettings() {
    Fixture f; f.input.settings.flags = GodMode;
    Check(f.Step().capabilities == 0, "first frame must not claim native capabilities");
    Check(f.fake.entities[1].damageable, "queued settings must not write before preflight");
    const auto ready = f.Step();
    Check((ready.stateFlags & (Ready | SettingsActive | PedAlive)) == (Ready | SettingsActive | PedAlive), "second advancing frame validates the ped");
    Check(ready.capabilities == KnownCapabilities && !f.fake.entities[1].damageable, "queued GodMode becomes observed");
    Check(ready.tick == f.now && ready.build == 3258, "native frame records the verified wall-clock timestamp and observed build");
    Fixture blocked; blocked.fake.entities[1].model = 0;
    Check(!(blocked.ReadyNow().stateFlags & Ready), "zero model cannot establish readiness");
    Check(blocked.engine.Status().capabilities == 0, "invalid ped exposes no native capability");
}
void TimerMustAdvanceAndGateLossCleansUp() {
    Fixture f; f.input.settings.flags = GodMode | Invisible;
    f.Step(); f.input.lastSeenMs = f.now; f.engine.Step(f.input, f.now);
    Check(f.engine.Status().error == WaitingNative && f.fake.entities[1].damageable, "stuck timer cannot establish readiness");
    f.Step(); Check(!f.fake.entities[1].damageable && !f.fake.entities[1].visible, "advancing timer enables queued effects");
    f.engine.Step(f.input, f.now);
    Check(f.fake.entities[1].damageable && f.fake.entities[1].visible, "a lost advancing frame releases owned properties");
    Check(!(f.engine.Status().stateFlags & Ready), "stalled native frame cannot remain ready");
    const auto verified = f.engine.Status().tick;
    f.now += 100; f.input.lastSeenMs = f.now; f.engine.Step(f.input, f.now);
    Check(f.engine.Status().tick == verified, "stuck native frames cannot refresh the verified frame timestamp");
}
void SnapshotRestorationAndExternalChanges() {
    Fixture f; f.input.settings.flags = GodMode | Invisible;
    f.ReadyNow(); f.input.connected = false; f.Step();
    Check(f.fake.entities[1].damageable && f.fake.entities[1].visible, "disconnect restores original properties");
    Fixture original; original.fake.entities[1].damageable = false; original.fake.entities[1].visible = false;
    original.input.settings.flags = GodMode | Invisible; original.ReadyNow(); original.input.connected = false; original.Step();
    Check(!original.fake.entities[1].damageable && !original.fake.entities[1].visible, "pre-existing protection and invisibility remain");
    Fixture other; other.fake.entities[1].damageable = false; other.fake.entities[1].visible = false;
    other.input.settings.flags = GodMode | Invisible; other.ReadyNow();
    other.fake.entities[1].damageable = true; other.fake.entities[1].visible = true;
    other.input.settings.flags = 0; ++other.input.settings.revision; other.Step();
    Check(other.fake.entities[1].damageable && other.fake.entities[1].visible, "cleanup preserves newer observable external changes");
}
void CleanupDoesNotShortCircuit() {
    Fixture f; f.input.settings.flags = GodMode | Invisible; f.ReadyNow();
    f.fake.failedHash = Damageable; f.input.connected = false; const auto status = f.Step();
    Check(f.fake.entities[1].visible, "a failed protection cleanup does not skip visibility cleanup");
    Check(status.error == LeaseOrOffline && (status.stateFlags & Blocked), "unconfirmed cleanup cannot report active");
    Check(!f.fake.entities[1].damageable, "failed getter leaves the original protection snapshot pending");
    f.fake.failedHash = 0; f.Step();
    Check(f.fake.entities[1].damageable, "next frame retries a failed restoration getter");
}
void CleanupRetainsSnapshotsUntilConfirmed() {
    Fixture setter; setter.input.settings.flags = GodMode | Invisible; setter.ReadyNow();
    setter.fake.failedHash = SetDamageable; setter.input.connected = false; setter.Step();
    Check(!setter.fake.entities[1].damageable && setter.fake.entities[1].visible,
        "failed restore setter does not prevent independent visibility cleanup");
    setter.fake.failedHash = 0; setter.Step();
    Check(setter.fake.entities[1].damageable, "a failed restoration setter is retried");

    Fixture ignored; ignored.input.settings.flags = GodMode; ignored.ReadyNow();
    ignored.fake.ignoreGod = true; ignored.input.connected = false; ignored.Step(); ignored.Step();
    Check(!ignored.fake.entities[1].damageable, "a setter without a confirmed state never discards the snapshot");
    const auto attempts = ignored.fake.calls[SetDamageable];
    ignored.fake.ignoreGod = false; ignored.Step();
    Check(ignored.fake.entities[1].damageable && ignored.fake.calls[SetDamageable] == attempts + 1,
        "restoration retries continue after failed readback until the original value is observed");

    Fixture replaced; replaced.input.settings.flags = GodMode; replaced.ReadyNow();
    replaced.fake.failedHash = Damageable; replaced.fake.localPed = 2;
    ++replaced.input.settings.revision; replaced.Step();
    Check(replaced.fake.entities[2].damageable, "failed old-ped restoration blocks taking ownership of a new ped");
    replaced.fake.failedHash = 0; ++replaced.input.settings.revision; replaced.Step();
    Check(replaced.fake.entities[1].damageable && !replaced.fake.entities[2].damageable,
        "new ped only receives settings after the original ped restoration is confirmed");

    Fixture reused; reused.input.settings.flags = GodMode; reused.ReadyNow();
    reused.fake.failedHash = Damageable; reused.input.connected = false; reused.Step();
    reused.fake.entities[1].model = 999; reused.fake.failedHash = 0;
    const auto before = reused.fake.calls[SetDamageable]; reused.Step();
    Check(reused.fake.calls[SetDamageable] == before && !reused.fake.entities[1].damageable,
        "a changed model discards the stale snapshot without restoring onto a reused handle");

    Fixture gone; gone.input.settings.flags = GodMode; gone.ReadyNow();
    gone.fake.failedHash = Damageable; gone.input.connected = false; gone.Step();
    gone.fake.entities[1].exists = false; gone.fake.failedHash = 0;
    const auto goneCalls = gone.fake.calls[SetDamageable]; gone.Step();
    Check(gone.fake.calls[SetDamageable] == goneCalls, "an explicitly gone entity releases its snapshot without a setter");
}
void MovementUsesCurrentFrameAndSpeed() {
    Fixture f; f.input.settings.flags = Noclip; f.fake.forward = -1; f.fake.right = 1;
    f.ReadyNow();
    const auto& entity = f.fake.entities[1];
    Near(std::hypot(entity.x, entity.y), 5 * 0.016f, "diagonal motion must be normalized");
    Check(entity.frozen, "noclip snapshots and freezes the ped so idle flight can hover");
    Check(entity.collision, "noclip disables collision");
    const int moves = f.fake.calls[SetCoords];
    f.fake.entities[1].x = 1000; f.fake.entities[1].y = 2000;
    f.input.settings.speed = 10; ++f.input.settings.revision; f.fake.right = 0; f.fake.dt = 0.5f; f.Step();
    Near(entity.x, 1000, "movement reads the current position instead of stale integration state");
    Near(entity.y, 2001, "live speed applies exactly once with bounded frame time");
    Check(f.fake.calls[SetCoords] == moves + 1, "a settings revision never adds another movement step");
    f.fake.forward = 0; f.fake.up = true; f.fake.fast = true; f.fake.dt = 0.016f; f.Step();
    Near(entity.z, 0.48f, "space and shift provide vertical fast movement");
    f.fake.up = false; f.fake.down = true; f.fake.fast = false; f.Step();
    Near(entity.z, 0.32f, "control provides downward movement");
    f.input.settings.flags = 0; ++f.input.settings.revision; f.Step();
    Check(!entity.collision, "turning noclip off restores collision");
    Check(!entity.frozen, "turning noclip off restores the original unfrozen state");
}
void NoclipPrerequisitesAndOriginalCollision() {
    Fixture f; f.input.settings.flags = Noclip; f.fake.forward = -1; f.fake.vehicle = true;
    Check(!(f.ReadyNow().stateFlags & SettingsActive), "vehicle context must not claim noclip applied");
    Check(f.fake.calls[SetCoords] == 0, "noclip cannot move a ped in a vehicle");
    f.fake.vehicle = false; f.Step(); Check(f.fake.entities[1].collision, "valid on-foot context enables queued noclip");
    f.fake.controlled = false; f.Step(); Check(!f.fake.entities[1].collision, "lost network control restores collision");
    Fixture original; original.fake.entities[1].collision = true; original.input.settings.flags = Noclip;
    original.ReadyNow(); original.input.connected = false; original.Step();
    Check(original.fake.entities[1].collision, "pre-existing disabled collision is preserved");
    Fixture frozen; frozen.fake.entities[1].frozen = true; frozen.input.settings.flags = Noclip;
    frozen.ReadyNow(); frozen.input.connected = false; frozen.Step();
    Check(frozen.fake.entities[1].frozen, "pre-existing frozen state survives flight cleanup");
}
void FlightHoversWithoutReadingBackgroundKeys() {
    Fixture f; f.input.settings.flags = Noclip; f.fake.forward = -1; f.fake.up = true;
    f.input.gameFocused = false; auto status = f.ReadyNow();
    Check((status.stateFlags & SettingsActive) && f.fake.entities[1].frozen,
        "background flight preserves a frozen hover without consuming movement keys");
    Check(f.fake.calls[Normal] == 0 && f.fake.calls[Pressed] == 0 && f.fake.calls[SetCoords] == 0,
        "background application keystrokes cannot move the game ped");
    f.input.gameFocused = true; f.Step();
    Check(f.fake.calls[SetCoords] == 1, "foreground game focus resumes movement on the next frame");
    f.fake.forward = 0; f.fake.up = false; const auto before = f.fake.calls[SetCoords]; f.Step();
    Check(f.fake.entities[1].frozen && f.fake.calls[SetCoords] == before,
        "an idle flight frame keeps gravity disabled through observed frozen state");
    f.input.connected = false; f.fake.failedHash = FreezePosition; f.Step();
    Check(!f.fake.entities[1].collision && f.fake.entities[1].frozen,
        "a failed unfreeze still restores collision independently");
    f.fake.failedHash = 0; f.Step();
    Check(!f.fake.entities[1].frozen, "a failed frozen-state restore is retried on the correct ped");
}
void PedReplacementDoesNotReapplyOldSettings() {
    Fixture f; f.input.settings.flags = GodMode | Invisible; f.ReadyNow();
    f.fake.localPed = 2; auto status = f.Step();
    Check(f.fake.entities[1].damageable && f.fake.entities[1].visible, "owned properties restored on previous ped");
    Check(f.fake.entities[2].damageable && f.fake.entities[2].visible, "old snapshot is not copied to a replacement ped");
    Check((status.stateFlags & Ready) && !(status.stateFlags & SettingsActive), "replacement ped waits for a new revision");
    ++f.input.settings.revision; f.Step();
    Check(!f.fake.entities[2].damageable, "a new explicit revision may target the replacement ped");
}
void FailureRequiresFreshRevision() {
    Fixture f; f.input.settings.flags = GodMode; f.fake.ignoreGod = true;
    const auto status = f.ReadyNow();
    Check((status.stateFlags & Blocked) && !(status.stateFlags & Ready) && status.error == EffectFailure, "setter success without observable state is rejected");
    f.fake.ignoreGod = false; f.Step(); Check(f.fake.entities[1].damageable, "rejected revision cannot retry silently");
    ++f.input.settings.revision; f.Step(); Check(!f.fake.entities[1].damageable, "a fresh revision can recover after a correction");
}
void LeaseAndEpochRevocation() {
    Fixture f; f.input.settings.flags = GodMode; f.ReadyNow();
    f.now += LeaseMilliseconds + 1; f.fake.timer += LeaseMilliseconds + 1; f.engine.Step(f.input, f.now);
    Check(f.fake.entities[1].damageable && f.engine.Status().error == LeaseOrOffline, "expired lease releases effects");
    f.Step(); Check(!(f.engine.Status().stateFlags & Ready), "refreshing an expired epoch does not revive its permissions");
    ++f.input.sessionEpoch; f.ReadyNow(); Check(!f.fake.entities[1].damageable, "new connection can validate and apply its own snapshot");
}
void AmmoPreservesPickupsAndHasNoPersistentFlags() {
    Fixture f; f.input.settings.flags = FreezeAmmo; f.ReadyNow();
    f.fake.total = 49; f.fake.clip = 9; f.Step();
    Check(f.fake.total == 50 && f.fake.clip == 10, "observed consumption is replenished");
    f.fake.total = 80; f.Step(); f.fake.total = 79; f.fake.clip = 9; f.Step();
    Check(f.fake.total == 80 && f.fake.clip == 10, "pickups update the observed reserve baseline");
    f.input.settings.flags = 0; ++f.input.settings.revision; f.fake.total = 70; f.fake.clip = 7; f.Step();
    Check(f.fake.total == 70 && f.fake.clip == 7, "disabled freeze makes no ammo mutation");
}
void SuicideRequiresMatchingPedAndNonce() {
    Fixture f; f.input.settings.flags = GodMode | Noclip; f.ReadyNow();
    f.ActionNow(Suicide, 1, 2); auto status = f.Step();
    Check(status.actionResult == Failed && f.fake.calls[Health] == 0, "ticket cannot target a different ped");
    f.ActionNow(Suicide, 2); status = f.Step();
    Check(status.actionNonce == 2 && status.actionResult == Observed && f.fake.entities[1].dead, "suicide ACK requires observed death");
    Check(f.fake.entities[1].damageable && !f.fake.entities[1].collision, "suicide restores owned protection and flight");
    const int calls = f.fake.calls[Health]; f.fake.entities[1].dead = false;
    ++f.input.settings.revision; f.Step();
    Check(f.fake.calls[Health] == calls, "a repeated nonce never repeats the action");
    Check(f.fake.entities[1].damageable && !f.fake.entities[1].collision, "new snapshots with stale enabled toggles do not undo suicide disarming");
    f.input.settings.flags = 0; ++f.input.settings.revision; f.Step();
    f.input.settings.flags = GodMode; ++f.input.settings.revision; f.Step();
    Check(!f.fake.entities[1].damageable, "explicit off then on can re-enable protection");
}
void ActionObservationTimeoutAndPedRace() {
    Fixture f; f.ReadyNow(); f.fake.ignoreHealth = true; f.ActionNow(Suicide);
    Check(f.Step().actionResult == Pending, "native return alone is not an action ACK");
    Check(f.Step(2001).actionResult == Failed && f.fake.calls[Health] == 1, "unobserved death fails without repeating the kill");
    Fixture changed; changed.ReadyNow(); changed.fake.ignoreHealth = true; changed.ActionNow(Suicide); changed.Step();
    changed.fake.localPed = 2; const auto status = changed.Step();
    Check(status.actionResult == Failed && changed.fake.healthTargets.size() == 1 && changed.fake.healthTargets[0] == 1,
        "pending action is invalidated when the local ped changes");
}
void ReviveIsNativeOnlyAndBoundToPed() {
    Fixture f; f.fake.entities[1].dead = true; f.ReadyNow(); f.ActionNow(Revive);
    auto status = f.Step();
    Check(status.actionResult == Observed && !f.fake.entities[1].dead && f.fake.calls[ReviveNative] == 1, "native resurrection observes an alive ped");
    Check(f.fake.healthTargets.size() == 1 && f.fake.healthTargets[0] == 1, "revive health targets the requested self ped");
    f.Step(); Check(f.fake.calls[ReviveNative] == 1, "revive is not repeated by a stale action snapshot");
    Fixture replaced; replaced.fake.entities[1].dead = true; replaced.fake.replaceOnRevive = true;
    replaced.ReadyNow(); replaced.ActionNow(Revive); status = replaced.Step();
    Check(status.actionResult == Failed && replaced.fake.healthTargets.empty(), "resurrection cannot continue onto a changed ped");
}
void InvalidSettingsAndUnavailableInvoker() {
    Fixture f; f.input.settings.speed = 51;
    Check(f.ReadyNow().error == EffectFailure && f.fake.calls[SetCoords] == 0, "invalid speed is rejected before writes");
    Fixture unavailable; unavailable.fake.enabled = false;
    Check(unavailable.Step().error == Unavailable && unavailable.engine.Status().capabilities == 0, "missing native invocation cannot advertise capabilities");
    Fixture build; build.fake.build = 3095;
    Check(build.ReadyNow().error == Unavailable && build.engine.Status().capabilities == 0, "a different observed build cannot advertise support");
}
void NativeAvailabilityIsSpecificAndDoesNotTrustStaleResults() {
    Fixture required; required.fake.missing.insert(Model);
    const auto unavailable = required.ReadyNow();
    Check(unavailable.error == Unavailable && unavailable.capabilities == 0, "missing common native blocks the module");
    Check(required.fake.calls[Model] == 0, "a missing native is never invoked even if its result buffer could look valid");

    const std::pair<std::uint64_t, std::uint32_t> optional[] = {
        {SetDamageable, CapabilityGodMode}, {SetVisible, CapabilityInvisible},
        {SetCollision, CapabilityNoclip}, {SetClip, CapabilityFreezeAmmo},
        {PositionFrozen, CapabilityNoclip}, {FreezePosition, CapabilityNoclip},
        {Blood, CapabilityRevive}, {Health, CapabilityRevive | CapabilitySuicide},
        {Coords, CapabilityNoclip | CapabilityRevive}
    };
    for (const auto& feature : optional) {
        Fixture f; f.fake.missing.insert(feature.first);
        const auto status = f.ReadyNow();
        Check((status.stateFlags & Ready) && status.capabilities == (KnownCapabilities & ~feature.second),
            "missing optional dependency only removes its affected feature capabilities");
        Check(f.fake.calls[feature.first] == 0, "missing optional native never relies on a stale return buffer");
    }
    Fixture requested; requested.fake.missing.insert(SetClip);
    requested.input.settings.flags = FreezeAmmo | GodMode;
    auto status = requested.ReadyNow();
    Check((status.stateFlags & Ready) && !(status.stateFlags & SettingsActive) && !status.appliedRevision,
        "an unsupported requested feature cannot acknowledge the whole snapshot as active");
    Check(!requested.fake.entities[1].damageable && requested.fake.calls[Clip] == 0,
        "supported effects continue without calling dependencies of the missing feature");
    requested.input.settings.flags = GodMode; ++requested.input.settings.revision; status = requested.Step();
    Check(status.appliedRevision == requested.input.settings.revision && (status.stateFlags & SettingsActive),
        "a compatible fresh settings revision can be acknowledged normally");

    Fixture action; action.fake.missing.insert(Health); action.ReadyNow(); action.ActionNow(Suicide);
    status = action.Step();
    Check(status.actionResult == Failed && action.fake.calls[Health] == 0,
        "an action with a missing handler fails without attempting native execution");
}
void ActionQueueDoesNotReusePreviousEpochObservations() {
    ControlSnapshot control; RuntimeStatus runtime;
    control.actionNonce = 1; runtime.actionNonce = 100;
    Check(HasQueuedAction(control, runtime, 4, 5), "old-epoch high nonce cannot consume a new queued action");
    control.actionNonce = 0;
    Check(!HasQueuedAction(control, runtime, 4, 5), "new epoch with no action has an empty queue");
    control.actionNonce = 2; runtime.actionNonce = 1;
    Check(HasQueuedAction(control, runtime, 5, 5), "same-epoch action is queued until its nonce is observed");
    runtime.actionNonce = 2;
    Check(!HasQueuedAction(control, runtime, 5, 5), "matching-epoch nonce acknowledgement empties the queue");
}
void NativeWritesRequireObservableReadback() {
    Fixture movement; movement.input.settings.flags = Noclip; movement.fake.forward = -1;
    movement.fake.ignoreCoordinates = true;
    auto status = movement.ReadyNow();
    Check(status.error == EffectFailure && !(status.stateFlags & SettingsActive),
        "an ignored coordinate setter cannot report movement observed");
    Check(!movement.fake.entities[1].collision, "failed movement observation restores owned collision state");

    Fixture clip; clip.input.settings.flags = FreezeAmmo; clip.ReadyNow();
    clip.fake.clip = 9; clip.fake.ignoreClip = true; status = clip.Step();
    Check(status.error == EffectFailure && !(status.stateFlags & SettingsActive),
        "a true ammo setter result is insufficient when clip readback did not change");
}
void PublicExportLookupRequiresAnUnambiguousValidPeExport() {
    constexpr const char* prefix = "?GetNativeHandler@scrEngine@rage@@";
    std::vector<std::uint8_t> image(4096);
    const auto word = [&](std::size_t at, std::uint16_t value) { std::memcpy(image.data() + at, &value, 2); };
    const auto dword = [&](std::size_t at, std::uint32_t value) { std::memcpy(image.data() + at, &value, 4); };
    const auto name = [&](std::size_t at, const char* value) { std::strcpy(reinterpret_cast<char*>(image.data() + at), value); };
    word(0, 0x5A4D); dword(0x3C, 128); dword(128, 0x4550); word(148, 240); word(152, 0x20B);
    dword(208, static_cast<std::uint32_t>(image.size())); dword(260, 16); dword(264, 512); dword(268, 128);
    dword(532, 2); dword(536, 2); dword(540, 768); dword(544, 800); dword(548, 832);
    dword(768, 2048); dword(772, 2050); dword(800, 1024); dword(804, 1200); word(832, 0); word(834, 1);
    name(1024, "?GetNativeHandler@scrEngine@rage@@SAPEAX_K@Z"); name(1200, "?GetNativeHandler@@YAPEAX_K@Z");
    Check(FindUniqueExportRva(image.data(), image.size(), prefix) == 2048,
        "lookup selects the unique public class and namespace export, ignoring global lookalikes");
    name(1200, "?GetNativeHandler@scrEngine@rage@@SECOND_OVERLOAD");
    Check(!FindUniqueExportRva(image.data(), image.size(), prefix), "ambiguous matching public exports are rejected");
    name(1200, "unrelated"); dword(768, 600);
    Check(!FindUniqueExportRva(image.data(), image.size(), prefix), "a forwarded export is not used as executable code");
    dword(768, 2048); word(832, 3);
    Check(!FindUniqueExportRva(image.data(), image.size(), prefix), "out-of-range export ordinal is rejected");
    word(832, 0); dword(800, 5000);
    Check(!FindUniqueExportRva(image.data(), image.size(), prefix), "out-of-image export name is rejected");
    dword(800, 1024); word(152, 0x10B);
    Check(!FindUniqueExportRva(image.data(), image.size(), prefix), "non-64-bit PE is rejected");
    word(152, 0x20B);
    Check(!FindUniqueExportRva(image.data(), image.size() - 1, prefix), "inconsistent image size is rejected");
    Check(!FindUniqueExportRva(nullptr, 0, prefix), "missing module image has no public lookup");
}
} // namespace

int main() {
    const std::pair<const char*, void(*)()> tests[] = {
        {"preflight and queue", PreflightAndQueuedSettings}, {"timer and gate", TimerMustAdvanceAndGateLossCleansUp},
        {"snapshot restoration", SnapshotRestorationAndExternalChanges}, {"independent cleanup", CleanupDoesNotShortCircuit},
        {"persistent restoration", CleanupRetainsSnapshotsUntilConfirmed},
        {"frame movement", MovementUsesCurrentFrameAndSpeed}, {"flight prerequisites", NoclipPrerequisitesAndOriginalCollision},
        {"flight hover and focus", FlightHoversWithoutReadingBackgroundKeys},
        {"ped replacement", PedReplacementDoesNotReapplyOldSettings}, {"effect verification", FailureRequiresFreshRevision},
        {"lease epochs", LeaseAndEpochRevocation}, {"ammo observations", AmmoPreservesPickupsAndHasNoPersistentFlags},
        {"suicide tickets", SuicideRequiresMatchingPedAndNonce}, {"action timeout and race", ActionObservationTimeoutAndPedRace},
        {"native revive", ReviveIsNativeOnlyAndBoundToPed}, {"invalid inputs", InvalidSettingsAndUnavailableInvoker},
        {"native availability", NativeAvailabilityIsSpecificAndDoesNotTrustStaleResults},
        {"setter observations", NativeWritesRequireObservableReadback},
        {"action queue epochs", ActionQueueDoesNotReusePreviousEpochObservations},
        {"public export lookup", PublicExportLookupRequiresAnUnambiguousValidPeExport}
    };
    for (const auto& test : tests) {
        try { test.second(); std::cout << "PASS " << test.first << '\n'; }
        catch (const std::exception& error) { std::cerr << "FAIL " << test.first << ": " << error.what() << '\n'; return EXIT_FAILURE; }
    }
    std::cout << sizeof(tests) / sizeof(tests[0]) << " suites / " << assertions
        << " assertions passed (fake natives and synthetic PE; no gameplay validation).\n";
}
