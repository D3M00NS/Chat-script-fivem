// C# / src/ZyonMenu.Windows/ClientDiagnostics.cs / .NET 8 / bounded read-only FiveM b3258 diagnostics.
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ZyonMenu.Windows;

internal static class ClientDiagnostics
{
    private sealed record ModuleSnapshot(string Name, ulong BaseAddress, uint ImageSize, string? FileVersion)
    {
        public bool Contains(ulong address) => address >= BaseAddress && address - BaseAddress < ImageSize;
    }

    private static readonly HashSet<string> SelectedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "FiveM_b3258_GTAProcess.exe", "rage-scripting-five.dll", "scripting-gta.dll", "gta-game-five.dll",
        "gta-net-five.dll", "citizen-playernames-five.dll", "citizen-resources-gta.dll",
        "rage-nutsnbolts-five.dll", "citizen-game-main.dll", "scripthookv.dll", "ZyonClient.asi"
    };

    public static object Capture(GameMemory? memory)
    {
        if (memory is null || !memory.IsAlive)
            return new { Available = false, Message = "Não há handle ativo do processo para esta captura." };
        try
        {
            using var process = Process.GetProcessById(memory.ProcessId);
            var modules = new List<ModuleSnapshot>();
            foreach (ProcessModule module in process.Modules)
            {
                if (!SelectedNames.Contains(module.ModuleName) || module.ModuleMemorySize <= 0) continue;
                string? version = null;
                try { version = FileVersionInfo.GetVersionInfo(module.FileName).FileVersion?.Trim(); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or Win32Exception or ArgumentException)
                { /* Missing version data is represented by null, without exposing full paths. */ }
                modules.Add(new(module.ModuleName, unchecked((ulong)module.BaseAddress.ToInt64()),
                    (uint)module.ModuleMemorySize, version));
            }
            return new
            {
                Available = true,
                OperatingSystem = RuntimeInformation.OSDescription,
                Framework = RuntimeInformation.FrameworkDescription,
                memory.ProcessId,
                memory.ProcessName,
                MainModuleBase = $"0x{memory.ModuleBase:X}",
                MainImageSize = memory.ImageSize,
                Modules = modules.Select(module => new
                {
                    module.Name, BaseAddress = $"0x{module.BaseAddress:X}", module.ImageSize, module.FileVersion
                }).ToArray(),
                PlayerGetters = CapturePlayerGetters(memory, modules)
            };
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException or NotSupportedException or IOException or UnauthorizedAccessException)
        {
            return new
            {
                Available = false,
                Message = "Não foi possível consultar os módulos do processo nesta captura.",
                ErrorType = error.GetType().Name,
                NativeErrorCode = error is Win32Exception native ? native.NativeErrorCode : 0
            };
        }
    }

    private static object CapturePlayerGetters(GameMemory memory, List<ModuleSnapshot> modules)
    {
        var game = modules.FirstOrDefault(module => module.Name.Equals("gta-game-five.dll", StringComparison.OrdinalIgnoreCase));
        if (game is null) return new { Available = false, Message = "gta-game-five.dll não está nos módulos consultados." };
        if (game.FileVersion != "1.0.0.37151" || game.ImageSize <= 0x33B48)
            return new { Available = false, Message = "Os getters foram inspecionados somente para a versão 1.0.0.37151." };

        // These exact accessor bodies were established from the supplied file by static inspection.
        if (!Matches(memory, game.BaseAddress + 0x10850, [0x48, 0xFF, 0x25, 0xF1, 0xF7, 0x01, 0x00]) ||
            !Matches(memory, game.BaseAddress + 0x10860, [0x48, 0x63, 0x05, 0xD9, 0x32, 0x02, 0x00, 0x48, 0x03, 0xC1, 0xC3]) ||
            !Matches(memory, game.BaseAddress + 0x10830, [0x0F, 0xB6, 0x15, 0xF9, 0x32, 0x02, 0x00, 0x48, 0x8B, 0x01, 0x4C, 0x8B, 0x04, 0x02, 0x49, 0xFF, 0xE0]))
            return new { Available = false, Message = "Os bytes dos accessors diferem do arquivo inspecionado; offsets não foram usados." };

        ulong? target = ReadValue<ulong>(memory, game.BaseAddress + 0x30048);
        bool inMain = target is not null && target.Value >= memory.ModuleBase &&
            target.Value - memory.ModuleBase < memory.ImageSize;
        return new
        {
            Available = true,
            AccessorBytesMatched = true,
            GetPlayerTarget = target is null ? null : $"0x{target:X}",
            GetPlayerTargetInMainImage = inMain,
            GamerInfoVtableByteOffset = ReadValue<byte>(memory, game.BaseAddress + 0x33B30),
            NonPhysicalPlayerIndexOffset = ReadValue<int>(memory, game.BaseAddress + 0x33B34),
            ActivePlayerIndexOffset = ReadValue<int>(memory, game.BaseAddress + 0x33B38),
            PhysicalPlayerIndexOffset = ReadValue<int>(memory, game.BaseAddress + 0x33B3C),
            PlayerInfoFieldOffset = ReadValue<int>(memory, game.BaseAddress + 0x33B40),
            PlayerClassSize = ReadValue<int>(memory, game.BaseAddress + 0x33B44),
            CodeSnapshots = inMain ? CaptureCodeHops(memory, modules, target!.Value) : [],
            Message = "Leituras para análise; não estabelecem nomes, IDs de servidor ou execução de natives."
        };
    }

    private static object[] CaptureCodeHops(GameMemory memory, List<ModuleSnapshot> modules, ulong address)
    {
        var output = new List<object>();
        var visited = new HashSet<ulong>();
        for (int hop = 0; hop <= 4 && visited.Add(address); hop++)
        {
            var module = modules.FirstOrDefault(item => item.Contains(address));
            if (module is null)
            {
                output.Add(new { Address = $"0x{address:X}", Message = "Destino fora dos módulos selecionados; leitura interrompida." });
                break;
            }
            int length = (int)Math.Min(160UL, module.ImageSize - (address - module.BaseAddress));
            if (!memory.TryReadBytes(address, length, out var code))
            {
                output.Add(new { module.Name, Address = $"0x{address:X}", Message = "Código não pôde ser lido." });
                break;
            }
            output.Add(new { module.Name, Address = $"0x{address:X}", Bytes = Convert.ToHexString(code) });
            ulong next;
            if (code.Length >= 5 && code[0] == 0xE9)
                next = unchecked((ulong)((long)address + 5 + BitConverter.ToInt32(code, 1)));
            else if (code.Length >= 6 && code[0] == 0xFF && code[1] == 0x25)
            {
                ulong slot = unchecked((ulong)((long)address + 6 + BitConverter.ToInt32(code, 2)));
                if (!memory.TryRead(slot, out next)) break;
            }
            else if (code.Length >= 7 && code[0] == 0x48 && code[1] == 0xFF && code[2] == 0x25)
            {
                ulong slot = unchecked((ulong)((long)address + 7 + BitConverter.ToInt32(code, 3)));
                if (!memory.TryRead(slot, out next)) break;
            }
            else if (code.Length >= 12 && code[0] == 0x48 && code[1] == 0xB8 && code[10] == 0xFF && code[11] == 0xE0)
                next = BitConverter.ToUInt64(code, 2);
            else break;
            address = next;
        }
        return output.ToArray();
    }

    private static bool Matches(GameMemory memory, ulong address, byte[] expected) =>
        memory.TryReadBytes(address, expected.Length, out var actual) && actual.AsSpan().SequenceEqual(expected);

    private static T? ReadValue<T>(GameMemory memory, ulong address) where T : unmanaged =>
        memory.TryRead(address, out T value) ? value : null;
}
