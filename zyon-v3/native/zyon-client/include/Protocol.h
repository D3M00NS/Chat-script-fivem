#pragma once

#include <cstddef>
#include <cstdint>

namespace zyon {

constexpr std::uint32_t ProtocolMagic = 0x4E4F595A;
constexpr std::uint16_t ProtocolVersion = 1;
constexpr std::uint64_t LeaseMilliseconds = 3000;
constexpr std::uint64_t MaximumFrameAgeMilliseconds = 1000;

enum class PacketKind : std::uint16_t {
    Hello = 1, Settings = 2, Action = 3, Status = 4, Disconnect = 5, Poll = 6
};

enum SettingFlag : std::uint32_t {
    GodMode = 1u << 0, Noclip = 1u << 1, Invisible = 1u << 2, FreezeAmmo = 1u << 3
};
constexpr std::uint32_t KnownSettingFlags = GodMode | Noclip | Invisible | FreezeAmmo;

enum Capability : std::uint32_t {
    CapabilityGodMode = 1u << 0, CapabilityNoclip = 1u << 1,
    CapabilityInvisible = 1u << 2, CapabilityFreezeAmmo = 1u << 3,
    CapabilityRevive = 1u << 4, CapabilitySuicide = 1u << 5
};
constexpr std::uint32_t KnownCapabilities = 0x3F;

enum StateFlag : std::uint32_t {
    Ready = 1u << 0, SettingsActive = 1u << 1, PedAlive = 1u << 2, Blocked = 1u << 3
};
enum Action : std::uint32_t { NoAction = 0, Revive = 1, Suicide = 2 };
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
};
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
static_assert(sizeof(SettingsPacket) == 32);
static_assert(sizeof(ActionPacket) == 32);
static_assert(sizeof(StatusPacket) == 240);
static_assert(offsetof(StatusPacket, message) == 80);
static_assert(offsetof(StatusPacket, capabilities) == 48);

} // namespace zyon
