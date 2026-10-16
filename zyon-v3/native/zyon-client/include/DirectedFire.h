#pragma once

#include "NativeInvoker.h"
#include <cstdint>
#include <initializer_list>

namespace zyon {

struct DirectedFireSettings {
    bool enabled = false, onlyPlayers = true, onlyVisible = true;
    std::uint32_t fov = 100, bone = 0, activation = 0;
};

// Experimental SET_PED_SHOOTS_AT_COORD request, not original-bullet redirection.
// Call only on the game script fiber. While enabled with a valid focused local
// ped, normal firing is suppressed, including frames without a usable target.
// A successful Step means valid gating/dispatch, never a confirmed hit.
class DirectedFire {
public:
    explicit DirectedFire(INativeInvoker& natives) : natives_(natives) {}
    bool Available() const;
    void Reset();
    // Run Step before the ammunition-restoration controller, and skip that
    // controller while this is true so it cannot erase the consumption sample.
    bool PendingConsumption() const { return pendingConsumption_; }
    bool Step(std::uint32_t ped, bool alive, bool focused,
              const DirectedFireSettings& settings, std::uint64_t now,
              char (&message)[160]);

private:
    struct Point { float x = 0, y = 0, z = 0; };
    struct Target { std::uint32_t player = 0, ped = 0, model = 0; Point point{}; };
    INativeInvoker& natives_;
    std::uint32_t ped_ = 0, model_ = 0, weapon_ = 0;
    std::uint64_t lastDispatch_ = 0;
    std::uint64_t pendingStarted_ = 0;
    int beforeTotal_ = 0, beforeClip_ = 0;
    bool armed_ = false, triggerDown_ = false, hasDispatch_ = false;
    bool pendingConsumption_ = false;
    char lastResult_[160]{};

    bool Call(std::uint64_t hash, std::initializer_list<std::uint64_t> args,
              NativeResult* result = nullptr);
    bool Integer(std::uint64_t hash, std::initializer_list<std::uint64_t> args,
                 std::uint32_t& value);
    bool Boolean(std::uint64_t hash, std::initializer_list<std::uint64_t> args,
                 bool& value);
    bool Entity(std::uint32_t ped, std::uint32_t& model, bool& valid);
    bool Ammo(std::uint32_t ped, std::uint32_t weapon, int& total, int& clip);
    bool Input(std::uint32_t control, bool& pressed);
    bool Bone(std::uint32_t ped, std::uint32_t bone, Point& point);
    bool InFov(const Point& point, int width, int height, std::uint32_t fov,
               bool& inside, float& squaredDistance);
    bool LocalGate(std::uint32_t ped, std::uint32_t model, std::uint32_t weapon,
                   bool& usable);
    bool ObserveConsumption(std::uint64_t now, char (&message)[160]);
};

} // namespace zyon
