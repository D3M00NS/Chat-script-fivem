// C# / fixed local client-module protocol / .NET 8 / little endian, bounded frames.
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace ZyonMenu.Core;

public enum ClientModuleKind : ushort { Hello = 1, Settings = 2, Action = 3, Status = 4, Disconnect = 5, Poll = 6 }
[Flags]
public enum ClientModuleCapabilities : uint
{
    None = 0, God = 1, Noclip = 2, Invisible = 4, FreezeAmmo = 8, ReviveNative = 16, Suicide = 32,
    LockVehicle = 64, UnlockVehicle = 128, DirectedFireExperimental = 256
}
[Flags]
public enum ClientModuleSettingsFlags : uint { None = 0, God = 1, Noclip = 2, Invisible = 4, FreezeAmmo = 8, DirectedFireExperimental = 256 }
[Flags]
public enum ClientModuleStateFlags : uint { None = 0, Ready = 1, SettingsActive = 2, PedAlive = 4, Blocked = 8 }
public enum ClientModuleAction : uint { None = 0, ReviveNative = 1, Suicide = 2, LockVehicle = 3, UnlockVehicle = 4 }
public enum ClientModuleActionResult : uint { None = 0, Pending = 1, Observed = 2, Failed = 3 }

public readonly record struct ClientModuleHeader(ClientModuleKind Kind, int PayloadLength);
public sealed record ClientModuleStatus(
    ulong NativeTick, ulong DesiredRevision, ulong AppliedRevision, ulong ActionNonce,
    ClientModuleCapabilities Capabilities, ClientModuleStateFlags StateFlags,
    ClientModuleAction Action, ClientModuleActionResult ActionResult,
    uint Ped, uint Build, uint Error, string Message);

public static class ClientModuleProtocol
{
    public const int HeaderLength = 16, TokenLength = 16, StatusPayloadLength = 240;
    public const uint Magic = 0x4E4F595A;
    public const ushort Version = 2;
    public const int MaximumNativeFrameAgeMilliseconds = 1000;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static int PayloadLength(ClientModuleKind kind) => kind switch
    {
        ClientModuleKind.Hello or ClientModuleKind.Poll or ClientModuleKind.Disconnect => 16,
        ClientModuleKind.Settings => 48,
        ClientModuleKind.Action => 32,
        ClientModuleKind.Status => StatusPayloadLength,
        _ => throw new InvalidDataException("Tipo de mensagem local desconhecido.")
    };

    public static byte[] EncodeHeader(ClientModuleKind kind)
    {
        byte[] bytes = new byte[HeaderLength];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, Magic);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), Version);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(6), (ushort)kind);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), (uint)PayloadLength(kind));
        return bytes;
    }

    public static ClientModuleHeader DecodeHeader(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != HeaderLength || BinaryPrimitives.ReadUInt32LittleEndian(bytes) != Magic ||
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[4..]) != Version ||
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[12..]) != 0)
            throw new InvalidDataException("Cabeçalho do módulo local inválido.");
        var kind = (ClientModuleKind)BinaryPrimitives.ReadUInt16LittleEndian(bytes[6..]);
        int expected = PayloadLength(kind);
        if (BinaryPrimitives.ReadUInt32LittleEndian(bytes[8..]) != expected)
            throw new InvalidDataException("Comprimento da mensagem local inválido.");
        return new(kind, expected);
    }

    public static byte[] TokenPayload(ReadOnlySpan<byte> token)
    {
        ValidateToken(token);
        return token.ToArray();
    }

    public static byte[] SettingsPayload(ReadOnlySpan<byte> token, ulong revision, ClientModuleSettingsFlags flags, uint speed,
        AimSettings? directedFire = null)
    {
        ValidateToken(token);
        if (revision is 0 or > long.MaxValue || ((uint)flags & ~271u) != 0 || speed is < 1 or > 50)
            throw new ArgumentOutOfRangeException(nameof(revision), "Configuração local fora do contrato.");
        // Inactive UI preferences are not part of this module's execution contract.
        directedFire = flags.HasFlag(ClientModuleSettingsFlags.DirectedFireExperimental)
            ? directedFire ?? new AimSettings() : new AimSettings();
        directedFire.Validate();
        if (!directedFire.OnlyPlayers || directedFire.DrawFov || directedFire.Smooth != 1)
            throw new ArgumentException("Parâmetros não suportados pelo disparo direcionado experimental.", nameof(directedFire));
        byte[] bytes = new byte[48];
        token.CopyTo(bytes);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(16), revision);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(24), (uint)flags);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(28), speed);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(32), (uint)directedFire.Fov);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(36), directedFire.TargetBone switch { "Head" => 0u, "Neck" => 1u, _ => 2u });
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(40), directedFire.ActivationKey switch
            { "RMB" => 0u, "LMB" => 1u, "Alt" => 2u, "Shift" => 3u, _ => 4u });
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(44), 1u | (directedFire.OnlyVisible ? 2u : 0u));
        return bytes;
    }

    public static byte[] ActionPayload(ReadOnlySpan<byte> token, ulong nonce, ClientModuleAction action, uint expectedPed)
    {
        ValidateToken(token);
        if (nonce is 0 or > long.MaxValue || action is not (ClientModuleAction.ReviveNative or ClientModuleAction.Suicide or ClientModuleAction.LockVehicle or ClientModuleAction.UnlockVehicle) || expectedPed == 0)
            throw new ArgumentOutOfRangeException(nameof(nonce), "Ação local fora do contrato.");
        byte[] bytes = new byte[32];
        token.CopyTo(bytes);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(16), nonce);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(24), (uint)action);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(28), expectedPed);
        return bytes;
    }

    public static ClientModuleStatus DecodeStatus(ReadOnlySpan<byte> payload, ReadOnlySpan<byte> expectedToken)
    {
        ValidateToken(expectedToken);
        if (payload.Length != StatusPayloadLength ||
            !CryptographicOperations.FixedTimeEquals(payload[..TokenLength], expectedToken) ||
            BinaryPrimitives.ReadUInt32LittleEndian(payload[76..]) != 0)
            throw new InvalidDataException("Estado local truncado, estrangeiro ou incompatível.");
        var text = payload[80..];
        int terminator = text.IndexOf((byte)0);
        if (terminator < 0 || text[(terminator + 1)..].IndexOfAnyExcept((byte)0) >= 0)
            throw new InvalidDataException("Texto do estado local sem terminação válida.");
        string message;
        try { message = StrictUtf8.GetString(text[..terminator]); }
        catch (DecoderFallbackException exception) { throw new InvalidDataException("Texto local não é UTF-8 válido.", exception); }
        if (message.Any(char.IsControl)) throw new InvalidDataException("Texto local contém caracteres de controle.");
        var status = new ClientModuleStatus(
            BinaryPrimitives.ReadUInt64LittleEndian(payload[16..]),
            BinaryPrimitives.ReadUInt64LittleEndian(payload[24..]),
            BinaryPrimitives.ReadUInt64LittleEndian(payload[32..]),
            BinaryPrimitives.ReadUInt64LittleEndian(payload[40..]),
            (ClientModuleCapabilities)BinaryPrimitives.ReadUInt32LittleEndian(payload[48..]),
            (ClientModuleStateFlags)BinaryPrimitives.ReadUInt32LittleEndian(payload[52..]),
            (ClientModuleAction)BinaryPrimitives.ReadUInt32LittleEndian(payload[56..]),
            (ClientModuleActionResult)BinaryPrimitives.ReadUInt32LittleEndian(payload[60..]),
            BinaryPrimitives.ReadUInt32LittleEndian(payload[64..]),
            BinaryPrimitives.ReadUInt32LittleEndian(payload[68..]),
            BinaryPrimitives.ReadUInt32LittleEndian(payload[72..]), message);
        ValidateStatus(status);
        return status;
    }

    public static byte[] EncodeStatus(ReadOnlySpan<byte> token, ClientModuleStatus status)
    {
        ValidateToken(token);
        ValidateStatus(status);
        byte[] text = StrictUtf8.GetBytes(status.Message);
        if (text.Length >= 160 || status.Message.Any(char.IsControl)) throw new ArgumentException("Texto de estado local inválido.", nameof(status));
        byte[] payload = new byte[StatusPayloadLength];
        token.CopyTo(payload);
        BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(16), status.NativeTick);
        BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(24), status.DesiredRevision);
        BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(32), status.AppliedRevision);
        BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(40), status.ActionNonce);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(48), (uint)status.Capabilities);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(52), (uint)status.StateFlags);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(56), (uint)status.Action);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(60), (uint)status.ActionResult);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(64), status.Ped);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(68), status.Build);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(72), status.Error);
        text.CopyTo(payload.AsSpan(80));
        return payload;
    }

    public static void ValidateResponse(ClientModuleStatus status, ulong highestRevision, ulong highestNonce, ClientModuleAction lastAction)
    {
        ValidateStatus(status);
        if (status.DesiredRevision > highestRevision || status.ActionNonce > highestNonce ||
            status.ActionNonce != 0 && status.ActionNonce == highestNonce && status.Action != lastAction)
            throw new InvalidDataException("O módulo respondeu com revisão ou ação não solicitada nesta conexão.");
    }

    public static bool IsReadyFresh(ClientModuleStatus status, long now)
    {
        if (now < 0 || status.NativeTick == 0 || status.NativeTick > (ulong)now) return false;
        return status.Build == 3258 && status.Ped != 0 && status.StateFlags.HasFlag(ClientModuleStateFlags.Ready) &&
            !status.StateFlags.HasFlag(ClientModuleStateFlags.Blocked) &&
            (ulong)now - status.NativeTick <= MaximumNativeFrameAgeMilliseconds;
    }

    public static async Task WriteFrameAsync(Stream stream, ClientModuleKind kind, ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default)
    {
        if (payload.Length != PayloadLength(kind)) throw new InvalidDataException("Payload local fora do comprimento fixo.");
        byte[] frame = new byte[HeaderLength + payload.Length];
        EncodeHeader(kind).CopyTo(frame, 0);
        payload.CopyTo(frame.AsMemory(HeaderLength));
        await stream.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task<ClientModuleStatus> ReadStatusAsync(Stream stream, ReadOnlyMemory<byte> expectedToken,
        CancellationToken cancellationToken = default)
    {
        byte[] headerBytes = new byte[HeaderLength];
        await stream.ReadExactlyAsync(headerBytes, cancellationToken).ConfigureAwait(false);
        var header = DecodeHeader(headerBytes);
        if (header.Kind != ClientModuleKind.Status) throw new InvalidDataException("O módulo local não enviou um estado.");
        byte[] payload = new byte[StatusPayloadLength];
        await stream.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);
        return DecodeStatus(payload, expectedToken.Span);
    }

    private static void ValidateToken(ReadOnlySpan<byte> token)
    {
        if (token.Length != TokenLength || token.IndexOfAnyExcept((byte)0) < 0)
            throw new ArgumentException("A conexão local exige um token aleatório de 16 bytes.", nameof(token));
    }

    private static void ValidateStatus(ClientModuleStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        if (((uint)status.Capabilities & ~511u) != 0 || ((uint)status.StateFlags & ~15u) != 0 ||
            status.DesiredRevision > long.MaxValue || status.AppliedRevision > status.DesiredRevision || status.ActionNonce > long.MaxValue ||
            status.NativeTick > long.MaxValue || status.Build is not (0 or 3258) ||
            status.Error is not (0 or 10 or 11 or 12 or 13 or 14 or 15 or 1001 or 1002 or 1003 or 1004 or 1005) ||
            status.Action is not (ClientModuleAction.None or ClientModuleAction.ReviveNative or ClientModuleAction.Suicide or ClientModuleAction.LockVehicle or ClientModuleAction.UnlockVehicle) ||
            status.ActionResult is not (ClientModuleActionResult.None or ClientModuleActionResult.Pending or ClientModuleActionResult.Observed or ClientModuleActionResult.Failed))
            throw new InvalidDataException("Campos do estado local fora do contrato.");
        bool noAction = status.ActionNonce == 0;
        if (noAction != (status.Action == ClientModuleAction.None) || noAction != (status.ActionResult == ClientModuleActionResult.None) ||
            status.StateFlags.HasFlag(ClientModuleStateFlags.SettingsActive) && status.AppliedRevision == 0 ||
            status.StateFlags.HasFlag(ClientModuleStateFlags.PedAlive) && status.Ped == 0 ||
            status.StateFlags.HasFlag(ClientModuleStateFlags.Ready) &&
            (status.Ped == 0 || status.Build != 3258 || status.NativeTick == 0 || status.StateFlags.HasFlag(ClientModuleStateFlags.Blocked)))
            throw new InvalidDataException("O estado local contém campos contraditórios.");
    }
}
