#include "StatusSnapshot.h"
#include <cstdlib>
#include <iostream>
#include <stdexcept>
#include <string>

using namespace zyon;

namespace {
int assertions = 0;
void Check(bool condition, const char* message) {
    ++assertions;
    if (!condition) throw std::runtime_error(message);
}

void BootstrapFailureSurvivesHelloAndReconnect() {
    const std::uint8_t token[16]{1, 2, 3};
    const RuntimeStatus runtime{}; // ScriptMain never registered or ran.
    ControlSnapshot control{};
    ModuleBootstrapFailure bootstrap{};
    bootstrap.failed = true;
    CopyStatusMessage(bootstrap.message, "Missing ScriptHookV exports: scriptRegister nativeCall");
    for (const auto epoch : {1u, 2u, 200u}) {
        control.sessionEpoch = epoch;
        const auto packet = BuildStatusSnapshot(token, runtime, control, 0, bootstrap, 90000);
        Check(packet.error == Unavailable, "Bootstrap error must survive a connection epoch with no script frame.");
        Check(std::string(packet.message) == bootstrap.message, "Missing export names must survive stale-frame normalization.");
        Check(packet.stateFlags == Blocked && !packet.capabilities, "Failed bootstrap cannot announce native readiness.");
        Check(packet.tick == 0 && !packet.ped && !packet.appliedRevision, "No native observation may be invented.");
        Check(std::memcmp(packet.token, token, sizeof(token)) == 0, "Failure status retains this connection's token.");
    }
}

void BootstrapFailureRejectsOldRuntimeClaims() {
    const std::uint8_t token[16]{4};
    RuntimeStatus runtime{};
    runtime.tick = 5000; runtime.capabilities = KnownCapabilities; runtime.ped = 123;
    runtime.stateFlags = Ready | SettingsActive; runtime.appliedRevision = 8;
    runtime.action = Revive; runtime.actionNonce = 7; runtime.actionResult = Observed;
    ControlSnapshot control{};
    control.sessionEpoch = 3; control.settings.revision = 8;
    ModuleBootstrapFailure bootstrap{};
    bootstrap.failed = true;
    CopyStatusMessage(bootstrap.message, "Missing CFX module: scripthookv.dll.");
    const auto packet = BuildStatusSnapshot(token, runtime, control, 3, bootstrap, 5001);
    Check(packet.error == Unavailable && packet.stateFlags == Blocked, "Bootstrap failure dominates old ready state.");
    Check(!packet.tick && !packet.capabilities && !packet.ped && !packet.build, "Bootstrap failure clears observation fields.");
    Check(!packet.appliedRevision && packet.desiredRevision == 8, "Desired settings are distinct from unobserved effects.");
    Check(packet.action == NoAction && packet.actionResult == NoResult && !packet.actionNonce,
        "A failed bootstrap cannot claim a previously observed action.");
}

void NoFrameDoesNotProvePermissionDenial() {
    const std::uint8_t token[16]{};
    const RuntimeStatus runtime{};
    ControlSnapshot control{};
    control.sessionEpoch = 1;
    const auto packet = BuildStatusSnapshot(token, runtime, control, 0, {}, 7000);
    Check(packet.error == FrameStale && packet.stateFlags == Blocked, "No frame remains unready.");
    const std::string message(packet.message);
    Check(message.find("permission denial is not confirmed") != std::string::npos,
        "No frame must not be presented as a confirmed server permission denial.");
    Check(!packet.capabilities && !packet.tick && !packet.ped, "Waiting cannot advertise available natives.");
}

void RuntimeFailureAndNewQueuedActionKeepTheirMeaning() {
    const std::uint8_t token[16]{};
    RuntimeStatus runtime{};
    runtime.error = Unavailable;
    CopyStatusMessage(runtime.message, "The native client requires an observed game build of 3258.");
    ControlSnapshot control{};
    control.sessionEpoch = 5;
    const auto packet = BuildStatusSnapshot(token, runtime, control, 5, {}, 8000);
    Check(packet.error == Unavailable && std::string(packet.message) == runtime.message,
        "A concrete runtime failure must not be replaced by a generic stale-frame error.");
    runtime.action = Revive; runtime.actionNonce = 19; runtime.actionResult = Observed;
    runtime.tick = 8000;
    control.sessionEpoch = 6; control.action = LockVehicle; control.actionNonce = 1;
    const auto pending = BuildStatusSnapshot(token, runtime, control, 5, {}, 8001);
    Check(pending.action == LockVehicle && pending.actionNonce == 1 && pending.actionResult == Pending,
        "A new epoch cannot inherit an old action result or high nonce.");
    Check(pending.appliedRevision == 0 && pending.capabilities == 0 && pending.stateFlags == Blocked,
        "A new connection must wait for its own script observation.");
}

void FreshAndStaleRuntimeRespectReadiness() {
    const std::uint8_t token[16]{};
    RuntimeStatus runtime{};
    runtime.tick = 10000; runtime.ped = 52; runtime.build = 3258;
    runtime.stateFlags = Ready | SettingsActive | PedAlive;
    runtime.capabilities = CapabilityGodMode; runtime.appliedRevision = 9;
    CopyStatusMessage(runtime.message, "Native settings observed.");
    ControlSnapshot control{};
    control.sessionEpoch = 2; control.settings.revision = 8;
    const auto fresh = BuildStatusSnapshot(token, runtime, control, 2, {}, 10500);
    Check(fresh.stateFlags == runtime.stateFlags && fresh.capabilities == CapabilityGodMode && fresh.ped == 52,
        "Fresh matching native state retains readiness and capabilities.");
    Check(fresh.appliedRevision == 8 && fresh.desiredRevision == 8, "Applied revision never exceeds desired revision.");
    const auto stale = BuildStatusSnapshot(token, runtime, control, 2, {}, 11001);
    Check(stale.error == FrameStale && !(stale.stateFlags & (Ready | SettingsActive)) && !stale.capabilities,
        "An old frame loses native readiness.");
    const auto future = BuildStatusSnapshot(token, runtime, control, 2, {}, 9999);
    Check(future.error == FrameStale && !(future.stateFlags & Ready), "A future native tick cannot prove readiness.");
}

void MessagesRemainBoundedAsciiAndProtocolErrorsRemainVisible() {
    const std::uint8_t token[16]{};
    std::string longMessage(300, 'x');
    longMessage[4] = '\n'; longMessage[6] = static_cast<char>(0xC3);
    ModuleBootstrapFailure bootstrap{};
    bootstrap.failed = true;
    CopyStatusMessage(bootstrap.message, longMessage.c_str());
    Check(std::strlen(bootstrap.message) == 159 && bootstrap.message[159] == '\0', "Status messages are limited to 159 bytes plus terminator.");
    Check(bootstrap.message[4] == '?' && bootstrap.message[6] == '?', "Status message bytes remain printable ASCII.");
    const auto packet = BuildStatusSnapshot(token, {}, {}, 0, bootstrap, 1, ProtocolInvalid, "Unsupported command.");
    Check(packet.error == ProtocolInvalid && std::string(packet.message) == "Unsupported command.",
        "A request-specific protocol rejection must remain visible.");
    Check(packet.stateFlags == Blocked && !packet.capabilities, "Protocol rejection cannot turn a failed bootstrap ready.");
}
} // namespace

int main() {
    try {
        BootstrapFailureSurvivesHelloAndReconnect();
        BootstrapFailureRejectsOldRuntimeClaims();
        NoFrameDoesNotProvePermissionDenial();
        RuntimeFailureAndNewQueuedActionKeepTheirMeaning();
        FreshAndStaleRuntimeRespectReadiness();
        MessagesRemainBoundedAsciiAndProtocolErrorsRemainVisible();
        std::cout << "Native status snapshot: 6 suites, " << assertions << " assertions passed.\n";
        return EXIT_SUCCESS;
    } catch (const std::exception& error) {
        std::cerr << "Native status snapshot failure: " << error.what() << '\n';
        return EXIT_FAILURE;
    }
}
