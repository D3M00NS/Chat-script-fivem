#include "Engine.h"
#include <algorithm>
#include <cmath>
#include <cstdio>

namespace zyon {
namespace {
// Standard GTA native hashes, from citizenfx/natives. No memory offsets.
constexpr std::uint64_t GameTimer = 0x9CD27B0045628463ULL, PlayerPed = 0xD80958FC74E988A6ULL;
constexpr std::uint64_t GameBuild = 0x804B9F7BULL; // CFX GET_GAME_BUILD_NUMBER.
constexpr std::uint64_t Exists = 0x7239B21A38F536BAULL, IsPed = 0x524AC5ECEA15343EULL;
constexpr std::uint64_t Model = 0x9F47B058362C84B5ULL, PedDead = 0x3317DEDB88C95038ULL;
constexpr std::uint64_t Damageable = 0xD95CC5D2AB15A09FULL, SetDamageable = 0x1760FFA8AB074D66ULL;
constexpr std::uint64_t Visible = 0x47D6F43D77935C75ULL, SetVisible = 0xEA1C610A04DB6BBBULL;
constexpr std::uint64_t CollisionDisabled = 0xCCF1E97BEFDAE480ULL, SetCollision = 0x1A9205C1B9EE827FULL;
constexpr std::uint64_t PositionFrozen = 0xEDBE6ADDULL; // CFX IS_ENTITY_POSITION_FROZEN.
constexpr std::uint64_t FreezePosition = 0x428CA6DBD1094446ULL;
constexpr std::uint64_t InVehicle = 0x997ABD671D25CA0BULL, Attached = 0xB346476EF1A64897ULL;
constexpr std::uint64_t NetworkControl = 0x01BF60A500E28887ULL, Disable = 0xFE99B66D079CF6BCULL;
constexpr std::uint64_t ControlNormal = 0x11E65974A982637CULL, Pressed = 0xE2587F8CBBD87B1DULL;
constexpr std::uint64_t CameraRotation = 0x837765A25378F0BBULL, FrameTime = 0x15C40837039FFAF7ULL;
constexpr std::uint64_t Coordinates = 0x3FEF770D40960D5AULL, SetCoordinates = 0x239A3351AC1DA385ULL;
constexpr std::uint64_t Velocity = 0x1C99BB7B6E96D16FULL, SelectedWeapon = 0x0A6DB4965674D243ULL;
constexpr std::uint64_t TotalAmmo = 0x015A522136D7F951ULL, ClipAmmo = 0x2E1202248937775CULL;
constexpr std::uint64_t SetTotalAmmo = 0x14E56BC5B5DB6A19ULL, SetClipAmmo = 0xDCD2A934D65CB497ULL;
constexpr std::uint64_t Heading = 0xE83D4F9BA2A38914ULL, Resurrect = 0xEA23C49EAA83ACFBULL;
constexpr std::uint64_t MaximumHealth = 0x15D757606D170C3CULL, SetHealth = 0x6B76DC1F3AE6E6A3ULL;
constexpr std::uint64_t ClearBlood = 0x8FE22675A5A45817ULL;
constexpr std::uint32_t Unarmed = 0xA2719263u;
bool FinitePosition(float x, float y, float z) {
    return std::isfinite(x) && std::isfinite(y) && std::isfinite(z) &&
           std::abs(x) < 1000000 && std::abs(y) < 1000000 && std::abs(z) < 1000000;
}
} // namespace

bool Engine::Call(std::uint64_t hash, std::initializer_list<std::uint64_t> args, NativeResult* result) {
    NativeResult output{};
    const bool ok = natives_.HasNative(hash) && natives_.Invoke(hash, args.begin(), args.size(), output);
    if (result) *result = output;
    return ok;
}
bool Engine::Bool(std::uint64_t hash, std::initializer_list<std::uint64_t> args, bool& value) {
    NativeResult result{};
    if (!Call(hash, args, &result)) return false;
    const auto raw = static_cast<std::uint32_t>(result.words[0]);
    if (raw > 1) return false;
    value = raw != 0;
    return true;
}
bool Engine::Integer(std::uint64_t hash, std::initializer_list<std::uint64_t> args, std::uint32_t& value) {
    NativeResult result{};
    if (!Call(hash, args, &result)) return false;
    value = static_cast<std::uint32_t>(result.words[0]);
    return true;
}
bool Engine::Float(std::uint64_t hash, std::initializer_list<std::uint64_t> args, float& value) {
    NativeResult result{};
    if (!Call(hash, args, &result)) return false;
    value = NativeFloat(result.words[0]);
    return std::isfinite(value);
}
bool Engine::Vector(std::uint64_t hash, std::initializer_list<std::uint64_t> args, Vec& value) {
    NativeResult result{};
    if (!Call(hash, args, &result)) return false;
    value = { NativeFloat(result.words[0]), NativeFloat(result.words[1]), NativeFloat(result.words[2]) };
    return FinitePosition(value.x, value.y, value.z);
}
bool Engine::ValidatePed(std::uint32_t ped, std::uint32_t* model) {
    bool exists = false, isPed = false;
    std::uint32_t value = 0;
    if (!ped || !Bool(Exists, {ped}, exists) || !exists || !Bool(IsPed, {ped}, isPed) || !isPed ||
        !Integer(Model, {ped}, value) || !value) return false;
    if (model) *model = value;
    return true;
}
bool Engine::SameLocalPed(std::uint32_t ped) {
    std::uint32_t current = 0, model = 0;
    return Integer(PlayerPed, {}, current) && current == ped && ValidatePed(ped, &model) && model == model_;
}
// IS_PED_DEAD_OR_DYING with melee-death extension disabled: a melee takedown
// alone must not be acknowledged as this module's observed suicide.
bool Engine::Dead(bool& dead) { return Bool(PedDead, {ped_, NativeArg(false)}, dead); }

bool Engine::RequiredNativesAvailable() const {
    for (const auto hash : {GameTimer, GameBuild, PlayerPed, Exists, IsPed, Model, PedDead})
        if (!natives_.HasNative(hash)) return false;
    return true;
}
std::uint32_t Engine::AvailableCapabilities() const {
    const auto available = [this](std::initializer_list<std::uint64_t> hashes) {
        for (const auto hash : hashes) if (!natives_.HasNative(hash)) return false;
        return true;
    };
    std::uint32_t result = 0;
    if (available({Damageable, SetDamageable})) result |= CapabilityGodMode;
    if (available({Visible, SetVisible})) result |= CapabilityInvisible;
    if (available({CollisionDisabled, SetCollision, PositionFrozen, FreezePosition, InVehicle, Attached, NetworkControl, Disable,
                   ControlNormal, Pressed, CameraRotation, FrameTime, Coordinates, SetCoordinates, Velocity}))
        result |= CapabilityNoclip;
    if (available({SelectedWeapon, TotalAmmo, ClipAmmo, SetTotalAmmo, SetClipAmmo})) result |= CapabilityFreezeAmmo;
    if (available({Coordinates, Heading, Resurrect, MaximumHealth, SetHealth, ClearBlood})) result |= CapabilityRevive;
    if (available({SetHealth})) result |= CapabilitySuicide;
    return result;
}

void Engine::Message(std::uint32_t error, const char* message) {
    status_.error = error;
    std::snprintf(status_.message, sizeof(status_.message), "%s", message);
}
void Engine::Block(std::uint32_t error, const char* message) {
    status_.stateFlags = Blocked;
    status_.capabilities = 0;
    status_.appliedRevision = 0;
    Message(error, message);
}

bool Engine::RestoreProperty(Property& property, std::uint64_t getter, std::uint64_t setter,
                             bool forcedValue, bool extraArgument) {
    if (!property.owned) return true;
    const auto saved = property;
    bool exists = false;
    if (!Bool(Exists, {saved.ped}, exists)) return false;
    if (!exists) { property = {}; return true; }
    std::uint32_t model = 0;
    if (!Integer(Model, {saved.ped}, model) || !model) return false;
    if (model != saved.model) { property = {}; return true; } // Entity handle was reused.
    bool current = false;
    if (!Bool(getter, {saved.ped}, current)) return false;
    if (current == saved.original || current != forcedValue) {
        // Already restored, or a newer observable external change must be preserved.
        property = {}; return true;
    }
    // The collision getter reports disabled; its setter takes enabled.
    const bool target = getter == CollisionDisabled ? !saved.original : saved.original;
    if (!(extraArgument ? Call(setter, {saved.ped, NativeArg(target), NativeArg(getter == CollisionDisabled)})
                         : Call(setter, {saved.ped, NativeArg(target)}))) return false;
    if (!Bool(getter, {saved.ped}, current) || current != saved.original) return false;
    property = {};
    return true;
}
bool Engine::RestoreFlight() {
    const bool collision = RestoreProperty(collision_, CollisionDisabled, SetCollision, true, true);
    const bool frozen = RestoreProperty(frozen_, PositionFrozen, FreezePosition, true, false);
    return collision && frozen;
}
bool Engine::ReleaseEffects() {
    // Do not short circuit: every owned property gets an independent cleanup attempt.
    const bool flight = RestoreFlight();
    const bool god = RestoreProperty(god_, Damageable, SetDamageable, false, false);
    const bool visibility = RestoreProperty(visible_, Visible, SetVisible, false, true);
    ammoWeapon_ = 0; ammoHasClip_ = false;
    return flight && god && visibility;
}

bool Engine::ApplyGod() {
    if (!(settings_.flags & ~suppressedFlags_ & ~unavailableFlags_ & GodMode))
        return RestoreProperty(god_, Damageable, SetDamageable, false, false);
    bool current = false;
    if (!Bool(Damageable, {ped_}, current)) return false;
    if (!god_.owned) god_ = {ped_, model_, current, true};
    if (current && !Call(SetDamageable, {ped_, NativeArg(false)})) return false;
    return Bool(Damageable, {ped_}, current) && !current;
}
bool Engine::ApplyVisibility() {
    if (!(settings_.flags & ~unavailableFlags_ & Invisible))
        return RestoreProperty(visible_, Visible, SetVisible, false, true);
    bool current = false;
    if (!Bool(Visible, {ped_}, current)) return false;
    if (!visible_.owned) visible_ = {ped_, model_, current, true};
    if (current && !Call(SetVisible, {ped_, NativeArg(false), NativeArg(false)})) return false;
    return Bool(Visible, {ped_}, current) && !current;
}

bool Engine::ApplyFlight(bool alive, bool focused, bool& waiting) {
    waiting = false;
    if (!(settings_.flags & ~suppressedFlags_ & ~unavailableFlags_ & Noclip)) return RestoreFlight();
    bool vehicle = false, attached = false, controlled = false;
    if (!Bool(InVehicle, {ped_, NativeArg(false)}, vehicle) || !Bool(Attached, {ped_}, attached) ||
        !Bool(NetworkControl, {ped_}, controlled)) return false;
    if (!alive || vehicle || attached || !controlled) { waiting = true; return RestoreFlight(); }
    bool disabled = false, frozen = false;
    if (!Bool(CollisionDisabled, {ped_}, disabled) || !Bool(PositionFrozen, {ped_}, frozen)) return false;
    if (!collision_.owned) collision_ = {ped_, model_, disabled, true};
    if (!frozen_.owned) frozen_ = {ped_, model_, frozen, true};
    if (!disabled && !Call(SetCollision, {ped_, NativeArg(false), NativeArg(false)})) return false;
    if (!Bool(CollisionDisabled, {ped_}, disabled) || !disabled) return false;
    if (!frozen && !Call(FreezePosition, {ped_, NativeArg(true)})) return false;
    if (!Bool(PositionFrozen, {ped_}, frozen) || !frozen) return false;
    if (!Call(Velocity, {ped_, NativeArg(0.0f), NativeArg(0.0f), NativeArg(0.0f)})) return false;
    if (!focused) return true; // Keep hover, but never consume keys from another application.
    for (const std::uint32_t control : {30u, 31u, 22u, 36u, 21u, 24u, 25u})
        if (!Call(Disable, {0u, control, NativeArg(true)})) return false;
    float forward = 0, right = 0, dt = 0;
    bool up = false, down = false, fast = false;
    Vec rotation{}, position{};
    if (!Float(ControlNormal, {0u, 31u}, forward) || !Float(ControlNormal, {0u, 30u}, right) ||
        !Bool(Pressed, {0u, 22u}, up) || !Bool(Pressed, {0u, 36u}, down) || !Bool(Pressed, {0u, 21u}, fast) ||
        !Float(FrameTime, {}, dt) || !Vector(CameraRotation, {2u}, rotation) ||
        !Vector(Coordinates, {ped_, NativeArg(false)}, position)) return false;
    forward = -std::clamp(forward, -1.0f, 1.0f);
    right = std::clamp(right, -1.0f, 1.0f);
    constexpr float radians = 0.017453292519943295f;
    const float pitch = rotation.x * radians, yaw = rotation.z * radians;
    Vec direction{-std::sin(yaw) * std::cos(pitch) * forward + std::cos(yaw) * right,
                  std::cos(yaw) * std::cos(pitch) * forward + std::sin(yaw) * right,
                  std::sin(pitch) * forward + (up ? 1.0f : 0.0f) - (down ? 1.0f : 0.0f)};
    const float length = std::sqrt(direction.x * direction.x + direction.y * direction.y + direction.z * direction.z);
    if (!std::isfinite(length) || !SameLocalPed(ped_)) return false;
    if (length < 0.00001f) return true;
    const float amount = settings_.speed * (fast ? 3.0f : 1.0f) * std::clamp(dt, 0.001f, 0.1f) / std::max(length, 1.0f);
    const Vec target{position.x + direction.x * amount, position.y + direction.y * amount, position.z + direction.z * amount};
    if (!Call(SetCoordinates, {ped_, NativeArg(target.x), NativeArg(target.y), NativeArg(target.z),
        NativeArg(true), NativeArg(true), NativeArg(false)})) return false;
    Vec observed{};
    return SameLocalPed(ped_) && Vector(Coordinates, {ped_, NativeArg(false)}, observed) &&
        std::abs(observed.x - target.x) <= 0.05f && std::abs(observed.y - target.y) <= 0.05f &&
        std::abs(observed.z - target.z) <= 0.05f;
}

bool Engine::ApplyAmmo() {
    if (!(settings_.flags & ~unavailableFlags_ & FreezeAmmo)) { ammoWeapon_ = 0; ammoHasClip_ = false; return true; }
    std::uint32_t weapon = 0, rawTotal = 0;
    if (!Integer(SelectedWeapon, {ped_}, weapon)) return false;
    if (!weapon || weapon == Unarmed) { ammoWeapon_ = 0; ammoHasClip_ = false; return true; }
    if (!Integer(TotalAmmo, {ped_, weapon}, rawTotal) || rawTotal > 1000000u) return false;
    int clip = 0;
    bool hasClip = false;
    const auto clipPointer = static_cast<std::uint64_t>(reinterpret_cast<std::uintptr_t>(&clip));
    if (!Bool(ClipAmmo, {ped_, weapon, clipPointer}, hasClip) || (hasClip && (clip < 0 || clip > 1000000))) return false;
    const int total = static_cast<int>(rawTotal);
    if (weapon != ammoWeapon_) {
        ammoWeapon_ = weapon; ammoTotal_ = total; ammoClip_ = clip; ammoHasClip_ = hasClip;
        return true;
    }
    ammoTotal_ = std::max(ammoTotal_, total);
    if (hasClip) ammoClip_ = ammoHasClip_ ? std::max(ammoClip_, clip) : clip;
    ammoHasClip_ = hasClip;
    if (total < ammoTotal_) {
        // SET_PED_AMMO documents an added fourth BOOL argument.
        if (!Call(SetTotalAmmo, {ped_, weapon, NativeArg(ammoTotal_), NativeArg(false)})) return false;
        std::uint32_t observed = 0;
        if (!Integer(TotalAmmo, {ped_, weapon}, observed) || observed < static_cast<std::uint32_t>(ammoTotal_)) return false;
    }
    if (hasClip && clip < ammoClip_) {
        bool changed = false;
        const int target = std::min(ammoClip_, ammoTotal_);
        if (!Bool(SetClipAmmo, {ped_, weapon, NativeArg(target)}, changed) || !changed) return false;
        clip = 0;
        if (!Bool(ClipAmmo, {ped_, weapon, clipPointer}, hasClip) || !hasClip || clip != target) return false;
    }
    return true;
}

void Engine::EndAction(std::uint32_t result, const char* message) {
    status_.actionResult = result;
    pendingAction_ = NoAction; pendingPed_ = 0;
    Message(result == Failed ? ActionFailure : NoError, message);
}
void Engine::ObserveAction(std::uint64_t nowMs, bool dead) {
    if (pendingAction_ == NoAction) return;
    if (pendingPed_ != ped_ || !SameLocalPed(pendingPed_)) { EndAction(Failed, "The local ped changed before the action was confirmed."); return; }
    if ((pendingAction_ == Suicide && dead) || (pendingAction_ == Revive && !dead)) {
        EndAction(Observed, pendingAction_ == Suicide ? "Native death observed on the requested local ped."
            : "Native resurrection observed; framework medical state is not changed.");
    } else if (nowMs < pendingStarted_ || nowMs - pendingStarted_ >= 2000) {
        EndAction(Failed, "The requested native state was not observed within two seconds.");
    }
}
void Engine::BeginAction(const ControlSnapshot& input, std::uint64_t nowMs, bool dead) {
    if (!input.action || !input.actionNonce || input.actionNonce <= lastActionNonce_ || pendingAction_) return;
    lastActionNonce_ = input.actionNonce;
    status_.actionNonce = input.actionNonce; status_.action = input.action; status_.actionResult = Pending;
    const auto capability = input.action == Revive ? CapabilityRevive : CapabilitySuicide;
    if ((input.action != Revive && input.action != Suicide) || !input.expectedPed || input.expectedPed != ped_ || !SameLocalPed(ped_)) {
        EndAction(Failed, "The action did not match the current local ped."); return;
    }
    if (!(status_.capabilities & capability)) {
        EndAction(Failed, "A required native handler for this action is unavailable."); return;
    }
    pendingAction_ = input.action; pendingPed_ = ped_; pendingStarted_ = nowMs;
    if (input.action == Suicide) {
        suppressedFlags_ |= GodMode | Noclip;
        const bool flight = RestoreFlight();
        const bool god = RestoreProperty(god_, Damageable, SetDamageable, false, false);
        if (!flight || !god) { EndAction(Failed, "Owned protection could not be restored before suicide."); return; }
        if (dead) { EndAction(Observed, "The requested local ped is already dead."); return; }
        // Explicit null inflictor also covers the documented added argument.
        if (!Call(SetHealth, {ped_, NativeArg(std::int32_t{0}), 0u})) EndAction(Failed, "The native suicide call was unavailable.");
    } else {
        if (!dead) { EndAction(Observed, "The requested local ped is already alive."); return; }
        Vec position{}; float heading = 0;
        if (!Vector(Coordinates, {ped_, NativeArg(false)}, position) || !Float(Heading, {ped_}, heading) || !ReleaseEffects() ||
            // Added parameters: unpause render phases, current-position spawn
            // metric, death reason. Older handlers ignore trailing arguments.
            !Call(Resurrect, {NativeArg(position.x), NativeArg(position.y), NativeArg(position.z), NativeArg(heading),
                0u, NativeArg(false), NativeArg(true), 6u, 0u}) ||
            !SameLocalPed(ped_)) { EndAction(Failed, "Native resurrection was unavailable or replaced the local ped."); return; }
        std::uint32_t maximum = 0;
        if (!Integer(MaximumHealth, {ped_}, maximum) || maximum == 0 || maximum > 10000 ||
            !Call(SetHealth, {ped_, maximum, 0u}) || !Call(ClearBlood, {ped_}))
            EndAction(Failed, "The resurrection health state could not be established.");
    }
}

void Engine::NewEpoch(std::uint64_t epoch) {
    ReleaseEffects();
    epoch_ = epoch; hasEpoch_ = true; revoked_ = false; timerSeen_ = false;
    settings_ = {}; hasSettings_ = false; minimumRevision_ = failedRevision_ = 0;
    lastActionNonce_ = 0; pendingAction_ = NoAction; pendingPed_ = ped_ = model_ = suppressedFlags_ = unavailableFlags_ = 0;
    status_.action = NoAction; status_.actionNonce = 0; status_.actionResult = NoResult;
}

void Engine::Step(const ControlSnapshot& input, std::uint64_t nowMs) {
    status_.desiredRevision = input.settings.revision;
    status_.stateFlags = 0; status_.capabilities = 0; status_.ped = 0; status_.appliedRevision = 0;
    if (!input.connected || !input.sessionEpoch || nowMs < input.lastSeenMs || nowMs - input.lastSeenMs > LeaseMilliseconds) {
        if (hasEpoch_) revoked_ = true;
        const bool restored = ReleaseEffects();
        hasSettings_ = false;
        if (pendingAction_) EndAction(Failed, "The control lease ended before action confirmation.");
        Block(LeaseOrOffline, restored ? "No live local control lease; owned effects released."
            : "No live control lease; some native restoration could not be confirmed.");
        return;
    }
    if (!hasEpoch_ || input.sessionEpoch != epoch_) NewEpoch(input.sessionEpoch);
    if (revoked_) { ReleaseEffects(); Block(LeaseOrOffline, "The expired session needs a new connection epoch."); return; }

    if (!RequiredNativesAvailable()) {
        ReleaseEffects(); timerSeen_ = false;
        Block(Unavailable, "A required native handler is unavailable; restoration was attempted."); return;
    }

    std::uint32_t timer = 0;
    if (!Integer(GameTimer, {}, timer)) {
        ReleaseEffects(); timerSeen_ = false;
        Block(Unavailable, "The native invoker is unavailable; restoration was attempted."); return;
    }
    if (!timerSeen_) {
        timerSeen_ = true; timer_ = timer;
        Message(WaitingNative, "Waiting for a second advancing native game frame."); return;
    }
    const std::uint32_t advance = timer - timer_;
    if (!advance || advance >= 0x80000000u) {
        if (hasSettings_) {
            ReleaseEffects(); minimumRevision_ = settings_.revision; hasSettings_ = false;
        }
        if (pendingAction_) EndAction(Failed, "The native game frame stopped before action confirmation.");
        Message(WaitingNative, "The game timer has not advanced; no native effect is claimed."); return;
    }
    timer_ = timer;
    std::uint32_t build = 0;
    if (!Integer(GameBuild, {}, build) || build != 3258) {
        ReleaseEffects(); status_.build = build;
        Block(Unavailable, "The native client requires an observed game build of 3258."); return;
    }
    status_.build = build;
    std::uint32_t current = 0, model = 0;
    if (!Integer(PlayerPed, {}, current) || !ValidatePed(current, &model)) {
        if (ped_) { ReleaseEffects(); minimumRevision_ = settings_.revision; hasSettings_ = false; ped_ = 0; }
        if (pendingAction_) EndAction(Failed, "The local ped became unavailable.");
        Message(InvalidPed, "Waiting for a real local ped with a valid entity model."); return;
    }
    if (ped_ && (ped_ != current || model_ != model)) {
        ReleaseEffects(); minimumRevision_ = settings_.revision; hasSettings_ = false;
        if (pendingAction_) EndAction(Failed, "The local ped changed before action confirmation.");
    }
    ped_ = current; model_ = model;
    bool dead = false;
    if (!Dead(dead)) { ReleaseEffects(); Block(Unavailable, "The native death-state getter is unavailable."); return; }
    status_.tick = nowMs;
    status_.ped = ped_; status_.capabilities = AvailableCapabilities();
    status_.stateFlags = Ready | (dead ? 0u : PedAlive);
    Message(NoError, "Local native ped validated; no settings snapshot applied yet.");

    // An old entity's failed cleanup is retried before a new ped or connection
    // can take ownership. Never copy the previous ped's snapshots onto this ped.
    if ((!hasSettings_ || failedRevision_) && !ReleaseEffects()) {
        Block(EffectFailure, "Previous native property restoration is still unconfirmed."); return;
    }

    if (input.settings.revision > settings_.revision && input.settings.revision > minimumRevision_) {
        if ((input.settings.flags & ~KnownSettingFlags) || input.settings.speed < 1 || input.settings.speed > 50) {
            ReleaseEffects(); failedRevision_ = input.settings.revision;
            Block(EffectFailure, "Settings contain an unsupported flag or invalid speed."); return;
        }
        settings_ = input.settings;
        // A stale UI snapshot must not re-enable protection after suicide.
        // Each suppressed toggle must first be explicitly observed off.
        suppressedFlags_ &= settings_.flags;
        hasSettings_ = true; failedRevision_ = 0;
    }
    if (input.settings.revision && input.settings.revision == failedRevision_) {
        Block(EffectFailure, "The rejected settings revision needs replacement."); return;
    }
    ObserveAction(nowMs, dead);
    BeginAction(input, nowMs, dead);
    if (!SameLocalPed(ped_) || !Dead(dead)) {
        ReleaseEffects(); Block(InvalidPed, "The ped changed while applying an action."); return;
    }
    status_.stateFlags = Ready | (dead ? 0u : PedAlive);
    if (hasSettings_) {
        std::uint32_t availableFlags = 0;
        if (status_.capabilities & CapabilityGodMode) availableFlags |= GodMode;
        if (status_.capabilities & CapabilityNoclip) availableFlags |= Noclip;
        if (status_.capabilities & CapabilityInvisible) availableFlags |= Invisible;
        if (status_.capabilities & CapabilityFreezeAmmo) availableFlags |= FreezeAmmo;
        unavailableFlags_ = settings_.flags & ~availableFlags;
        bool waitingFlight = false;
        if (!ApplyGod() || !ApplyVisibility() || !ApplyFlight(!dead, input.gameFocused, waitingFlight) || (!dead && !ApplyAmmo())) {
            const bool restored = ReleaseEffects(); failedRevision_ = settings_.revision;
            Block(EffectFailure, restored ? "A native effect was not observed; owned effects were released."
                : "A native effect was not observed; property restoration is still unconfirmed."); return;
        }
        if (unavailableFlags_) {
            Message(NoError, "A requested feature lacks native handlers; supported features remain available.");
        } else if (!waitingFlight) {
            status_.appliedRevision = settings_.revision;
            status_.stateFlags |= SettingsActive;
            if (status_.error == NoError && status_.actionResult != Pending && status_.actionResult != Failed)
                Message(NoError, suppressedFlags_ ? "Native settings active; suicide keeps owned GodMode and noclip disabled."
                    : "Native settings observed on the validated local ped.");
        } else Message(NoError, "Noclip waits for a live on-foot ped with network control and no attachment.");
    }
    // Calls can change death state immediately; observation is still required for ACK.
    ObserveAction(nowMs, dead);
}

} // namespace zyon
