// C# / src/ZyonMenu.Windows/GameMemory.cs / .NET 8 / Windows 10 x64 process memory transport.
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ZyonMenu.Windows;

internal sealed class GameMemoryException : IOException
{
    public GameMemoryException(string message, ulong address = 0, int nativeErrorCode = 0,
        Exception? innerException = null) : base(message, innerException)
    {
        Address = address;
        NativeErrorCode = nativeErrorCode;
    }

    public ulong Address { get; }
    public int NativeErrorCode { get; }
}

/// <summary>
/// Owns the supplied Process after successful attachment and a normal Windows process handle.
/// Writes are restricted to committed, writable data pages; executable pages are never written.
/// </summary>
internal sealed class GameMemory : IDisposable
{
    private const uint RequiredAccess = 0x0008 | 0x0010 | 0x0020 | 0x0400 | 0x00100000;
    private const uint WaitTimeout = 0x00000102;
    private const ulong MinimumUserAddress = 0x0000000000010000;
    private const ulong MaximumUserAddress = 0x00007FFFFFFFFFFF;
    private const int MaximumTransferBytes = 64 * 1024;
    private const int MaximumBulkReadBytes = 32 * 1024 * 1024;
    private const uint MemoryCommit = 0x1000;
    private const uint PageGuard = 0x0100;
    private const uint PageReadOnly = 0x02;
    private const uint PageReadWrite = 0x04;
    private const uint PageWriteCopy = 0x08;
    private const uint PageExecuteRead = 0x20;
    private const uint PageExecuteReadWrite = 0x40;
    private const uint PageExecuteWriteCopy = 0x80;
    private const ushort MachineUnknown = 0;
    private const ushort MachineAmd64 = 0x8664;
    private static readonly nuint MemoryInformationSize = (nuint)Marshal.SizeOf<MemoryBasicInformation>();

    private readonly Process _process;
    private readonly SafeProcessHandle _handle;
    private int _disposed;

    private GameMemory(Process process, SafeProcessHandle handle, int processId, string processName,
        ulong moduleBase, uint imageSize)
    {
        _process = process;
        _handle = handle;
        ProcessId = processId;
        ProcessName = processName;
        ModuleBase = moduleBase;
        ImageSize = imageSize;
    }

    public int ProcessId { get; }
    public string ProcessName { get; }
    public ulong ModuleBase { get; }
    public uint ImageSize { get; }

    public bool IsAlive
    {
        get
        {
            if (Volatile.Read(ref _disposed) != 0)
                return false;
            try
            {
                return WaitForSingleObject(_handle, 0) == WaitTimeout;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
        }
    }

    /// <summary>On failure the caller retains ownership of process; on success this object owns it.</summary>
    public static bool TryOpen(Process process, out GameMemory? memory, out string error)
    {
        memory = null;
        error = string.Empty;
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess)
        {
            error = "Este executável precisa rodar no Windows em um processo x64.";
            return false;
        }
        if (process is null)
        {
            error = "O processo do jogo não foi encontrado.";
            return false;
        }

        SafeProcessHandle? handle = null;
        try
        {
            int processId = process.Id;
            string processName = process.ProcessName;
            handle = OpenProcess(RequiredAccess, false, (uint)processId);
            if (handle.IsInvalid)
            {
                int nativeError = Marshal.GetLastWin32Error();
                error = "O Windows não permitiu abrir o processo do jogo: " + NativeFailure(nativeError);
                return false;
            }
            if (WaitForSingleObject(handle, 0) != WaitTimeout)
            {
                error = "O processo do jogo foi encerrado antes da conexão.";
                return false;
            }
            if (!TryCheckX64(handle, out error))
                return false;

            ProcessModule? module = process.MainModule;
            if (module is null)
            {
                error = "O módulo principal do jogo ainda não está disponível.";
                return false;
            }
            ulong moduleBase = unchecked((ulong)module.BaseAddress.ToInt64());
            int moduleSize = module.ModuleMemorySize;
            if (moduleSize <= 0 || moduleBase < MinimumUserAddress || moduleBase > MaximumUserAddress ||
                (ulong)moduleSize - 1 > MaximumUserAddress - moduleBase)
            {
                error = "O módulo principal do jogo apresentou um intervalo de memória inválido.";
                return false;
            }
            if (WaitForSingleObject(handle, 0) != WaitTimeout)
            {
                error = "O processo do jogo foi encerrado durante a conexão.";
                return false;
            }

            memory = new GameMemory(process, handle, processId, processName, moduleBase, (uint)moduleSize);
            handle = null;
            return true;
        }
        catch (Win32Exception exception)
        {
            error = "Não foi possível consultar o processo do jogo: " + NativeFailure(exception.NativeErrorCode);
            return false;
        }
        catch (InvalidOperationException)
        {
            error = "O processo do jogo não está mais disponível.";
            return false;
        }
        catch (NotSupportedException)
        {
            error = "A consulta exige um processo local x64.";
            return false;
        }
        finally
        {
            handle?.Dispose();
        }
    }

    public bool TryRead<T>(ulong address, out T value) where T : unmanaged =>
        TryReadCore(address, out value, out _, out _);

    public bool TryReadBytes(ulong address, int length, out byte[] bytes) =>
        TryReadBytesCore(address, length, out bytes, out _, out _);

    public byte[] ReadBytes(ulong address, int length)
    {
        if (TryReadBytesCore(address, length, out byte[] bytes, out string failure, out int nativeError))
            return bytes;
        throw new GameMemoryException($"Falha de leitura em 0x{address:X}: {failure}", address, nativeError);
    }

    public T Read<T>(ulong address) where T : unmanaged
    {
        if (TryReadCore(address, out T value, out string failure, out int nativeError))
            return value;
        throw new GameMemoryException($"Falha de leitura em 0x{address:X}: {failure}", address, nativeError);
    }

    public bool TryWrite<T>(ulong address, T value) where T : unmanaged =>
        TryWriteCore(address, value, out _, out _);

    public void Write<T>(ulong address, T value) where T : unmanaged
    {
        if (!TryWriteCore(address, value, out string failure, out int nativeError))
            throw new GameMemoryException($"Falha de escrita em 0x{address:X}: {failure}", address, nativeError);
    }

    public bool IsReadableRange(ulong address, int length) => CheckRegions(address, length, writableData: false);

    private bool TryReadCore<T>(ulong address, out T value, out string failure, out int nativeError)
        where T : unmanaged
    {
        value = default;
        nativeError = 0;
        int size = Unsafe.SizeOf<T>();
        if (!CheckTransfer(address, size, out failure))
            return false;
        try
        {
            Span<byte> bytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref value, 1));
            bool success = ReadProcessMemory(_handle, (nint)address, ref MemoryMarshal.GetReference(bytes),
                (nuint)size, out nuint transferred);
            int lastError = Marshal.GetLastWin32Error();
            if (success && transferred == (nuint)size)
                return true;
            nativeError = lastError == 0 ? 299 : lastError;
            failure = NativeFailure(nativeError);
            value = default;
            return false;
        }
        catch (ObjectDisposedException)
        {
            value = default;
            failure = "A conexão com o processo foi fechada.";
            return false;
        }
    }

    private bool TryReadBytesCore(ulong address, int length, out byte[] bytes, out string failure,
        out int nativeError)
    {
        bytes = [];
        nativeError = 0;
        if (!ValidRange(address, length, MaximumBulkReadBytes))
        {
            failure = "O endereço ou tamanho está fora do intervalo permitido para leitura.";
            return false;
        }
        if (!IsAlive)
        {
            failure = "O processo foi encerrado ou a conexão foi fechada.";
            return false;
        }
        byte[] buffer = new byte[length];
        try
        {
            for (int offset = 0; offset < length;)
            {
                int chunkSize = Math.Min(MaximumTransferBytes, length - offset);
                bool success = ReadProcessMemory(_handle, (nint)(address + (ulong)offset), ref buffer[offset],
                    (nuint)chunkSize, out nuint transferred);
                int lastError = Marshal.GetLastWin32Error();
                if (!success || transferred != (nuint)chunkSize)
                {
                    nativeError = lastError == 0 ? 299 : lastError;
                    failure = NativeFailure(nativeError);
                    return false;
                }
                offset += chunkSize;
            }
            bytes = buffer;
            failure = string.Empty;
            return true;
        }
        catch (ObjectDisposedException)
        {
            failure = "A conexão com o processo foi fechada.";
            return false;
        }
    }

    private bool TryWriteCore<T>(ulong address, T value, out string failure, out int nativeError)
        where T : unmanaged
    {
        nativeError = 0;
        int size = Unsafe.SizeOf<T>();
        if (!CheckTransfer(address, size, out failure))
            return false;
        if (!CheckRegions(address, size, writableData: true))
        {
            failure = "O intervalo não contém somente dados graváveis do processo.";
            return false;
        }
        try
        {
            Span<byte> bytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref value, 1));
            bool success = WriteProcessMemory(_handle, (nint)address, ref MemoryMarshal.GetReference(bytes),
                (nuint)size, out nuint transferred);
            int lastError = Marshal.GetLastWin32Error();
            if (success && transferred == (nuint)size)
                return true;
            nativeError = lastError == 0 ? 299 : lastError;
            failure = NativeFailure(nativeError);
            return false;
        }
        catch (ObjectDisposedException)
        {
            failure = "A conexão com o processo foi fechada.";
            return false;
        }
    }

    private bool CheckTransfer(ulong address, int size, out string failure)
    {
        if (!ValidRange(address, size))
        {
            failure = "O endereço ou tamanho está fora do intervalo permitido.";
            return false;
        }
        if (!IsAlive)
        {
            failure = "O processo foi encerrado ou a conexão foi fechada.";
            return false;
        }
        failure = string.Empty;
        return true;
    }

    private bool CheckRegions(ulong address, int length, bool writableData)
    {
        if (!ValidRange(address, length) || !IsAlive)
            return false;
        ulong cursor = address;
        ulong end = address + (ulong)length;
        try
        {
            for (int regionCount = 0; cursor < end && regionCount < 64; regionCount++)
            {
                if (VirtualQueryEx(_handle, (nint)cursor, out MemoryBasicInformation information,
                        MemoryInformationSize) != MemoryInformationSize || information.State != MemoryCommit ||
                    (information.Protect & PageGuard) != 0)
                    return false;
                uint protection = information.Protect & 0xFF;
                bool permitted = writableData
                    ? protection is PageReadWrite or PageWriteCopy
                    : protection is PageReadOnly or PageReadWrite or PageWriteCopy or PageExecuteRead or
                        PageExecuteReadWrite or PageExecuteWriteCopy;
                if (!permitted)
                    return false;

                ulong regionBase = unchecked((ulong)information.BaseAddress.ToInt64());
                ulong regionSize = information.RegionSize;
                if (regionSize == 0 || regionBase > cursor || regionBase > MaximumUserAddress ||
                    regionSize - 1 > MaximumUserAddress - regionBase)
                    return false;
                ulong regionEnd = regionBase + regionSize;
                if (regionEnd <= cursor)
                    return false;
                cursor = Math.Min(regionEnd, end);
            }
            return cursor == end;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    private static bool ValidRange(ulong address, int size, int maximum = MaximumTransferBytes) =>
        size > 0 && size <= maximum &&
        address >= MinimumUserAddress && address <= MaximumUserAddress &&
        (ulong)size - 1 <= MaximumUserAddress - address;

    private static bool TryCheckX64(SafeProcessHandle handle, out string error)
    {
        try
        {
            if (!IsWow64Process2(handle, out ushort processMachine, out ushort nativeMachine))
            {
                int nativeError = Marshal.GetLastWin32Error();
                error = "Não foi possível verificar a arquitetura do jogo: " + NativeFailure(nativeError);
                return false;
            }
            if ((processMachine == MachineUnknown && nativeMachine == MachineAmd64) ||
                processMachine == MachineAmd64)
            {
                error = string.Empty;
                return true;
            }
            error = "O processo selecionado não é um jogo x64 compatível.";
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            if (!IsWow64Process(handle, out bool wow64))
            {
                int nativeError = Marshal.GetLastWin32Error();
                error = "Não foi possível verificar a arquitetura do jogo: " + NativeFailure(nativeError);
                return false;
            }
            if (!wow64 && Environment.Is64BitOperatingSystem)
            {
                error = string.Empty;
                return true;
            }
            error = "O processo selecionado não é um jogo x64 compatível.";
            return false;
        }
    }

    private static string NativeFailure(int error) => error switch
    {
        5 => "acesso negado pelo Windows.",
        6 => "o identificador do processo não está mais disponível.",
        87 => "o Windows rejeitou os parâmetros da operação.",
        299 => "o Windows não transferiu todos os bytes solicitados.",
        487 or 998 => "o intervalo de memória não está acessível.",
        _ => $"erro do Windows {error}: {new Win32Exception(error).Message}"
    };

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        _handle.Dispose();
        _process.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryBasicInformation
    {
        public nint BaseAddress;
        public nint AllocationBase;
        public uint AllocationProtect;
        public ushort PartitionId;
        public nuint RegionSize;
        public uint State;
        public uint Protect;
        public uint Type;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(SafeProcessHandle handle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWow64Process2(SafeProcessHandle process, out ushort processMachine,
        out ushort nativeMachine);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWow64Process(SafeProcessHandle process,
        [MarshalAs(UnmanagedType.Bool)] out bool wow64Process);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadProcessMemory(SafeProcessHandle process, nint address,
        [In, Out] ref byte buffer, nuint size, out nuint bytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WriteProcessMemory(SafeProcessHandle process, nint address,
        [In] ref byte buffer, nuint size, out nuint bytesWritten);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nuint VirtualQueryEx(SafeProcessHandle process, nint address,
        out MemoryBasicInformation information, nuint length);
}
