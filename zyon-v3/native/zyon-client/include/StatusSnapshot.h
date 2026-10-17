#pragma once

#include "Protocol.h"
#include <algorithm>
#include <cstring>

namespace zyon {

// Initialization failures belong to the loaded module, rather than to a pipe
// connection. A failed script registration never produces a runtimeEpoch.
struct ModuleBootstrapFailure {
    bool failed = false;
    char message[160]{};
};

inline void CopyStatusMessage(char (&target)[160], const char* source) noexcept {
    std::memset(target, 0, sizeof(target));
    if (!source) return;
    for (std::size_t index = 0; index < sizeof(target) - 1 && source[index]; ++index) {
        const auto byte = static_cast<unsigned char>(source[index]);
        target[index] = byte >= 32 && byte <= 126 ? static_cast<char>(byte) : '?';
    }
}

inline StatusPacket BuildStatusSnapshot(const std::uint8_t (&token)[16], const RuntimeStatus& runtime,
                                       const ControlSnapshot& control, std::uint64_t runtimeEpoch,
                                       const ModuleBootstrapFailure& bootstrap, std::uint64_t now,
                                       std::uint32_t error = NoError, const char* detail = nullptr) noexcept {
    StatusPacket packet{};
    std::memcpy(packet.token, token, sizeof(packet.token));
    packet.tick = runtime.tick;
    packet.desiredRevision = control.settings.revision;
    if (runtimeEpoch == control.sessionEpoch) {
        packet.appliedRevision = std::min(runtime.appliedRevision, packet.desiredRevision);
        packet.actionNonce = runtime.actionNonce;
        packet.capabilities = runtime.capabilities;
        packet.stateFlags = runtime.stateFlags;
        packet.action = runtime.action;
        packet.actionResult = runtime.actionResult;
        packet.ped = runtime.ped;
        packet.build = runtime.build;
        packet.error = runtime.error;
        CopyStatusMessage(packet.message, runtime.message);
    } else {
        packet.error = WaitingNative;
        packet.stateFlags = Blocked;
        CopyStatusMessage(packet.message, "Waiting for a game script frame for this connection.");
    }
    if (bootstrap.failed) {
        // Never turn a known missing DLL/export into an unproven policy denial.
        packet.tick = 0;
        packet.appliedRevision = 0;
        packet.actionNonce = 0;
        packet.capabilities = 0;
        packet.stateFlags = Blocked;
        packet.action = NoAction;
        packet.actionResult = NoResult;
        packet.ped = 0;
        packet.build = 0;
        packet.error = Unavailable;
        CopyStatusMessage(packet.message, bootstrap.message);
    }
    if (control.actionNonce > packet.actionNonce) {
        packet.actionNonce = control.actionNonce;
        packet.action = control.action;
        packet.actionResult = Pending;
    }
    if (!packet.tick || now < packet.tick || now - packet.tick > MaximumFrameAgeMilliseconds) {
        packet.stateFlags = (packet.stateFlags & ~(Ready | SettingsActive)) | Blocked;
        packet.capabilities = 0;
        packet.appliedRevision = 0;
        const bool sameEpoch = control.sessionEpoch != 0 && runtimeEpoch == control.sessionEpoch;
        const bool callbackFresh = runtime.scriptTick != 0 && now >= runtime.scriptTick &&
            now - runtime.scriptTick <= MaximumFrameAgeMilliseconds;
        const bool currentPreflight = sameEpoch && callbackFresh && !(runtime.stateFlags & Ready) &&
            (runtime.error == WaitingNative || runtime.error == InvalidPed || runtime.error == LeaseOrOffline);
        if (packet.error != Unavailable) {
            if (!currentPreflight) {
                packet.error = FrameStale;
                if (!runtime.scriptTick) {
                    CopyStatusMessage(packet.message,
                        "ASI loaded; registered game callback has not entered. Check session readiness and ScriptHook policy; a permission denial is not confirmed.");
                } else if (!sameEpoch) {
                    CopyStatusMessage(packet.message,
                        "Waiting for a registered game callback for this connection; no native execution is confirmed.");
                } else if (!callbackFresh) {
                    CopyStatusMessage(packet.message,
                        "The registered game callback stopped updating; no recent native execution is confirmed. ScriptHook permission denial is not confirmed.");
                } else {
                    CopyStatusMessage(packet.message,
                        "Game callback is running; no recent validated native frame is available.");
                }
            }
        }
    }
    if (error) {
        packet.error = error;
        CopyStatusMessage(packet.message, detail);
    }
    return packet;
}

} // namespace zyon
