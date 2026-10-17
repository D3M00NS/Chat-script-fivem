#pragma once

#include <cstddef>
#include <cstdint>

namespace zyon {

constexpr std::uint32_t ProtocolMagic = 0x4E4F595A;
constexpr std::uint16_t ProtocolVersion = 2;
constexpr std::uint64_t LeaseMilliseconds = 3000;
constexpr std::uint64_t MaximumFrameAgeMilliseconds = 1000;

enum class PacketKind : std::uint16_t {
    Hello = 1, Settings = 2, Action = 3, Status = 4, Disconnect = 5, Poll = 6
};

enum SettingFlag : std::uint32_t {
    GodMode = 1u << 0, Noclip = 1u << 1, Invisible = 1u << 2, FreezeAmmo = 1u << 3,
    DirectedFireExperimental = 1u << 8
};
constexpr std::uint32_t KnownSettingFlags = GodMode | Noclip | Invisible | FreezeAmmo | DirectedFireExperimental;

enum Capability : std::uint32_t {
    CapabilityGodMode = 1u << 0, CapabilityNoclip = 1u << 1,
    CapabilityInvisible = 1u << 2, CapabilityFreezeAmmo = 1u << 3,
    CapabilityRevive = 1u << 4, CapabilitySuicide = 1u << 5,
    CapabilityLockVehicle = 1u << 6, CapabilityUnlockVehicle = 1u << 7,
    CapabilityDirectedFireExperimental = 1u << 8
};
constexpr std::uint32_t KnownCapabilities = 0x1FF;

enum StateFlag : std::uint32_t {
    Ready = 1u << 0, SettingsActive = 1u << 1, PedAlive = 1u << 2, Blocked = 1u << 3
};
enum Action : std::uint32_t { NoAction = 0, Revive = 1, Suicide = 2, LockVehicle = 3, UnlockVehicle = 4 };
enum ActionResult : std::uint32_t { NoResult = 0, Pending = 1, Observed = 2, Failed = 3 };
enum Error : std::uint32_t {
    NoError = 0, WaitingNative = 10, Unavailable = 11, InvalidPed = 12,
    EffectFailure = 13, ActionFailure = 14, LeaseOrOffline = 15,
    ProtocolInvalid = 1001, TokenInvalid = 1002, FrameStale = 1003,
    ActionBusy = 1004, StaleRevisionOrNonce = 1005
};

// Runtime values are independent of the on-wire packed representation.
struct Settings {
    std::uint64_t revision = 0;
    std::uint32_t flags = 0;
    std::uint32_t speed = 5;
    std::uint32_t silentFov = 100, silentBone = 0, silentActivation = 0, silentOptions = 3;
};
inline bool SettingsInRange(const Settings& settings) noexcept {
    return !(settings.flags & ~KnownSettingFlags) && settings.speed >= 1 && settings.speed <= 50 &&
        settings.silentFov >= 10 && settings.silentFov <= 500 && settings.silentBone <= 2 &&
        settings.silentActivation <= 4 && !(settings.silentOptions & ~3u) && (settings.silentOptions & 1u);
}
struct ControlSnapshot {
    Settings settings{};
    std::uint64_t sessionEpoch = 0;
    std::uint64_t actionNonce = 0;
    std::uint64_t lastSeenMs = 0;
    std::uint32_t action = NoAction;
    std::uint32_t expectedPed = 0;
    bool connected = false;
    // Local scheduler observation only; never supplied by the pipe client.
    bool gameFocused = true;
};
struct RuntimeStatus {
    // Scheduler heartbeat only. This field is never serialized or used to
    // establish native readiness; tick records a validated native frame.
    std::uint64_t scriptTick = 0;
    std::uint64_t tick = 0;
    std::uint64_t desiredRevision = 0;
    std::uint64_t appliedRevision = 0;
    std::uint64_t actionNonce = 0;
    std::uint32_t capabilities = 0;
    std::uint32_t stateFlags = 0;
    std::uint32_t action = NoAction;
    std::uint32_t actionResult = NoResult;
    std::uint32_t ped = 0;
    std::uint32_t build = 0;
    std::uint32_t error = NoError;
    char message[160]{};
};

inline bool HasQueuedAction(const ControlSnapshot& control, const RuntimeStatus& runtime,
                            std::uint64_t runtimeEpoch, std::uint64_t connectionEpoch) noexcept {
    // A previous connection's high nonce must never make a new queued action
    // look consumed before the game fiber has read the new epoch.
    return runtimeEpoch != connectionEpoch ? control.actionNonce != 0
        : control.actionNonce > runtime.actionNonce;
}

#pragma pack(push, 1)
struct PacketHeader {
    std::uint32_t magic = ProtocolMagic;
    std::uint16_t version = ProtocolVersion;
    std::uint16_t kind = 0;
    std::uint32_t length = 0;
    std::uint32_t reserved = 0;
};
struct TokenPacket { std::uint8_t token[16]{}; };
struct SettingsPacket {
    std::uint8_t token[16]{};
    std::uint64_t revision = 0;
    std::uint32_t flags = 0;
    std::uint32_t speed = 5;
    std::uint32_t silentFov = 100, silentBone = 0, silentActivation = 0, silentOptions = 3;
};
struct ActionPacket {
    std::uint8_t token[16]{};
    std::uint64_t nonce = 0;
    std::uint32_t action = NoAction;
    std::uint32_t expectedPed = 0;
};
struct StatusPacket {
    std::uint8_t token[16]{};
    std::uint64_t tick = 0;
    std::uint64_t desiredRevision = 0;
    std::uint64_t appliedRevision = 0;
    std::uint64_t actionNonce = 0;
    std::uint32_t capabilities = 0;
    std::uint32_t stateFlags = 0;
    std::uint32_t action = NoAction;
    std::uint32_t actionResult = NoResult;
    std::uint32_t ped = 0;
    std::uint32_t build = 0;
    std::uint32_t error = NoError;
    std::uint32_t reserved = 0;
    char message[160]{};
};
#pragma pack(pop)

static_assert(sizeof(PacketHeader) == 16);
static_assert(sizeof(TokenPacket) == 16);
static_assert(sizeof(SettingsPacket) == 48);
static_assert(offsetof(SettingsPacket, silentFov) == 32);
static_assert(offsetof(SettingsPacket, silentBone) == 36);
static_assert(offsetof(SettingsPacket, silentActivation) == 40);
static_assert(offsetof(SettingsPacket, silentOptions) == 44);
static_assert(sizeof(ActionPacket) == 32);
static_assert(sizeof(StatusPacket) == 240);
static_assert(offsetof(StatusPacket, message) == 80);
static_assert(offsetof(StatusPacket, capabilities) == 48);

} // namespace zyon
