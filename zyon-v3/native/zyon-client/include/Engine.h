#pragma once

#include "NativeInvoker.h"
#include "Protocol.h"
#include <initializer_list>

namespace zyon {

// All game calls, including cleanup, must run on the registered script fiber.
// This class never accepts addresses or native hashes from IPC.
class Engine {
public:
    explicit Engine(INativeInvoker& natives) : natives_(natives) {}
    void Step(const ControlSnapshot& snapshot, std::uint64_t nowMs);
    RuntimeStatus Status() const { return status_; }

private:
    struct Vec { float x = 0, y = 0, z = 0; };
    struct Property {
        std::uint32_t ped = 0, model = 0;
        bool original = false, owned = false;
    };
    INativeInvoker& natives_;
    RuntimeStatus status_{};
    Settings settings_{};
    std::uint64_t epoch_ = 0, minimumRevision_ = 0, failedRevision_ = 0;
    std::uint64_t lastActionNonce_ = 0, pendingStarted_ = 0;
    std::uint32_t pendingPed_ = 0, pendingAction_ = NoAction;
    std::uint32_t ped_ = 0, model_ = 0, timer_ = 0, suppressedFlags_ = 0, unavailableFlags_ = 0;
    std::uint32_t ammoWeapon_ = 0;
    int ammoTotal_ = 0, ammoClip_ = 0;
    bool hasEpoch_ = false, revoked_ = false, timerSeen_ = false, hasSettings_ = false, ammoHasClip_ = false;
    Property god_{}, visible_{}, collision_{}, frozen_{};

    bool Call(std::uint64_t hash, std::initializer_list<std::uint64_t> args, NativeResult* result = nullptr);
    bool Bool(std::uint64_t hash, std::initializer_list<std::uint64_t> args, bool& value);
    bool Integer(std::uint64_t hash, std::initializer_list<std::uint64_t> args, std::uint32_t& value);
    bool Float(std::uint64_t hash, std::initializer_list<std::uint64_t> args, float& value);
    bool Vector(std::uint64_t hash, std::initializer_list<std::uint64_t> args, Vec& value);
    bool ValidatePed(std::uint32_t ped, std::uint32_t* model = nullptr);
    bool SameLocalPed(std::uint32_t ped);
    bool RequiredNativesAvailable() const;
    std::uint32_t AvailableCapabilities() const;
    bool Dead(bool& dead);
    bool RestoreProperty(Property& property, std::uint64_t getter, std::uint64_t setter,
                         bool forcedValue, bool extraArgument);
    bool RestoreFlight();
    bool ReleaseEffects();
    bool ApplyGod();
    bool ApplyVisibility();
    bool ApplyFlight(bool alive, bool focused, bool& waiting);
    bool ApplyAmmo();
    void BeginAction(const ControlSnapshot& snapshot, std::uint64_t nowMs, bool dead);
    void ObserveAction(std::uint64_t nowMs, bool dead);
    void EndAction(std::uint32_t result, const char* message);
    void Message(std::uint32_t error, const char* message);
    void Block(std::uint32_t error, const char* message);
    void NewEpoch(std::uint64_t epoch);
};

} // namespace zyon
