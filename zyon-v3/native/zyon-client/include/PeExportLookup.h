#pragma once

#include <cstddef>
#include <cstdint>
#include <cstring>

namespace zyon {

// Looks at a loaded PE32+ image, not file offsets. This only identifies a
// public export; it never scans code, guesses an RVA or invokes a raw handler.
// Reject malformed tables, forwarded exports and an ambiguous name prefix.
inline std::uint32_t FindUniqueExportRva(const std::uint8_t* image, std::size_t size,
                                       const char* prefix) noexcept {
    if (!image || !prefix || !*prefix) return 0;
    const auto range = [size](std::size_t offset, std::size_t length) {
        return offset <= size && length <= size - offset;
    };
    const auto word = [&](std::size_t offset, std::uint16_t& value) {
        if (!range(offset, sizeof(value))) return false;
        std::memcpy(&value, image + offset, sizeof(value)); return true;
    };
    const auto dword = [&](std::size_t offset, std::uint32_t& value) {
        if (!range(offset, sizeof(value))) return false;
        std::memcpy(&value, image + offset, sizeof(value)); return true;
    };
    std::uint16_t mz = 0, magic = 0, optionalSize = 0;
    std::uint32_t pe = 0, signature = 0, imageSize = 0, directories = 0;
    if (!word(0, mz) || mz != 0x5A4D || !dword(0x3C, pe) || !range(pe, 24) ||
        !dword(pe, signature) || signature != 0x4550 || !word(pe + 20, optionalSize) ||
        optionalSize < 120 || !range(pe + 24, optionalSize) || !word(pe + 24, magic) || magic != 0x20B ||
        !dword(pe + 24 + 56, imageSize) || imageSize != size ||
        !dword(pe + 24 + 108, directories) || !directories) return 0;
    std::uint32_t exports = 0, exportSize = 0, functions = 0, names = 0;
    std::uint32_t functionTable = 0, nameTable = 0, ordinalTable = 0;
    if (!dword(pe + 24 + 112, exports) || !dword(pe + 24 + 116, exportSize) ||
        !exports || exportSize < 40 || !range(exports, exportSize) ||
        !dword(exports + 20, functions) || !dword(exports + 24, names) ||
        !dword(exports + 28, functionTable) || !dword(exports + 32, nameTable) ||
        !dword(exports + 36, ordinalTable) || !functions || !names || names > functions ||
        !range(functionTable, std::size_t{functions} * 4) ||
        !range(nameTable, std::size_t{names} * 4) || !range(ordinalTable, std::size_t{names} * 2)) return 0;
    const auto prefixSize = std::strlen(prefix);
    std::uint32_t found = 0;
    for (std::uint32_t index = 0; index < names; ++index) {
        std::uint32_t name = 0;
        if (!dword(nameTable + std::size_t{index} * 4, name) || !range(name, 1)) return 0;
        const auto* text = reinterpret_cast<const char*>(image + name);
        const auto* end = static_cast<const char*>(std::memchr(text, 0, size - name));
        if (!end) return 0;
        if (static_cast<std::size_t>(end - text) < prefixSize || std::memcmp(text, prefix, prefixSize)) continue;
        std::uint16_t ordinal = 0;
        std::uint32_t target = 0;
        if (!word(ordinalTable + std::size_t{index} * 2, ordinal) || ordinal >= functions ||
            !dword(functionTable + std::size_t{ordinal} * 4, target) || !target || !range(target, 1) ||
            (target >= exports && target - exports < exportSize) || found) return 0;
        found = target;
    }
    return found;
}

} // namespace zyon
