#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <psapi.h>
#include <sddl.h>

#include <algorithm>
#include <cstdint>
#include <cstring>
#include <cwchar>
#include <string>

#include "Engine.h"
#include "NativeInvoker.h"
#include "Protocol.h"
#include "PeExportLookup.h"

namespace {

using namespace zyon;
using ScriptEntry = void (*)();
using RegisterScript = void (*)(HMODULE, ScriptEntry);
using UnregisterScript = void (*)(ScriptEntry);
using WaitScript = void (*)(unsigned long);
using InitNative = void (*)(std::uint64_t);
using PushNative = void (*)(std::uint64_t);
using CallNative = std::uint64_t* (*)();
using NativeHandler = void (*)(void*);
using LookupNative = NativeHandler (*)(std::uint64_t);

class ScriptHookInvoker final : public INativeInvoker {
public:
    bool Resolve() noexcept {
        const HMODULE module = GetModuleHandleW(L"scripthookv.dll");
        if (!module) return false;
        // CFX exports these public ScriptHookV signatures with MSVC C++ linkage.
        // The pointer-only Win64 ABI is compatible with the GNU cross compiler.
        // Plain spellings are accepted only if the loaded implementation
        // actually exports them; missing functions leave the module unavailable.
        registerScript = Find<RegisterScript>(module,
            "?scriptRegister@@YAXPEAUHINSTANCE__@@P6AXXZ@Z", "scriptRegister",
            "?scriptRegister@@YAXPEAXP6AXXZ@Z");
        unregisterScript = Find<UnregisterScript>(module,
            "?scriptUnregister@@YAXP6AXXZ@Z", "scriptUnregister");
        waitScript = Find<WaitScript>(module, "?scriptWait@@YAXK@Z", "scriptWait");
        init = Find<InitNative>(module, "?nativeInit@@YAX_K@Z", "nativeInit");
        push = Find<PushNative>(module, "?nativePush64@@YAX_K@Z", "nativePush64");
        call = Find<CallNative>(module, "?nativeCall@@YAPEA_KXZ", "nativeCall");
        const auto engine = GetModuleHandleW(L"rage-scripting-five.dll");
        MODULEINFO information{};
        if (engine && K32GetModuleInformation(GetCurrentProcess(), engine, &information, sizeof(information))) {
            const auto* image = static_cast<const std::uint8_t*>(information.lpBaseOfDll);
            const auto rva = FindUniqueExportRva(image, information.SizeOfImage, "?GetNativeHandler@scrEngine@rage@@");
            if (rva) lookup = reinterpret_cast<LookupNative>(const_cast<std::uint8_t*>(image) + rva);
        }
        return registerScript && waitScript && init && push && call && lookup;
    }

    bool HasNative(std::uint64_t hash) const noexcept override {
        // This public CFX lookup maps original hashes internally. We use it
        // only as an existence check, never to invoke the returned handler.
        return lookup && lookup(hash) != nullptr;
    }

    bool Invoke(std::uint64_t hash, const std::uint64_t* arguments,
                std::size_t count, NativeResult& result) noexcept override {
        result = {};
        if (!init || !push || !call || !HasNative(hash) || count > 32 || (count && !arguments)) return false;
        init(hash);
        for (std::size_t index = 0; index < count; ++index) push(arguments[index]);
        const auto* returned = call();
        if (!returned) return false;
        // RAGE vectors occupy three 8-byte slots, with float bits in each
        // slot's low word. Copy before another native uses this shared buffer.
        std::memcpy(result.words, returned, sizeof(result.words));
        return true;
    }

    RegisterScript registerScript = nullptr;
    UnregisterScript unregisterScript = nullptr;
    WaitScript waitScript = nullptr;

private:
    template<typename T>
    static T Find(HMODULE module, const char* decorated, const char* plain,
                  const char* alternative = nullptr) noexcept {
        auto function = GetProcAddress(module, decorated);
        if (!function) function = GetProcAddress(module, plain);
        if (!function && alternative) function = GetProcAddress(module, alternative);
        // GetProcAddress uses an untyped Win64 function pointer. Copy the
        // address representation instead of an incompatible function cast;
        // the explicit signatures above define the documented target ABI.
        static_assert(sizeof(T) == sizeof(function));
        T resolved = nullptr;
        std::memcpy(&resolved, &function, sizeof(resolved));
        return resolved;
    }
    InitNative init = nullptr;
    PushNative push = nullptr;
    CallNative call = nullptr;
    LookupNative lookup = nullptr;
};

struct SharedState {
    SRWLOCK lock = SRWLOCK_INIT;
    ControlSnapshot control{};
    RuntimeStatus runtime{};
    std::uint64_t runtimeEpoch = 0;
};

SharedState shared;
ScriptHookInvoker invoker;
volatile LONG stopping = FALSE;

bool Stopping() noexcept { return InterlockedCompareExchange(&stopping, 0, 0) != 0; }

void CopyMessage(char (&target)[160], const char* source) noexcept {
    std::memset(target, 0, sizeof(target));
    if (source) std::strncpy(target, source, sizeof(target) - 1);
}

void TransportOffline() noexcept {
    AcquireSRWLockExclusive(&shared.lock);
    shared.control.connected = false;
    ReleaseSRWLockExclusive(&shared.lock);
}

StatusPacket MakeStatus(const std::uint8_t (&token)[16], std::uint32_t error = 0,
                        const char* detail = nullptr) noexcept {
    RuntimeStatus runtime;
    ControlSnapshot control;
    std::uint64_t runtimeEpoch;
    AcquireSRWLockShared(&shared.lock);
    runtime = shared.runtime;
    control = shared.control;
    runtimeEpoch = shared.runtimeEpoch;
    ReleaseSRWLockShared(&shared.lock);

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
        CopyMessage(packet.message, runtime.message);
    } else {
        packet.error = WaitingNative;
        packet.stateFlags = Blocked;
        CopyMessage(packet.message, "Waiting for a game script frame for this connection.");
    }
    if (control.actionNonce > packet.actionNonce) {
        packet.actionNonce = control.actionNonce;
        packet.action = control.action;
        packet.actionResult = Pending;
    }
    const auto now = GetTickCount64();
    if (!packet.tick || now < packet.tick || now - packet.tick > MaximumFrameAgeMilliseconds) {
        packet.stateFlags = (packet.stateFlags & ~(Ready | SettingsActive)) | Blocked;
        packet.capabilities = 0;
        if (packet.error != Unavailable) {
            packet.error = FrameStale;
            CopyMessage(packet.message,
                "No authorized game frame. Check ScriptHook permission and whether the player is in the game.");
        }
    }
    if (error) {
        packet.error = error;
        CopyMessage(packet.message, detail);
    }
    return packet;
}

bool ReadExact(HANDLE pipe, void* destination, DWORD length, ULONGLONG deadline) noexcept {
    auto* bytes = static_cast<std::uint8_t*>(destination);
    DWORD offset = 0;
    while (offset < length && !Stopping() && GetTickCount64() < deadline) {
        DWORD available = 0;
        if (!PeekNamedPipe(pipe, nullptr, 0, nullptr, &available, nullptr)) return false;
        if (!available) { Sleep(5); continue; }
        DWORD received = 0;
        if (!ReadFile(pipe, bytes + offset, std::min(length - offset, available), &received, nullptr))
            return false;
        if (!received) return false;
        offset += received;
    }
    return offset == length;
}

bool WriteExact(HANDLE pipe, const void* source, DWORD length) noexcept {
    const auto* bytes = static_cast<const std::uint8_t*>(source);
    const auto deadline = GetTickCount64() + 500;
    DWORD offset = 0;
    while (offset < length && !Stopping() && GetTickCount64() < deadline) {
        DWORD sent = 0;
        if (!WriteFile(pipe, bytes + offset, length - offset, &sent, nullptr)) {
            if (GetLastError() != ERROR_NO_DATA) return false;
        }
        if (!sent) { Sleep(5); continue; }
        offset += sent;
    }
    return offset == length;
}

bool Reply(HANDLE pipe, const std::uint8_t (&token)[16], std::uint32_t error = 0,
           const char* message = nullptr) noexcept {
    const auto packet = MakeStatus(token, error, message);
    PacketHeader header;
    header.kind = static_cast<std::uint16_t>(PacketKind::Status);
    header.length = sizeof(packet);
    return WriteExact(pipe, &header, sizeof(header)) && WriteExact(pipe, &packet, sizeof(packet));
}

bool EqualToken(const std::uint8_t* left, const std::uint8_t* right) noexcept {
    unsigned difference = 0;
    for (std::size_t index = 0; index < 16; ++index) difference |= left[index] ^ right[index];
    return difference == 0;
}

bool NonzeroToken(const std::uint8_t* token) noexcept {
    unsigned value = 0;
    for (std::size_t index = 0; index < 16; ++index) value |= token[index];
    return value != 0;
}

void Serve(HANDLE pipe, std::uint64_t epoch) noexcept {
    std::uint8_t token[16]{};
    bool hello = false;
    while (!Stopping()) {
        PacketHeader header{};
        std::uint8_t payload[sizeof(SettingsPacket)]{};
        if (!ReadExact(pipe, &header, sizeof(header), GetTickCount64() + 3500)) break;
        const auto kind = static_cast<PacketKind>(header.kind);
        const bool small = kind == PacketKind::Hello || kind == PacketKind::Poll || kind == PacketKind::Disconnect;
        const auto expectedLength = small ? sizeof(TokenPacket) : kind == PacketKind::Settings ? sizeof(SettingsPacket) :
            kind == PacketKind::Action ? sizeof(ActionPacket) : 0u;
        if (header.magic != ProtocolMagic || header.version != ProtocolVersion || header.reserved ||
            !expectedLength || header.length != expectedLength) break;
        if (!ReadExact(pipe, payload, header.length, GetTickCount64() + 500)) break;
        if (!hello) {
            if (kind != PacketKind::Hello || !NonzeroToken(payload)) break;
            std::memcpy(token, payload, sizeof(token));
            AcquireSRWLockExclusive(&shared.lock);
            shared.control = {};
            shared.control.sessionEpoch = epoch;
            shared.control.lastSeenMs = GetTickCount64();
            shared.control.connected = true;
            ReleaseSRWLockExclusive(&shared.lock);
            hello = true;
            if (!Reply(pipe, token)) break;
            continue;
        }
        if (kind == PacketKind::Hello || !EqualToken(token, payload)) break;
        if (kind == PacketKind::Disconnect) {
            TransportOffline();
            Reply(pipe, token);
            break;
        }
        std::uint32_t error = NoError;
        const char* message = nullptr;
        AcquireSRWLockExclusive(&shared.lock);
        if (kind == PacketKind::Settings) {
            SettingsPacket packet;
            std::memcpy(&packet, payload, sizeof(packet));
            const Settings candidate{packet.revision, packet.flags, packet.speed, packet.silentFov,
                packet.silentBone, packet.silentActivation, packet.silentOptions};
            if (!packet.revision || packet.revision <= shared.control.settings.revision) {
                error = StaleRevisionOrNonce; message = "Settings revision must increase within this connection.";
            } else if (!SettingsInRange(candidate)) {
                error = ProtocolInvalid; message = "Unsupported setting flags, noclip speed or directed-fire configuration.";
            } else {
                shared.control.settings = candidate;
            }
        } else if (kind == PacketKind::Action) {
            ActionPacket packet;
            std::memcpy(&packet, payload, sizeof(packet));
            const bool queued = HasQueuedAction(shared.control, shared.runtime, shared.runtimeEpoch, epoch);
            const bool pending = shared.runtimeEpoch == epoch && shared.runtime.actionResult == Pending;
            if (!packet.nonce || packet.nonce <= shared.control.actionNonce) {
                error = StaleRevisionOrNonce; message = "Action nonce must increase within this connection.";
            } else if (!packet.expectedPed || (packet.action != Revive && packet.action != Suicide &&
                       packet.action != LockVehicle && packet.action != UnlockVehicle)) {
                error = ProtocolInvalid; message = "Action requires a valid action kind and captured player ped.";
            } else if (queued || pending) {
                error = ActionBusy; message = "Wait for the previous native action to finish.";
            } else {
                shared.control.actionNonce = packet.nonce;
                shared.control.action = packet.action;
                shared.control.expectedPed = packet.expectedPed;
            }
        }
        if (!error) shared.control.lastSeenMs = GetTickCount64();
        ReleaseSRWLockExclusive(&shared.lock);
        if (!Reply(pipe, token, error, message)) break;
        if (error == ProtocolInvalid) break;
    }
    TransportOffline();
}

PSECURITY_DESCRIPTOR PipeSecurity() noexcept {
    HANDLE processToken = nullptr;
    if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &processToken)) return nullptr;
    DWORD needed = 0;
    GetTokenInformation(processToken, TokenUser, nullptr, 0, &needed);
    if (!needed || needed > 4096) { CloseHandle(processToken); return nullptr; }
    alignas(TOKEN_USER) BYTE buffer[4096]{};
    if (!GetTokenInformation(processToken, TokenUser, buffer, sizeof(buffer), &needed)) {
        CloseHandle(processToken); return nullptr;
    }
    CloseHandle(processToken);
    LPWSTR sid = nullptr;
    if (!ConvertSidToStringSidW(reinterpret_cast<TOKEN_USER*>(buffer)->User.Sid, &sid)) return nullptr;
    const std::wstring descriptor = std::wstring(L"D:P(A;;GA;;;SY)(A;;GA;;;") + sid + L")";
    LocalFree(sid);
    PSECURITY_DESCRIPTOR security = nullptr;
    if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(descriptor.c_str(), SDDL_REVISION_1,
                                                              &security, nullptr)) return nullptr;
    return security;
}

DWORD WINAPI PipeWorker(void*) noexcept {
    auto* descriptor = PipeSecurity();
    if (!descriptor) return 1;
    SECURITY_ATTRIBUTES security{sizeof(SECURITY_ATTRIBUTES), descriptor, FALSE};
    wchar_t name[128]{};
    std::swprintf(name, sizeof(name) / sizeof(name[0]), L"\\\\.\\pipe\\ZyonClientBridge-v2-%lu", GetCurrentProcessId());
    std::uint64_t epoch = 0;
    while (!Stopping()) {
        HANDLE pipe = CreateNamedPipeW(name, PIPE_ACCESS_DUPLEX | FILE_FLAG_FIRST_PIPE_INSTANCE,
            PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_NOWAIT | PIPE_REJECT_REMOTE_CLIENTS,
            1, 4096, 4096, 500, &security);
        if (pipe == INVALID_HANDLE_VALUE) break;
        bool connected = false;
        while (!Stopping()) {
            if (ConnectNamedPipe(pipe, nullptr) || GetLastError() == ERROR_PIPE_CONNECTED) {
                connected = true; break;
            }
            const DWORD error = GetLastError();
            if (error != ERROR_PIPE_LISTENING && error != ERROR_NO_DATA) break;
            Sleep(10);
        }
        if (connected) Serve(pipe, ++epoch);
        DisconnectNamedPipe(pipe);
        CloseHandle(pipe);
    }
    LocalFree(descriptor);
    return 0;
}

void ScriptMain() {
    // The CFX scheduler supplies the active RAGE thread and fiber context.
    // Game natives are never called by DllMain or the pipe worker.
    Engine engine(invoker);
    while (!Stopping()) {
        ControlSnapshot snapshot;
        AcquireSRWLockShared(&shared.lock);
        snapshot = shared.control;
        ReleaseSRWLockShared(&shared.lock);
        DWORD foregroundProcess = 0;
        const auto foreground = GetForegroundWindow();
        if (foreground) GetWindowThreadProcessId(foreground, &foregroundProcess);
        snapshot.gameFocused = foreground && foregroundProcess == GetCurrentProcessId();
        engine.Step(snapshot, GetTickCount64());
        const auto status = engine.Status();
        AcquireSRWLockExclusive(&shared.lock);
        shared.runtime = status;
        shared.runtimeEpoch = snapshot.sessionEpoch;
        ReleaseSRWLockExclusive(&shared.lock);
        invoker.waitScript(0);
    }
}

} // namespace

BOOL WINAPI DllMain(HINSTANCE module, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH) {
        DisableThreadLibraryCalls(module);
        // Prevent unsupported live unloading while a scheduler callback or pipe
        // worker can still refer to this module. Installation/removal is done
        // while FiveM is closed; normal process shutdown releases everything.
        HMODULE pinned = nullptr;
        if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_PIN,
                reinterpret_cast<LPCWSTR>(&DllMain), &pinned)) return FALSE;
        if (invoker.Resolve()) {
            invoker.registerScript(module, ScriptMain);
        } else {
            shared.runtime.stateFlags = Blocked;
            shared.runtime.error = Unavailable;
            CopyMessage(shared.runtime.message, "Required CFX ScriptHookV exports or native lookup are unavailable.");
        }
        // The worker performs no DLL loading or native calls. We never wait for
        // it under the loader lock; Windows starts its entry after DllMain exits.
        HANDLE worker = CreateThread(nullptr, 0, PipeWorker, nullptr, 0, nullptr);
        if (worker) CloseHandle(worker);
    } else if (reason == DLL_PROCESS_DETACH) {
        InterlockedExchange(&stopping, TRUE);
        if (invoker.unregisterScript) invoker.unregisterScript(ScriptMain);
    }
    return TRUE;
}
