#include "DirectedFire.h"
#include <cmath>
#include <cstdio>
#include <limits>

namespace zyon {
namespace {
// GTA native contracts from citizenfx/natives; no raw addresses or camera writes.
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
constexpr std::uint32_t Unarmed = 0xA2719263u;
constexpr std::uint32_t Bones[] = {31086, 39317, 24818}; // Head, neck, spine3/chest.
constexpr std::uint32_t ActivationControls[] = {25, 24, 19, 21, 36};
constexpr std::uint64_t MinimumInterval = 250;

void Message(char (&message)[160], const char* text) {
    std::snprintf(message, sizeof(message), "%s", text);
}
template<typename T> std::uint64_t Pointer(T* pointer) {
    return static_cast<std::uint64_t>(reinterpret_cast<std::uintptr_t>(pointer));
}
bool Finite(float value) { return std::isfinite(value) && std::abs(value) < 1000000.0f; }
} // namespace

bool DirectedFire::Available() const {
    for (const auto hash : {PlayerId, LocalPed, ActivePlayer, PlayerPed, Exists, IsPed, Model, Dead,
                           Vehicle, Attached, Reloading, Weapon, DamageType, TotalAmmo, ClipAmmo, Pressed,
                           DisabledPressed, DisableControl, DisableFiring, ScreenSize, ScreenPoint,
                           BoneCoords, Los, Shoot})
        if (!natives_.HasNative(hash)) return false;
    return true;
}

void DirectedFire::Reset() {
    ped_ = model_ = weapon_ = 0;
    armed_ = triggerDown_ = false;
    pendingConsumption_ = false;
    lastResult_[0] = '\0';
    // Preserve the last dispatch across focus/settings/ped transitions. A reset
    // must not remove the conservative cooldown between native requests.
}

bool DirectedFire::Call(std::uint64_t hash, std::initializer_list<std::uint64_t> args,
                        NativeResult* result) {
    NativeResult output{};
    const bool ok = natives_.HasNative(hash) && natives_.Invoke(hash, args.begin(), args.size(), output);
    if (result) *result = output;
    return ok;
}
bool DirectedFire::Integer(std::uint64_t hash, std::initializer_list<std::uint64_t> args,
                           std::uint32_t& value) {
    NativeResult result{};
    if (!Call(hash, args, &result)) return false;
    value = static_cast<std::uint32_t>(result.words[0]);
    return true;
}
bool DirectedFire::Boolean(std::uint64_t hash, std::initializer_list<std::uint64_t> args,
                           bool& value) {
    std::uint32_t raw = 0;
    if (!Integer(hash, args, raw) || raw > 1) return false;
    value = raw != 0;
    return true;
}
bool DirectedFire::Entity(std::uint32_t ped, std::uint32_t& model, bool& valid) {
    valid = false;
    model = 0;
    if (!ped) return true;
    bool exists = false, isPed = false;
    if (!Boolean(Exists, {ped}, exists)) return false;
    if (!exists) return true;
    if (!Boolean(IsPed, {ped}, isPed)) return false;
    if (!isPed) return true;
    if (!Integer(Model, {ped}, model)) return false;
    valid = model != 0;
    return true;
}
bool DirectedFire::Ammo(std::uint32_t ped, std::uint32_t weapon, int& total, int& clip) {
    std::uint32_t rawTotal = 0;
    bool hasClip = false;
    clip = -1;
    if (!Integer(TotalAmmo, {ped, weapon}, rawTotal) || rawTotal > 1000000 ||
        !Boolean(ClipAmmo, {ped, weapon, Pointer(&clip)}, hasClip)) return false;
    total = static_cast<int>(rawTotal);
    if (!hasClip) { clip = 0; return true; }
    return clip >= 0 && clip <= 1000000;
}
bool DirectedFire::Input(std::uint32_t control, bool& pressed) {
    bool normal = false, disabled = false;
    if (!Boolean(Pressed, {0u, control}, normal) ||
        !Boolean(DisabledPressed, {0u, control}, disabled)) return false;
    pressed = normal || disabled;
    return true;
}
bool DirectedFire::Bone(std::uint32_t ped, std::uint32_t bone, Point& point) {
    NativeResult result{};
    if (!Call(BoneCoords, {ped, Bones[bone], NativeArg(0.0f), NativeArg(0.0f), NativeArg(0.0f)}, &result))
        return false;
    point = {NativeFloat(result.words[0]), NativeFloat(result.words[1]), NativeFloat(result.words[2])};
    return Finite(point.x) && Finite(point.y) && Finite(point.z);
}
bool DirectedFire::InFov(const Point& point, int width, int height, std::uint32_t fov,
                        bool& inside, float& squaredDistance) {
    float x = std::numeric_limits<float>::quiet_NaN(), y = x;
    bool projected = false;
    if (!Boolean(ScreenPoint, {NativeArg(point.x), NativeArg(point.y), NativeArg(point.z),
                              Pointer(&x), Pointer(&y)}, projected)) return false;
    inside = false;
    squaredDistance = std::numeric_limits<float>::infinity();
    if (!projected) return true;
    if (!std::isfinite(x) || !std::isfinite(y)) return false;
    if (x < 0 || x > 1 || y < 0 || y > 1) return true;
    const float dx = (x - 0.5f) * static_cast<float>(width);
    const float dy = (y - 0.5f) * static_cast<float>(height);
    squaredDistance = dx * dx + dy * dy;
    inside = squaredDistance <= static_cast<float>(fov * fov);
    return true;
}
bool DirectedFire::LocalGate(std::uint32_t ped, std::uint32_t model, std::uint32_t weapon,
                            bool& usable) {
    usable = false;
    std::uint32_t current = 0, currentModel = 0, currentWeapon = 0;
    bool valid = false, dead = false, inVehicle = false, attached = false, reloading = false;
    if (!Integer(LocalPed, {}, current)) return false;
    if (current != ped) return true;
    if (!Entity(ped, currentModel, valid)) return false;
    if (!valid || currentModel != model) return true;
    if (!Boolean(Dead, {ped, NativeArg(false)}, dead) ||
        !Boolean(Vehicle, {ped, NativeArg(false)}, inVehicle) ||
        !Boolean(Attached, {ped}, attached) || !Boolean(Reloading, {ped}, reloading) ||
        !Integer(Weapon, {ped}, currentWeapon)) return false;
    usable = !dead && !inVehicle && !attached && !reloading && currentWeapon == weapon;
    return true;
}

bool DirectedFire::ObserveConsumption(std::uint64_t now, char (&message)[160]) {
    bool usable = false;
    if (!LocalGate(ped_, model_, weapon_, usable)) {
        pendingConsumption_ = false;
        Message(message, "Experimental native dispatched; ped/weapon validation failed during ammo readback. Hit not confirmed.");
        return false;
    }
    if (!usable) {
        Reset();
        Message(message, "Ped/weapon changed after native dispatch; consumption and hit not confirmed. Release fire before retrying.");
        return true;
    }
    int afterTotal = 0, afterClip = 0;
    if (!Ammo(ped_, weapon_, afterTotal, afterClip)) {
        pendingConsumption_ = false;
        Message(message, "Experimental native dispatched; ammunition readback failed. Hit not confirmed.");
        return false;
    }
    if (afterTotal < beforeTotal_ || afterClip < beforeClip_) {
        pendingConsumption_ = false;
        std::snprintf(lastResult_, sizeof(lastResult_),
                      "ammo %d->%d, clip %d->%d. Consumption observed; hit not confirmed.",
                      beforeTotal_, afterTotal, beforeClip_, afterClip);
        std::snprintf(message, sizeof(message),
                      "Experimental native dispatched; ammo %d->%d, clip %d->%d. Consumption observed; hit not confirmed.",
                      beforeTotal_, afterTotal, beforeClip_, afterClip);
        return true;
    }
    if (now < pendingStarted_ || now - pendingStarted_ >= 500) {
        pendingConsumption_ = false;
        Message(message, "Experimental native dispatched but no ammo consumption observed within 500 ms; shot/hit not confirmed.");
        return false;
    }
    Message(message, "Experimental native dispatched; waiting up to 500 ms for ammo consumption. Shot/hit not confirmed.");
    return true;
}

bool DirectedFire::Step(std::uint32_t ped, bool alive, bool focused,
                        const DirectedFireSettings& settings, std::uint64_t now,
                        char (&message)[160]) {
    if (!settings.enabled) { Reset(); Message(message, "Experimental directed fire disabled."); return true; }
    if (!alive || !ped) {
        Reset(); Message(message, "Experimental directed fire waiting for a living local ped."); return true;
    }
    if (!Available()) { Reset(); Message(message, "A required directed-fire native handler is unavailable."); return false; }
    if (!focused) {
        // Alt-tab to save diagnostics must retain the last observation. It must
        // not retain an armed trigger or issue any input suppression/dispatch.
        armed_ = false; triggerDown_ = true;
        std::uint32_t currentPed = 0, currentModel = 0;
        bool validPed = false, dead = false;
        if (!Integer(LocalPed, {}, currentPed) || !Entity(ped, currentModel, validPed) ||
            (validPed && !Boolean(Dead, {ped, NativeArg(false)}, dead))) {
            Reset(); Message(message, "Could not validate the unfocused local ped for diagnostics."); return false;
        }
        if (currentPed != ped || !validPed || dead ||
            (ped_ && (ped_ != ped || model_ != currentModel))) {
            Reset(); Message(message, "Local ped changed or died; experimental request state cleared."); return true;
        }
        if (pendingConsumption_) return ObserveConsumption(now, message);
        if (lastResult_[0]) {
            std::snprintf(message, sizeof(message), "Last experimental request: %.132s", lastResult_);
            return true;
        }
        Message(message, "Waiting for game focus; release fire before another experimental request."); return true;
    }
    std::uint32_t current = 0, model = 0, player = 0;
    bool valid = false;
    if (!Integer(LocalPed, {}, current) || !Entity(ped, model, valid)) {
        Reset(); Message(message, "Could not validate the local ped through native calls."); return false;
    }
    if (current != ped || !valid) {
        Reset(); Message(message, "Local ped changed; release the trigger before retrying."); return true;
    }
    if (!Integer(PlayerId, {}, player) || player > 255 ||
        !Call(DisableFiring, {player, NativeArg(true)}) ||
        !Call(DisableControl, {0u, 24u, NativeArg(true)}) ||
        !Call(DisableControl, {0u, 257u, NativeArg(true)})) {
        Reset(); Message(message, "Could not suppress normal firing; directed dispatch cancelled."); return false;
    }
    if (!settings.onlyPlayers || settings.bone > 2 || settings.activation > 4 ||
        settings.fov < 10 || settings.fov > 500) {
        Reset(); Message(message, "Prototype supports players only and valid bone/key/FOV settings; normal fire suppressed."); return true;
    }
    if (pendingConsumption_) return ObserveConsumption(now, message);
    std::uint32_t weapon = 0;
    bool usable = false;
    if (!Integer(Weapon, {ped}, weapon) || !LocalGate(ped, model, weapon, usable)) {
        Reset(); Message(message, "Could not read weapon/on-foot/reload native gates."); return false;
    }
    if (!usable || !weapon || weapon == Unarmed) {
        Reset(); Message(message, "Waiting for a living, on-foot ped with a ready firearm; normal fire suppressed."); return true;
    }
    std::uint32_t damageType = 0;
    if (!Integer(DamageType, {weapon}, damageType)) {
        Reset(); Message(message, "Could not inspect the selected weapon damage type."); return false;
    }
    if (damageType != 3) {
        Reset(); Message(message, "Prototype accepts bullet firearms only; normal fire suppressed."); return true;
    }
    bool trigger = false, activation = false;
    if (!Input(24, trigger) || !Input(ActivationControls[settings.activation], activation)) {
        Reset(); Message(message, "Could not read actual firing/activation controls."); return false;
    }
    if (ped_ != ped || model_ != model || weapon_ != weapon) {
        ped_ = ped; model_ = model; weapon_ = weapon; armed_ = false; triggerDown_ = trigger;
        lastResult_[0] = '\0';
    }
    if (!trigger) {
        armed_ = true; triggerDown_ = false;
        if (lastResult_[0]) {
            std::snprintf(message, sizeof(message), "Last experimental request: %.132s", lastResult_);
            return true;
        }
        Message(message, "Experimental mode armed; hold activation and press fire. Normal fire suppressed."); return true;
    }
    const bool edge = !triggerDown_;
    triggerDown_ = true;
    if (!armed_ || !edge || !activation) {
        if (edge && armed_ && !activation) lastResult_[0] = '\0';
        armed_ = false;
        if (lastResult_[0]) {
            std::snprintf(message, sizeof(message), "Last experimental request: %.132s", lastResult_);
            return true;
        }
        Message(message, "Release fire, then press it with activation; normal fire suppressed."); return true;
    }
    armed_ = false;
    // A fresh attempt supersedes the last observation, including when this
    // attempt is cancelled by cooldown, missing ammo, target or a native error.
    lastResult_[0] = '\0';
    if (hasDispatch_ && (now < lastDispatch_ || now - lastDispatch_ < MinimumInterval)) {
        Message(message, "Experimental dispatch cooldown; release fire before retrying. Normal fire suppressed."); return true;
    }
    int total = 0, clip = 0;
    if (!Ammo(ped, weapon, total, clip)) {
        Message(message, "Could not read valid total/clip ammunition before dispatch."); return false;
    }
    if (total <= 0 || clip <= 0) {
        Message(message, "No loaded ammunition; normal fire suppressed. Release fire before retrying."); return true;
    }
    int width = 0, height = 0;
    if (!Call(ScreenSize, {Pointer(&width), Pointer(&height)}) ||
        width < 90 || height < 90 || width > 16384 || height > 16384) {
        Message(message, "Could not read a valid active screen resolution."); return false;
    }
    Target selected{};
    float closest = std::numeric_limits<float>::infinity();
    for (std::uint32_t index = 0; index < 256; ++index) {
        if (index == player) continue;
        bool active = false;
        std::uint32_t candidate = 0, candidateModel = 0;
        if (!Boolean(ActivePlayer, {index}, active)) {
            Message(message, "Could not enumerate active player indices through natives."); return false;
        }
        if (!active) continue;
        if (!Integer(PlayerPed, {index}, candidate) || !Entity(candidate, candidateModel, valid)) {
            Message(message, "Could not validate an active player ped."); return false;
        }
        if (!valid || candidate == ped) continue;
        bool dead = false, clear = true, inside = false;
        Point point{};
        float distance = 0;
        if (!Boolean(Dead, {candidate, NativeArg(false)}, dead)) {
            Message(message, "Could not read target life state."); return false;
        }
        if (dead) continue;
        if (settings.onlyVisible && !Boolean(Los, {ped, candidate, 17u}, clear)) {
            Message(message, "Could not evaluate target line of sight."); return false;
        }
        if (!clear) continue;
        if (!Bone(candidate, settings.bone, point) || !InFov(point, width, height, settings.fov, inside, distance)) {
            Message(message, "Could not project a valid target bone."); return false;
        }
        if (inside && distance < closest) {
            closest = distance; selected = {index, candidate, candidateModel, point};
        }
    }
    if (!selected.ped) {
        Message(message, "No living player in FOV/line of sight; normal fire suppressed. Release fire to retry."); return true;
    }
    // Fresh handles/models/weapon/input/visibility are mandatory immediately
    // before dispatch; native handles are never taken from desktop memory.
    std::uint32_t targetPed = 0, targetModel = 0;
    bool active = false, targetValid = false, dead = false, clear = true, inside = false;
    float distance = 0;
    if (!LocalGate(ped, model, weapon, usable) ||
        !Boolean(ActivePlayer, {selected.player}, active) ||
        !Integer(PlayerPed, {selected.player}, targetPed) ||
        !Entity(targetPed, targetModel, targetValid) ||
        (targetValid && !Boolean(Dead, {targetPed, NativeArg(false)}, dead)) ||
        !Input(24, trigger) || !Input(ActivationControls[settings.activation], activation)) {
        Message(message, "Native validation failed immediately before experimental dispatch."); return false;
    }
    if (!usable || !active || !targetValid || targetPed != selected.ped ||
        targetModel != selected.model || dead || !trigger || !activation) {
        Message(message, "Player/target/weapon/input changed; directed dispatch cancelled."); return true;
    }
    if ((settings.onlyVisible && !Boolean(Los, {ped, targetPed, 17u}, clear)) ||
        !Bone(targetPed, settings.bone, selected.point) ||
        !InFov(selected.point, width, height, settings.fov, inside, distance) ||
        !Ammo(ped, weapon, total, clip)) {
        Message(message, "Fresh target projection/ammunition native validation failed."); return false;
    }
    if (!clear || !inside || total <= 0 || clip <= 0) {
        Message(message, "Target visibility/FOV/ammunition changed; directed dispatch cancelled."); return true;
    }
    hasDispatch_ = true; lastDispatch_ = now;
    if (!Call(Shoot, {ped, NativeArg(selected.point.x), NativeArg(selected.point.y),
                      NativeArg(selected.point.z), NativeArg(true)})) {
        Message(message, "SET_PED_SHOOTS_AT_COORD dispatch failed; no hit is claimed."); return false;
    }
    pendingConsumption_ = true; pendingStarted_ = now;
    beforeTotal_ = total; beforeClip_ = clip;
    return ObserveConsumption(now, message);
}

} // namespace zyon
