#pragma once

#include <cstddef>
#include <cstdint>
#include <cstring>

namespace zyon {

struct NativeResult { std::uint64_t words[3]{}; };

class INativeInvoker {
public:
    virtual ~INativeInvoker() = default;
    // A non-null nativeCall result is not proof that a handler exists: CFX
    // returns its shared result buffer even for an unknown hash.
    virtual bool HasNative(std::uint64_t hash) const noexcept = 0;
    // Called only on the ScriptHook script fiber. Pointer arguments refer to
    // local plugin memory; pointers from IPC are never accepted.
    virtual bool Invoke(std::uint64_t hash, const std::uint64_t* arguments,
                        std::size_t count, NativeResult& result) noexcept = 0;
};

inline std::uint64_t NativeArg(float value) noexcept {
    std::uint32_t bits = 0;
    std::memcpy(&bits, &value, sizeof(bits));
    return bits;
}
inline std::uint64_t NativeArg(std::int32_t value) noexcept {
    return static_cast<std::uint32_t>(value);
}
inline std::uint64_t NativeArg(std::uint32_t value) noexcept { return value; }
inline std::uint64_t NativeArg(bool value) noexcept { return value ? 1u : 0u; }
inline float NativeFloat(std::uint64_t word) noexcept {
    const auto bits = static_cast<std::uint32_t>(word);
    float result = 0;
    std::memcpy(&result, &bits, sizeof(result));
    return result;
}

} // namespace zyon
