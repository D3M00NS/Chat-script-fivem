#include "DirectedFire.h"
#include <algorithm>
#include <cmath>
#include <iostream>
#include <map>
#include <set>
#include <stdexcept>
#include <string>
#include <vector>

using namespace zyon;
namespace {
int assertions = 0;
constexpr std::uint64_t PlayerId = 0x4F8644AF03D0E0D6ULL, LocalPed = 0xD80958FC74E988A6ULL;
constexpr std::uint64_t ActivePlayer = 0xB8DFD30D6973E135ULL, PlayerPed = 0x43A66C31C68491C0ULL;
constexpr std::uint64_t Exists = 0x7239B21A38F536BAULL, IsPed = 0x524AC5ECEA15343EULL;
constexpr std::uint64_t Model = 0x9F47B058362C84B5ULL, Dead = 0x3317DEDB88C95038ULL;
constexpr std::uint64_t Vehicle = 0x997ABD671D25CA0BULL, Attached = 0xB346476EF1A64897ULL;
constexpr std::uint64_t Reloading = 0x24B100C68C645951ULL, Weapon = 0x0A6DB4965674D243ULL;
constexpr std::uint64_t DamageType = 0x3BE0BB12D25FB305ULL;
constexpr std::uint64_t TotalAmmo = 0x015A522136D7F951ULL, ClipAmmo = 0x2E1202248937775CULL;
constexpr std::uint64_t Pressed = 0xF3A21BCD95725A4AULL, DisabledPressed = 0xE2587F8CBBD87B1DULL;
constexpr std::uint64_t DisableControl = 0xFE99B66D079CF6BCULL, DisableFiring = 0x5E6CC07646BBEAB8ULL;
constexpr std::uint64_t ScreenSize = 0x873C9F3104101DD3ULL, ScreenPoint = 0x34E82F05DF2974F5ULL;
constexpr std::uint64_t BoneCoords = 0x17C07FC640E86B4EULL, Los = 0xFCDFF7B72D23A1ACULL;
constexpr std::uint64_t Shoot = 0x96A05E4FB321B1BAULL;
constexpr std::uint64_t SetCamRotation = 0x85973643155D0B07ULL;
constexpr std::uint64_t SyntheticBullet = 0x867654CBC7606F2CULL;

void Check(bool value, const char* message) {
    ++assertions;
    if (!value) throw std::runtime_error(message);
}
struct Entity {
    std::uint32_t model = 123;
    bool exists = true, isPed = true, dead = false, clear = true, projected = true;
    float screenX = 0.5f, screenY = 0.5f;
};
struct Fake final : INativeInvoker {
    std::map<std::uint32_t, Entity> entities{{10, {}}, {20, {}}, {30, {}}};
    std::map<std::uint32_t, std::uint32_t> players{{0, 10}, {1, 20}};
    std::map<std::uint64_t, int> calls;
    std::map<std::uint32_t, bool> controls{{24, false}, {25, true}};
    std::vector<std::uint64_t> order;
    std::set<std::uint64_t> missing;
    std::uint64_t fail = 0;
    std::uint32_t localPed = 10, weapon = 0x1B06D571u, damageType = 3, lastBone = 0, lastShotPed = 0;
    int total = 20, clip = 10, width = 1920, height = 1080;
    bool vehicle = false, attached = false, reloading = false, consumeAmmo = true;
    bool changedLocalDuringEnumeration = false, changedWeaponDuringEnumeration = false;
    bool changedTargetDuringEnumeration = false, changedTargetModelDuringEnumeration = false;
    bool invalidProjection = false, invalidClip = false, argumentsValid = true;
    float lastShotX = 0;
    bool HasNative(std::uint64_t hash) const noexcept override {
        if (missing.count(hash)) return false;
        switch (hash) {
        case PlayerId: case LocalPed: case ActivePlayer: case PlayerPed: case Exists: case IsPed:
        case Model: case Dead: case Vehicle: case Attached: case Reloading: case Weapon: case DamageType:
        case TotalAmmo: case ClipAmmo: case Pressed: case DisabledPressed: case DisableControl:
        case DisableFiring: case ScreenSize: case ScreenPoint: case BoneCoords: case Los: case Shoot: return true;
        default: return false;
        }
    }
    bool Invoke(std::uint64_t hash, const std::uint64_t* args, std::size_t count,
                NativeResult& result) noexcept override {
        ++calls[hash]; order.push_back(hash); result = {};
        if (!HasNative(hash) || hash == fail) return false;
        const auto require = [&](std::size_t expected) {
            if (count != expected) argumentsValid = false;
            return count == expected;
        };
        const auto integer = [&](std::uint32_t value) { result.words[0] = value; return true; };
        if (hash == PlayerId) return require(0) && integer(0);
        if (hash == LocalPed) return require(0) && integer(localPed);
        if (hash == ActivePlayer) {
            if (!require(1)) return false;
            if (args[0] == 255) {
                if (changedLocalDuringEnumeration) localPed = 30;
                if (changedWeaponDuringEnumeration) weapon = 444;
                if (changedTargetDuringEnumeration) players[1] = 30;
                if (changedTargetModelDuringEnumeration) entities[20].model = 456;
            }
            return integer(players.count(static_cast<std::uint32_t>(args[0])) != 0);
        }
        if (hash == PlayerPed) return require(1) && integer(players[static_cast<std::uint32_t>(args[0])]);
        if (hash == Pressed || hash == DisabledPressed) {
            if (!require(2)) return false;
            // Normal reads do not see disabled attack; disabled reads still do.
            return integer((hash == DisabledPressed || args[1] != 24) && controls[static_cast<std::uint32_t>(args[1])]);
        }
        if (hash == DisableControl) return require(3) && args[0] == 0 && args[2] == 1;
        if (hash == DisableFiring) return require(2) && args[0] == 0 && args[1] == 1;
        if (hash == DamageType) return require(1) && integer(damageType);
        if (hash == ScreenSize) {
            if (!require(2) || !args[0] || !args[1]) return false;
            *reinterpret_cast<int*>(static_cast<std::uintptr_t>(args[0])) = width;
            *reinterpret_cast<int*>(static_cast<std::uintptr_t>(args[1])) = height;
            return true;
        }
        if (hash == ScreenPoint) {
            if (!require(5) || !args[3] || !args[4]) return false;
            const auto id = static_cast<std::uint32_t>(NativeFloat(args[0]));
            const auto& entity = entities[id];
            *reinterpret_cast<float*>(static_cast<std::uintptr_t>(args[3])) = invalidProjection ? std::nanf("") : entity.screenX;
            *reinterpret_cast<float*>(static_cast<std::uintptr_t>(args[4])) = entity.screenY;
            return integer(entity.projected);
        }
        if (hash == Los) return require(3) && args[2] == 17 && integer(entities[static_cast<std::uint32_t>(args[1])].clear);
        if (hash == Shoot) {
            if (!require(5) || args[4] != 1) return false;
            lastShotPed = static_cast<std::uint32_t>(args[0]); lastShotX = NativeFloat(args[1]);
            if (consumeAmmo) { --total; --clip; }
            return true;
        }
        if (!count) return false;
        const auto id = static_cast<std::uint32_t>(args[0]);
        if (hash == Exists) return require(1) && integer(entities.count(id) && entities[id].exists);
        if (!entities.count(id)) return false;
        const auto& entity = entities[id];
        switch (hash) {
        case IsPed: return require(1) && integer(entity.isPed);
        case Model: return require(1) && integer(entity.model);
        case Dead: return require(2) && args[1] == 0 && integer(entity.dead);
        case Vehicle: return require(2) && args[1] == 0 && integer(vehicle);
        case Attached: return require(1) && integer(attached);
        case Reloading: return require(1) && integer(reloading);
        case Weapon: return require(1) && integer(weapon);
        case TotalAmmo: return require(2) && integer(static_cast<std::uint32_t>(total));
        case ClipAmmo:
            if (!require(3) || !args[2]) return false;
            *reinterpret_cast<int*>(static_cast<std::uintptr_t>(args[2])) = invalidClip ? -1 : clip;
            return integer(true);
        case BoneCoords:
            if (!require(5) || args[2] || args[3] || args[4]) return false;
            lastBone = static_cast<std::uint32_t>(args[1]);
            result.words[0] = NativeArg(static_cast<float>(id)); result.words[1] = NativeArg(1.0f); result.words[2] = NativeArg(2.0f);
            return true;
        default: return false;
        }
    }
};
struct Fixture {
    Fake fake;
    DirectedFire fire{fake};
    DirectedFireSettings settings;
    char message[160]{};
    std::uint64_t now = 1000;
    Fixture() { settings.enabled = true; }
    bool Step(bool alive = true, bool focused = true, std::uint32_t ped = 10) {
        return fire.Step(ped, alive, focused, settings, now, message);
    }
    void Arm() { fake.controls[24] = false; Check(Step(), "release frame succeeds"); }
    void Fire() { fake.controls[24] = true; Check(Step(), "press frame succeeds"); }
};

void AvailabilityAndGates() {
    Fixture f;
    Check(f.fire.Available(), "complete native set is available");
    f.fake.missing.insert(Shoot);
    Check(!f.fire.Available(), "missing shoot native removes capability");
    Check(!f.Step() && f.fake.order.empty(), "unavailable prototype does not invoke natives");
    Fixture disabled; disabled.settings.enabled = false;
    Check(disabled.Step() && disabled.fake.order.empty(), "disabled feature has no native effect");
    Fixture focus;
    Check(focus.Step(true, false) && !focus.fake.calls[DisableFiring] && !focus.fake.calls[DisableControl] &&
          !focus.fake.calls[Shoot], "unfocused game never suppresses input or dispatches");
    focus.fake.order.clear();
    Check(focus.Step(false) && focus.fake.order.empty(), "caller life gate prevents input effects");
    Fixture changed; changed.fake.localPed = 30;
    Check(changed.Step() && !changed.fake.calls[DisableFiring], "stale supplied ped does not suppress another player");
    Fixture unsupported; unsupported.settings.onlyPlayers = false;
    Check(unsupported.Step() && unsupported.fake.calls[DisableFiring] == 1, "unsupported NPC mode remains explicitly blocked");
    Check(std::string(unsupported.message).find("players only") != std::string::npos, "NPC limitation is visible");
}
void ReleaseAndInput() {
    Fixture f; f.fake.controls[24] = true;
    Check(f.Step() && !f.fake.calls[Shoot], "held trigger on enable cannot dispatch");
    f.now += 1000;
    Check(f.Step() && !f.fake.calls[Shoot], "held trigger cannot auto-arm");
    f.Arm(); f.Fire();
    Check(f.fake.calls[Shoot] == 1, "released then pressed input dispatches one native request");
    f.now += 1000;
    Check(f.Step() && f.fake.calls[Shoot] == 1, "held trigger never repeats dispatch");
    Check(f.fake.lastShotPed == 10 && f.fake.lastShotX == 20, "shot owner and target coordinates use native handles");
    Check(f.fake.lastBone == 31086, "head uses documented bone ID");
    Check(f.fake.argumentsValid, "all invocations follow explicit parameter counts");
    const auto shot = std::find(f.fake.order.begin(), f.fake.order.end(), Shoot);
    Check(std::find(f.fake.order.begin(), shot, DisableFiring) != shot, "normal firing suppressed before dispatch");
    Check(std::find(f.fake.order.begin(), shot, DisableControl) != shot, "attack control suppressed before dispatch");
    Check(!f.fake.calls[SetCamRotation] && !f.fake.calls[SyntheticBullet], "prototype neither writes camera nor creates synthetic bullets");
    Check(std::string(f.message).find("Last experimental request:") != std::string::npos,
          "held-input frame preserves the observed request without repeating it");
    Fixture activation; activation.fake.controls[25] = false;
    activation.Arm(); activation.Fire();
    Check(!activation.fake.calls[Shoot], "missing activation blocks shot");
    activation.fake.controls[25] = true;
    Check(activation.Step() && !activation.fake.calls[Shoot], "activation while trigger held cannot queue shot");
    activation.Arm(); activation.Fire();
    Check(activation.fake.calls[Shoot] == 1, "fresh trigger after activation can dispatch");
    for (std::uint32_t key = 1; key < 5; ++key) {
        Fixture alternate; alternate.settings.activation = key;
        const std::uint32_t control[] = {25, 24, 19, 21, 36};
        alternate.fake.controls[control[key]] = true;
        alternate.Arm(); alternate.Fire();
        Check(alternate.fake.calls[Shoot] == 1, "all configured activation controls are supported");
    }
}
void CooldownAndReset() {
    Fixture f; f.Arm(); f.Fire();
    f.now += 249; f.Arm(); f.Fire();
    Check(f.fake.calls[Shoot] == 1, "second edge before 250 ms is blocked");
    f.now += 1; f.Arm(); f.Fire();
    Check(f.fake.calls[Shoot] == 2, "second edge after 250 ms can dispatch");
    f.fire.Reset(); f.now += 1; f.Arm(); f.Fire();
    Check(f.fake.calls[Shoot] == 2, "reset cannot remove cooldown");
    f.now -= 1000; f.Arm(); f.Fire();
    Check(f.fake.calls[Shoot] == 2, "backward timer cannot bypass cooldown");
    f.settings.enabled = false; Check(f.Step(), "disable frame succeeds");
    f.settings.enabled = true; f.now += 5000;
    Check(f.Step() && f.fake.calls[Shoot] == 2, "re-enabled held input requires release");
}
void TargetsAndProjection() {
    Fixture blocked; blocked.fake.entities[20].clear = false; blocked.Arm(); blocked.Fire();
    Check(!blocked.fake.calls[Shoot] && blocked.fake.calls[DisableFiring] == 2, "no LOS blocks dispatch while normal fire stays suppressed");
    Fixture allowed; allowed.settings.onlyVisible = false; allowed.fake.entities[20].clear = false;
    allowed.Arm(); allowed.Fire();
    Check(allowed.fake.calls[Shoot] == 1 && !allowed.fake.calls[Los], "visibility option controls native LOS gate");
    Fixture fov; fov.fake.entities[20].screenX = 0.6f; fov.Arm(); fov.Fire();
    Check(!fov.fake.calls[Shoot], "FOV is pixels, not normalized-coordinate units");
    Fixture edge; edge.fake.entities[20].screenX = 0.55f; edge.Arm(); edge.Fire();
    Check(edge.fake.calls[Shoot] == 1, "player within 100 screen pixels is eligible");
    Fixture dead; dead.fake.entities[20].dead = true; dead.Arm(); dead.Fire();
    Check(!dead.fake.calls[Shoot], "dead target is excluded");
    Fixture offscreen; offscreen.fake.entities[20].projected = false; offscreen.Arm(); offscreen.Fire();
    Check(!offscreen.fake.calls[Shoot], "failed native projection means no eligible target");
    Fixture nearest; nearest.fake.players[2] = 30; nearest.fake.entities[20].screenX = 0.53f;
    nearest.fake.entities[30].screenX = 0.51f; nearest.settings.bone = 2;
    nearest.Arm(); nearest.Fire();
    Check(nearest.fake.lastShotX == 30 && nearest.fake.lastBone == 24818, "nearest player and configured chest bone are selected");
}
void LocalAndFreshIdentity() {
    Fixture vehicle; vehicle.fake.vehicle = true; vehicle.Arm(); vehicle.Fire();
    Check(!vehicle.fake.calls[Shoot], "vehicle gate prevents dispatch");
    Fixture reload; reload.fake.reloading = true; reload.Arm(); reload.Fire();
    Check(!reload.fake.calls[Shoot], "reload gate prevents dispatch");
    Fixture attached; attached.fake.attached = true; attached.Arm(); attached.Fire();
    Check(!attached.fake.calls[Shoot], "attached ped cannot dispatch");
    Fixture noAmmo; noAmmo.fake.clip = 0; noAmmo.Arm(); noAmmo.Fire();
    Check(!noAmmo.fake.calls[Shoot], "empty clip prevents dispatch");
    Fixture explosive; explosive.fake.damageType = 5; explosive.Arm(); explosive.Fire();
    Check(!explosive.fake.calls[Shoot], "non-bullet weapon is excluded");
    Fixture changedPed; changedPed.fake.changedLocalDuringEnumeration = true; changedPed.Arm(); changedPed.Fire();
    Check(!changedPed.fake.calls[Shoot], "local ped changed during selection cannot dispatch");
    Fixture changedWeapon; changedWeapon.fake.changedWeaponDuringEnumeration = true; changedWeapon.Arm(); changedWeapon.Fire();
    Check(!changedWeapon.fake.calls[Shoot], "weapon changed during selection cannot dispatch");
    Fixture changedTarget; changedTarget.fake.changedTargetDuringEnumeration = true; changedTarget.Arm(); changedTarget.Fire();
    Check(!changedTarget.fake.calls[Shoot], "target player handle changed during selection cannot dispatch");
    Fixture reused; reused.fake.changedTargetModelDuringEnumeration = true; reused.Arm(); reused.Fire();
    Check(!reused.fake.calls[Shoot], "reused target model is rejected");
}
void DiagnosticsAndFailures() {
    Fixture f; f.Arm(); f.Fire();
    Check(std::string(f.message).find("ammo 20->19, clip 10->9") != std::string::npos, "dispatch records observed native ammunition changes");
    Check(std::string(f.message).find("hit not confirmed") != std::string::npos, "dispatch never claims confirmed hit");
    Fixture ignored; ignored.fake.consumeAmmo = false; ignored.Arm(); ignored.Fire();
    Check(ignored.fire.PendingConsumption(), "void native with no immediate ammunition effect waits for observation");
    Check(std::string(ignored.message).find("Shot/hit not confirmed") != std::string::npos, "pending observation never claims a shot");
    ignored.now += 499;
    Check(ignored.Step() && ignored.fire.PendingConsumption(), "ammunition observation remains pending for bounded interval");
    ignored.now += 1;
    Check(!ignored.Step() && !ignored.fire.PendingConsumption(), "unchanged ammunition times out after 500 ms with concrete failure");
    Check(std::string(ignored.message).find("no ammo consumption") != std::string::npos, "timeout states that native dispatch was not observed");
    Fixture delayed; delayed.fake.consumeAmmo = false; delayed.Arm(); delayed.Fire();
    --delayed.fake.total; --delayed.fake.clip; delayed.now += 100;
    Check(delayed.Step() && !delayed.fire.PendingConsumption(), "delayed native ammo decrease completes observation");
    Check(std::string(delayed.message).find("Consumption observed; hit not confirmed") != std::string::npos, "consumption observation is distinct from a hit");
    for (int frame = 0; frame < 4; ++frame) {
        delayed.now += 16; delayed.fake.controls[24] = frame < 2;
        Check(delayed.Step() && std::string(delayed.message).find("Last experimental request:") != std::string::npos &&
              std::string(delayed.message).find("Consumption observed; hit not confirmed") != std::string::npos,
              "four subsequent held/idle frames retain the consumption result for desktop polling");
    }
    delayed.now += 500; delayed.fake.controls[25] = false; delayed.fake.controls[24] = true;
    Check(delayed.Step() && std::string(delayed.message).find("Consumption observed") == std::string::npos,
          "a new cancelled attempt supersedes the previous consumption result");
    delayed.fire.Reset(); delayed.fake.controls[24] = false;
    Check(delayed.Step() && std::string(delayed.message).find("Last experimental request:") == std::string::npos,
          "reset clears the stored diagnostic result");
    Fixture suppress; suppress.fake.fail = DisableFiring;
    Check(!suppress.Step() && !suppress.fake.calls[Shoot], "failed suppression is concrete error and prevents dispatch");
    Fixture shoot; shoot.Arm(); shoot.fake.fail = Shoot; shoot.fake.controls[24] = true;
    Check(!shoot.Step() && shoot.fake.calls[Shoot] == 1, "failed dispatch reports error without repeated invocation");
    Check(shoot.Step() && shoot.fake.calls[Shoot] == 1, "failed dispatch is not retried while trigger held");
    Fixture invalid; invalid.Arm(); invalid.fake.invalidProjection = true; invalid.fake.controls[24] = true;
    Check(!invalid.Step() && !invalid.fake.calls[Shoot], "nonfinite projection is a concrete failure");
    Fixture clip; clip.Arm(); clip.fake.invalidClip = true; clip.fake.controls[24] = true;
    Check(!clip.Step() && !clip.fake.calls[Shoot], "invalid ammunition readback is rejected");
}
void FocusKeepsDiagnosticAndDisarms() {
    Fixture f; f.Arm(); f.Fire();
    const auto suppressed = f.fake.calls[DisableFiring];
    const auto disabledControls = f.fake.calls[DisableControl];
    for (int frame = 0; frame < 4; ++frame) {
        f.now += 100;
        Check(f.Step(true, false) && std::string(f.message).find("Consumption observed; hit not confirmed") != std::string::npos,
              "alt-tab idle frames preserve the observed diagnostic");
    }
    Check(f.fake.calls[DisableFiring] == suppressed && f.fake.calls[DisableControl] == disabledControls &&
          f.fake.calls[Shoot] == 1, "unfocused diagnostic frames do not suppress input or dispatch");
    Check(f.Step() && f.fake.calls[Shoot] == 1, "regained focus with held fire cannot dispatch");
    f.Arm(); f.Fire();
    Check(f.fake.calls[Shoot] == 2, "release then press after focus regain permits the next request");

    Fixture delayed; delayed.fake.consumeAmmo = false; delayed.Arm(); delayed.Fire();
    --delayed.fake.total; --delayed.fake.clip; delayed.now += 100;
    const auto beforeSuppression = delayed.fake.calls[DisableFiring];
    Check(delayed.Step(true, false) && !delayed.fire.PendingConsumption() &&
          std::string(delayed.message).find("Consumption observed; hit not confirmed") != std::string::npos,
          "pending consumption can finish while unfocused without a new shot");
    Check(delayed.fake.calls[DisableFiring] == beforeSuppression && delayed.fake.calls[Shoot] == 1,
          "unfocused consumption readback does not suppress firing or dispatch again");
    delayed.fake.entities[10].dead = true;
    Check(delayed.Step(true, false) && std::string(delayed.message).find("Consumption observed") == std::string::npos,
          "native-confirmed death clears the stored result while unfocused");
}
} // namespace

int main() {
    try {
        AvailabilityAndGates(); ReleaseAndInput(); CooldownAndReset(); TargetsAndProjection();
        LocalAndFreshIdentity(); DiagnosticsAndFailures(); FocusKeepsDiagnosticAndDisarms();
        std::cout << "7 directed-fire native suites passed (" << assertions << " assertions).\n";
        return 0;
    } catch (const std::exception& error) {
        std::cerr << "Directed-fire test failed: " << error.what() << '\n';
        return 1;
    }
}
