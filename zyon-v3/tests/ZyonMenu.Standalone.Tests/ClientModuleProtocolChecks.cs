using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using ZyonMenu.Core;

namespace ZyonMenu.Standalone.Tests;

internal static class ClientModuleProtocolChecks
{
    public static int Run() => RunAsync().GetAwaiter().GetResult();

    private static async Task<int> RunAsync()
    {
        var checks = new Checks();
        CheckCodec(checks);
        await CheckPipeExchange(checks);
        await CheckBrokenResponses(checks);
        await CheckReadCancellation(checks);
        return checks.Count;
    }

    private static void CheckCodec(Checks checks)
    {
        byte[] token = Token();
        checks.That(ClientModuleProtocol.HeaderLength == 16 && ClientModuleProtocol.TokenLength == 16 &&
            ClientModuleProtocol.StatusPayloadLength == 240, "Managed protocol sizes must match the packed native ABI.");
        foreach (var (kind, length) in new[]
        {
            (ClientModuleKind.Hello, 16), (ClientModuleKind.Settings, 48), (ClientModuleKind.Action, 32),
            (ClientModuleKind.Status, 240), (ClientModuleKind.Disconnect, 16), (ClientModuleKind.Poll, 16)
        })
        {
            byte[] expected = Header(kind, length);
            checks.That(ClientModuleProtocol.EncodeHeader(kind).SequenceEqual(expected),
                $"{kind} headers must use the native little-endian layout and exact payload length.");
            var decoded = ClientModuleProtocol.DecodeHeader(expected);
            checks.That(decoded.Kind == kind && decoded.PayloadLength == length,
                $"The independent native {kind} header must decode correctly.");
        }
        foreach (int length in new[] { 0, 1, 15, 17, 32 })
            checks.Reject(() => ClientModuleProtocol.DecodeHeader(new byte[length]),
                "Truncated or oversized headers must be rejected.");
        foreach (int offset in new[] { 0, 4, 6, 8, 12 })
        {
            byte[] malformed = Header(ClientModuleKind.Status, 240);
            malformed[offset] ^= 0x80;
            checks.Reject(() => ClientModuleProtocol.DecodeHeader(malformed),
                "Invalid magic, version, kind, payload size and reserved header fields must be rejected.");
        }
        foreach (ushort kind in new ushort[] { 0, 7, ushort.MaxValue })
        {
            byte[] malformed = Header(ClientModuleKind.Status, 240);
            BinaryPrimitives.WriteUInt16LittleEndian(malformed.AsSpan(6), kind);
            checks.Reject(() => ClientModuleProtocol.DecodeHeader(malformed), "Unknown packet kinds must not be accepted.");
        }
        checks.Reject(() => ClientModuleProtocol.EncodeHeader((ClientModuleKind)7), "The encoder must reject unknown packet kinds.");

        checks.That(ClientModuleProtocol.TokenPayload(token).SequenceEqual(token), "Token packets must preserve all sixteen token bytes.");
        foreach (int length in new[] { 0, 15, 17 })
        {
            byte[] invalidToken = new byte[length];
            checks.Reject(() => ClientModuleProtocol.TokenPayload(invalidToken), "Local token payloads require exactly sixteen bytes.");
            checks.Reject(() => ClientModuleProtocol.DecodeStatus(StatusPayload(token), invalidToken),
                "Status decoding requires an exact-size expected token.");
        }
        checks.Reject(() => ClientModuleProtocol.TokenPayload(new byte[16]), "An all-zero session token must not be accepted.");
        byte[] settings = ClientModuleProtocol.SettingsPayload(token, 0x0102030405060708,
            ClientModuleSettingsFlags.God | ClientModuleSettingsFlags.Noclip | ClientModuleSettingsFlags.Invisible |
            ClientModuleSettingsFlags.FreezeAmmo, 50);
        checks.That(settings.Length == 48 && settings.AsSpan(0, 16).SequenceEqual(token) &&
            BinaryPrimitives.ReadUInt64LittleEndian(settings.AsSpan(16)) == 0x0102030405060708 &&
            BinaryPrimitives.ReadUInt32LittleEndian(settings.AsSpan(24)) == 15 &&
            BinaryPrimitives.ReadUInt32LittleEndian(settings.AsSpan(28)) == 50 &&
            BinaryPrimitives.ReadUInt32LittleEndian(settings.AsSpan(32)) == 100 &&
            BinaryPrimitives.ReadUInt32LittleEndian(settings.AsSpan(36)) == 0 &&
            BinaryPrimitives.ReadUInt32LittleEndian(settings.AsSpan(40)) == 0 &&
            BinaryPrimitives.ReadUInt32LittleEndian(settings.AsSpan(44)) == 3,
            "Settings must preserve the revision, known flags and speed in the native packed layout.");
        checks.Reject(() => ClientModuleProtocol.SettingsPayload(token, 0, ClientModuleSettingsFlags.None, 5),
            "A settings request must have a nonzero revision.");
        checks.Reject(() => ClientModuleProtocol.SettingsPayload(token, ulong.MaxValue, ClientModuleSettingsFlags.None, 5),
            "Settings revisions must not overflow the client's signed revision counter.");
        checks.Reject(() => ClientModuleProtocol.SettingsPayload(token, 1, (ClientModuleSettingsFlags)16, 5),
            "Unknown settings bits must not be sent to the module.");
        foreach (uint speed in new uint[] { 0, 51, uint.MaxValue })
            checks.Reject(() => ClientModuleProtocol.SettingsPayload(token, 1, ClientModuleSettingsFlags.None, speed),
                "Noclip speed must stay within the supported 1–50 metres-per-second range.");

        byte[] action = ClientModuleProtocol.ActionPayload(token, 0x0102030405060708,
            ClientModuleAction.ReviveNative, 0x12345678);
        checks.That(action.Length == 32 && action.AsSpan(0, 16).SequenceEqual(token) &&
            BinaryPrimitives.ReadUInt64LittleEndian(action.AsSpan(16)) == 0x0102030405060708 &&
            BinaryPrimitives.ReadUInt32LittleEndian(action.AsSpan(24)) == 1 &&
            BinaryPrimitives.ReadUInt32LittleEndian(action.AsSpan(28)) == 0x12345678,
            "Actions must carry their nonce, action and expected ped; the final word is not reserved.");
        checks.Reject(() => ClientModuleProtocol.ActionPayload(token, 0, ClientModuleAction.Suicide, 7),
            "An action must have a nonzero nonce.");
        checks.Reject(() => ClientModuleProtocol.ActionPayload(token, ulong.MaxValue, ClientModuleAction.Suicide, 7),
            "Action nonces must not overflow the client's signed action counter.");
        checks.Reject(() => ClientModuleProtocol.ActionPayload(token, 1, ClientModuleAction.Suicide, 0),
            "An action without its expected ped must never be sent.");
        foreach (var invalid in new[] { ClientModuleAction.None, (ClientModuleAction)5, (ClientModuleAction)uint.MaxValue })
            checks.Reject(() => ClientModuleProtocol.ActionPayload(token, 1, invalid, 7), "Only supported one-shot actions may be encoded.");
        foreach (var vehicle in new[] { ClientModuleAction.LockVehicle, ClientModuleAction.UnlockVehicle })
        {
            byte[] encoded = ClientModuleProtocol.ActionPayload(token, 2, vehicle, 7);
            checks.That(BinaryPrimitives.ReadUInt32LittleEndian(encoded.AsSpan(24)) == (uint)vehicle &&
                BinaryPrimitives.ReadUInt32LittleEndian(encoded.AsSpan(28)) == 7,
                "Vehicle actions retain their expected local ped and explicit action id.");
        }
        var experimental = new AimSettings { Enabled = true, Fov = 222, TargetBone = "Chest", ActivationKey = "Ctrl", OnlyVisible = false };
        byte[] directed = ClientModuleProtocol.SettingsPayload(token, 3, ClientModuleSettingsFlags.DirectedFireExperimental, 5, experimental);
        checks.That(BinaryPrimitives.ReadUInt32LittleEndian(directed.AsSpan(24)) == 256 &&
            BinaryPrimitives.ReadUInt32LittleEndian(directed.AsSpan(32)) == 222 &&
            BinaryPrimitives.ReadUInt32LittleEndian(directed.AsSpan(36)) == 2 &&
            BinaryPrimitives.ReadUInt32LittleEndian(directed.AsSpan(40)) == 4 &&
            BinaryPrimitives.ReadUInt32LittleEndian(directed.AsSpan(44)) == 1,
            "Experimental fire uses bounded primitive fields, never native handles or addresses from the UI.");
        foreach (var invalid in new[] { new AimSettings { OnlyPlayers = false }, new AimSettings { DrawFov = true }, new AimSettings { Smooth = 2 } })
            checks.Reject(() => ClientModuleProtocol.SettingsPayload(token, 1, ClientModuleSettingsFlags.DirectedFireExperimental, 5, invalid),
                "Unsupported experimental features cannot silently change the requested behavior.");
        var experimentalMenu = new MenuSettings();
        experimentalMenu.Silent.Enabled = true;
        checks.Reject(() => experimentalMenu.Validate(), "External and server integrations continue rejecting experimental fire.");
        experimentalMenu.Validate(allowExperimentalSilent: true);
        checks.That(experimentalMenu.Silent.Enabled, "Only the explicit local module validation accepts experimental fire.");

        byte[] payload = StatusPayload(token);
        var status = ClientModuleProtocol.DecodeStatus(payload, token);
        checks.That(status.NativeTick == 10000 && status.DesiredRevision == 7 && status.AppliedRevision == 7 &&
            status.ActionNonce == 0 && (uint)status.Capabilities == 63 && (uint)status.StateFlags == 5 &&
            status.Action == ClientModuleAction.None && status.ActionResult == ClientModuleActionResult.None &&
            status.Ped == 0x1234 && status.Build == 3258 && status.Error == 0 && status.Message == "native ready",
            "The independent 240-byte native fixture must decode all status fields at their documented offsets.");
        checks.That(ClientModuleProtocol.EncodeStatus(token, status).SequenceEqual(payload),
            "The status encoder must match the independent native fixture, including reserved bytes and message padding.");
        foreach (int length in new[] { 0, 16, 79, 239, 241 })
            checks.Reject(() => ClientModuleProtocol.DecodeStatus(new byte[length], token), "Malformed status sizes must not be decoded.");
        for (int index = 0; index < token.Length; index++)
        {
            byte[] foreign = (byte[])payload.Clone();
            foreign[index] ^= 0x01;
            checks.Reject(() => ClientModuleProtocol.DecodeStatus(foreign, token), "Every token byte must participate in status authentication.");
        }
        foreach (int offset in new[] { 48, 52, 56, 60, 72, 76 })
        {
            byte[] malformed = (byte[])payload.Clone();
            BinaryPrimitives.WriteUInt32LittleEndian(malformed.AsSpan(offset), uint.MaxValue);
            checks.Reject(() => ClientModuleProtocol.DecodeStatus(malformed, token),
                "Unknown capability/state/action/result/error values and nonzero reserved data must be rejected.");
        }
        byte[] badRevision = (byte[])payload.Clone();
        BinaryPrimitives.WriteUInt64LittleEndian(badRevision.AsSpan(32), 8);
        checks.Reject(() => ClientModuleProtocol.DecodeStatus(badRevision, token), "Applied settings cannot exceed the desired revision.");
        foreach (int offset in new[] { 16, 24, 40 })
        {
            byte[] overflow = (byte[])payload.Clone();
            BinaryPrimitives.WriteUInt64LittleEndian(overflow.AsSpan(offset), ulong.MaxValue);
            checks.Reject(() => ClientModuleProtocol.DecodeStatus(overflow, token),
                "Native ticks, revisions and nonces must fit the client's signed counters.");
        }
        foreach (var (offset, value) in new[] { (56, 1u), (60, 1u), (64, 0u), (68, 3095u) })
        {
            byte[] contradiction = (byte[])payload.Clone();
            BinaryPrimitives.WriteUInt32LittleEndian(contradiction.AsSpan(offset), value);
            checks.Reject(() => ClientModuleProtocol.DecodeStatus(contradiction, token),
                "Action identity, live-ped state and build must be internally consistent.");
        }
        byte[] nonceWithoutAction = (byte[])payload.Clone();
        BinaryPrimitives.WriteUInt64LittleEndian(nonceWithoutAction.AsSpan(40), 9);
        checks.Reject(() => ClientModuleProtocol.DecodeStatus(nonceWithoutAction, token),
            "A nonzero action nonce cannot be paired with an absent action and result.");
        byte[] readyWithoutTick = (byte[])payload.Clone();
        BinaryPrimitives.WriteUInt64LittleEndian(readyWithoutTick.AsSpan(16), 0);
        checks.Reject(() => ClientModuleProtocol.DecodeStatus(readyWithoutTick, token), "A Ready state must come from a completed native tick.");
        byte[] blockedReady = (byte[])payload.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(blockedReady.AsSpan(52), 13);
        checks.Reject(() => ClientModuleProtocol.DecodeStatus(blockedReady, token), "A blocked module cannot simultaneously claim Ready.");
        byte[] activeWithoutRevision = StatusPayload(token, revision: 0);
        BinaryPrimitives.WriteUInt32LittleEndian(activeWithoutRevision.AsSpan(52), 7);
        checks.Reject(() => ClientModuleProtocol.DecodeStatus(activeWithoutRevision, token),
            "Active settings require a nonzero applied revision.");
        byte[] badUtf8 = (byte[])payload.Clone();
        Array.Clear(badUtf8, 80, 160);
        badUtf8[80] = 0xC3;
        badUtf8[81] = 0x28;
        checks.Reject(() => ClientModuleProtocol.DecodeStatus(badUtf8, token), "Invalid UTF-8 must not become a misleading status message.");
        byte[] noTerminator = (byte[])payload.Clone();
        Array.Fill(noTerminator, (byte)'x', 80, 160);
        checks.Reject(() => ClientModuleProtocol.DecodeStatus(noTerminator, token), "The fixed-size message must contain a NUL terminator.");
        byte[] hiddenText = (byte[])payload.Clone();
        hiddenText[239] = (byte)'x';
        checks.Reject(() => ClientModuleProtocol.DecodeStatus(hiddenText, token), "Message bytes after the NUL must be zero padding.");
        byte[] unicode = (byte[])payload.Clone();
        Array.Clear(unicode, 80, 160);
        Encoding.UTF8.GetBytes("ação confirmada").CopyTo(unicode, 80);
        checks.That(ClientModuleProtocol.DecodeStatus(unicode, token).Message == "ação confirmada", "Valid UTF-8 messages must preserve Portuguese text.");
        foreach (char control in new[] { '\n', '\r', '\t', '\u001B' })
        {
            byte[] controlText = (byte[])payload.Clone();
            controlText[80] = (byte)control;
            checks.Reject(() => ClientModuleProtocol.DecodeStatus(controlText, token), "Control characters must not enter the status UI through native messages.");
        }
        checks.Reject(() => ClientModuleProtocol.EncodeStatus(token, status with { Message = new string('x', 160) }),
            "Encoded messages must leave one byte for the NUL terminator.");

        ClientModuleProtocol.ValidateResponse(status, 7, 0, ClientModuleAction.None);
        checks.That(true, "The native fixture is valid for the sent revision and nonce bounds.");
        checks.Reject(() => ClientModuleProtocol.ValidateResponse(status, 6, 0, ClientModuleAction.None),
            "A status cannot acknowledge a settings revision the client never sent.");
        var observed = status with { ActionNonce = 9, Action = ClientModuleAction.ReviveNative, ActionResult = ClientModuleActionResult.Observed };
        ClientModuleProtocol.ValidateResponse(observed, 7, 9, ClientModuleAction.ReviveNative);
        checks.That(true, "A matching one-shot observation is valid for its sent nonce.");
        checks.Reject(() => ClientModuleProtocol.ValidateResponse(observed, 7, 8, ClientModuleAction.ReviveNative),
            "A foreign or future action nonce must not be acknowledged.");
        checks.Reject(() => ClientModuleProtocol.ValidateResponse(observed, 7, 9, ClientModuleAction.Suicide),
            "The same nonce must not acknowledge a different action.");
        ClientModuleProtocol.ValidateResponse(observed, 7, 10, ClientModuleAction.Suicide);
        checks.That(true, "A historical action result may be reported while a later nonce is pending; it must retain its original identity.");

        checks.That(ClientModuleProtocol.IsReadyFresh(status, 10000) && ClientModuleProtocol.IsReadyFresh(status, 11000),
            "Ready snapshots remain fresh through the documented one-second boundary.");
        checks.That(!ClientModuleProtocol.IsReadyFresh(status, 11001) && !ClientModuleProtocol.IsReadyFresh(status, 9999) &&
            !ClientModuleProtocol.IsReadyFresh(status, -1),
            "Expired or future native ticks must not make a module ready.");
        foreach (var invalid in new[]
        {
            status with { NativeTick = 0 }, status with { Ped = 0 }, status with { Build = 3095 },
            status with { StateFlags = ClientModuleStateFlags.None },
            status with { StateFlags = status.StateFlags | ClientModuleStateFlags.Blocked }
        })
            checks.That(!ClientModuleProtocol.IsReadyFresh(invalid, 10000), "Readiness needs a current native tick, b3258, a ped and unblocked Ready state.");
    }

    private static async Task CheckPipeExchange(Checks checks)
    {
        byte[] token = Token();
        await PipeScenario(async (server, cancellationToken) =>
        {
            byte[] hello = await ReadRequest(server, ClientModuleKind.Hello, cancellationToken);
            checks.That(hello.SequenceEqual(token), "Loopback Hello must carry the current session token.");
            await SendStatus(server, StatusPayload(token, revision: 0), cancellationToken, fragmented: true);

            byte[] settings = await ReadRequest(server, ClientModuleKind.Settings, cancellationToken);
            checks.That(settings.AsSpan(0, 16).SequenceEqual(token) &&
                BinaryPrimitives.ReadUInt64LittleEndian(settings.AsSpan(16)) == 7 &&
                BinaryPrimitives.ReadUInt32LittleEndian(settings.AsSpan(24)) == 3 &&
                BinaryPrimitives.ReadUInt32LittleEndian(settings.AsSpan(28)) == 12,
                "Loopback settings must retain their authenticated revision, flags and speed.");
            await SendStatus(server, StatusPayload(token), cancellationToken);

            byte[] action = await ReadRequest(server, ClientModuleKind.Action, cancellationToken);
            checks.That(BinaryPrimitives.ReadUInt64LittleEndian(action.AsSpan(16)) == 9 &&
                BinaryPrimitives.ReadUInt32LittleEndian(action.AsSpan(24)) == 1 &&
                BinaryPrimitives.ReadUInt32LittleEndian(action.AsSpan(28)) == 0x1234,
                "The mock module must receive the expected ped with the one-shot action nonce.");
            await SendStatus(server, StatusPayload(token, nonce: 9, action: 1, result: 1), cancellationToken);
            byte[] poll = await ReadRequest(server, ClientModuleKind.Poll, cancellationToken);
            checks.That(poll.SequenceEqual(token), "Polling a pending action must retain the session token.");
            await SendStatus(server, StatusPayload(token, nonce: 9, action: 1, result: 2), cancellationToken, fragmented: true);
            byte[] disconnect = await ReadRequest(server, ClientModuleKind.Disconnect, cancellationToken);
            checks.That(disconnect.SequenceEqual(token), "A clean disconnect must be authenticated.");
        }, async (client, cancellationToken) =>
        {
            await ClientModuleProtocol.WriteFrameAsync(client, ClientModuleKind.Hello,
                ClientModuleProtocol.TokenPayload(token), cancellationToken);
            var hello = await ClientModuleProtocol.ReadStatusAsync(client, token, cancellationToken);
            checks.That(hello.DesiredRevision == 0 && hello.AppliedRevision == 0, "Fragmented Hello status must be read completely.");
            await ClientModuleProtocol.WriteFrameAsync(client, ClientModuleKind.Settings,
                ClientModuleProtocol.SettingsPayload(token, 7, ClientModuleSettingsFlags.God | ClientModuleSettingsFlags.Noclip, 12), cancellationToken);
            var applied = await ClientModuleProtocol.ReadStatusAsync(client, token, cancellationToken);
            ClientModuleProtocol.ValidateResponse(applied, 7, 0, ClientModuleAction.None);
            checks.That(applied.AppliedRevision == 7, "A settings response must preserve the actual applied revision.");
            await ClientModuleProtocol.WriteFrameAsync(client, ClientModuleKind.Action,
                ClientModuleProtocol.ActionPayload(token, 9, ClientModuleAction.ReviveNative, 0x1234), cancellationToken);
            var pending = await ClientModuleProtocol.ReadStatusAsync(client, token, cancellationToken);
            checks.That(pending.ActionResult == ClientModuleActionResult.Pending, "A pending receipt must remain distinct from an observed native action.");
            await ClientModuleProtocol.WriteFrameAsync(client, ClientModuleKind.Poll, ClientModuleProtocol.TokenPayload(token), cancellationToken);
            var observed = await ClientModuleProtocol.ReadStatusAsync(client, token, cancellationToken);
            ClientModuleProtocol.ValidateResponse(observed, 7, 9, ClientModuleAction.ReviveNative);
            checks.That(observed.ActionNonce == 9 && observed.ActionResult == ClientModuleActionResult.Observed,
                "The eventual native observation must retain the exact action nonce.");
            await ClientModuleProtocol.WriteFrameAsync(client, ClientModuleKind.Disconnect,
                ClientModuleProtocol.TokenPayload(token), cancellationToken);
        });
    }

    private static async Task CheckBrokenResponses(Checks checks)
    {
        byte[] token = Token();
        var responses = new List<byte[]>
        {
            Header(ClientModuleKind.Status, 240)[..7],
            Header(ClientModuleKind.Status, 240).Concat(StatusPayload(token)[..239]).ToArray(),
            Header(ClientModuleKind.Poll, 16).Concat(token).ToArray()
        };
        byte[] foreign = StatusPayload(token);
        foreign[15] ^= 1;
        responses.Add(Header(ClientModuleKind.Status, 240).Concat(foreign).ToArray());
        byte[] oversizedHeader = Header(ClientModuleKind.Status, int.MaxValue);
        responses.Add(oversizedHeader);
        foreach (byte[] response in responses)
        {
            await PipeScenario(async (server, cancellationToken) =>
            {
                await server.WriteAsync(response, cancellationToken);
                await server.FlushAsync(cancellationToken);
            }, async (client, cancellationToken) =>
            {
                await checks.RejectAsync(async () => { _ = await ClientModuleProtocol.ReadStatusAsync(client, token, cancellationToken); },
                    "Partial, foreign, incorrectly typed or oversized pipe responses must fail without an accepted status.");
            });
        }
    }

    private static async Task CheckReadCancellation(Checks checks)
    {
        byte[] token = Token();
        await PipeScenario(async (_, cancellationToken) =>
        {
            try { await Task.Delay(Timeout.Infinite, cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        }, async (client, _) =>
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
            var timer = Stopwatch.StartNew();
            try
            {
                await ClientModuleProtocol.ReadStatusAsync(client, token, timeout.Token);
                throw new InvalidOperationException("A silent module must not produce an accepted status.");
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                checks.That(timer.Elapsed < TimeSpan.FromSeconds(1), "A silent module must respect the caller deadline instead of hanging the UI.");
            }
        }, serverWaitsForCancellation: true);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        using var output = new MemoryStream();
        try
        {
            await ClientModuleProtocol.WriteFrameAsync(output, ClientModuleKind.Action,
                ClientModuleProtocol.ActionPayload(token, 1, ClientModuleAction.Suicide, 7), cancelled.Token);
            throw new InvalidOperationException("A pre-cancelled action frame must not be written.");
        }
        catch (OperationCanceledException)
        {
            checks.That(output.Length == 0, "An action cancelled before transmission must leave the stream untouched.");
        }
    }

    private static async Task PipeScenario(Func<NamedPipeServerStream, CancellationToken, Task> serve,
        Func<NamedPipeClientStream, CancellationToken, Task> use, bool serverWaitsForCancellation = false)
    {
        string name = "ZyonProtocolChecks-" + Guid.NewGuid().ToString("N");
        using var deadline = new CancellationTokenSource(TimeSpan.FromMilliseconds(1500));
        using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        Task serverTask = Serve();
        try
        {
            await client.ConnectAsync(deadline.Token);
            await use(client, deadline.Token);
            if (serverWaitsForCancellation) deadline.Cancel();
            await serverTask;
        }
        finally
        {
            deadline.Cancel();
            server.Dispose();
            client.Dispose();
            try { await serverTask; }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested) { }
        }
        async Task Serve()
        {
            await server.WaitForConnectionAsync(deadline.Token);
            await serve(server, deadline.Token);
            server.Dispose(); // Deliver EOF so a truncated response cannot hide behind an open server.
        }
    }

    private static async Task<byte[]> ReadRequest(Stream stream, ClientModuleKind expectedKind, CancellationToken cancellationToken)
    {
        byte[] header = new byte[16];
        await stream.ReadExactlyAsync(header, cancellationToken);
        int length = expectedKind == ClientModuleKind.Settings ? 48 : expectedKind == ClientModuleKind.Action ? 32 : 16;
        if (!header.SequenceEqual(Header(expectedKind, length)))
            throw new InvalidOperationException("The loopback module received an invalid request header.");
        byte[] payload = new byte[length];
        await stream.ReadExactlyAsync(payload, cancellationToken);
        return payload;
    }

    private static async Task SendStatus(Stream stream, byte[] payload, CancellationToken cancellationToken, bool fragmented = false)
    {
        byte[] frame = Header(ClientModuleKind.Status, 240).Concat(payload).ToArray();
        if (fragmented)
        {
            await stream.WriteAsync(frame.AsMemory(0, 7), cancellationToken);
            await stream.FlushAsync(cancellationToken);
            await Task.Delay(5, cancellationToken);
            await stream.WriteAsync(frame.AsMemory(7, 34), cancellationToken);
            await stream.FlushAsync(cancellationToken);
            await Task.Delay(5, cancellationToken);
            await stream.WriteAsync(frame.AsMemory(41), cancellationToken);
        }
        else await stream.WriteAsync(frame, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static byte[] Header(ClientModuleKind kind, int payloadLength)
    {
        byte[] bytes = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 0x4E4F595A);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(6), (ushort)kind);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), checked((uint)payloadLength));
        return bytes;
    }

    private static byte[] StatusPayload(byte[] token, ulong revision = 7, ulong nonce = 0, uint action = 0, uint result = 0)
    {
        byte[] bytes = new byte[240];
        token.CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(16), 10000);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(24), revision);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(32), revision);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(40), nonce);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(48), 63);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(52), 5);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(56), action);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(60), result);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(64), 0x1234);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(68), 3258);
        Encoding.UTF8.GetBytes("native ready").CopyTo(bytes, 80);
        return bytes;
    }

    private static byte[] Token() => Enumerable.Range(1, 16).Select(value => (byte)value).ToArray();

    private sealed class Checks
    {
        private int _count;
        public int Count => Volatile.Read(ref _count);
        public void That(bool condition, string message)
        {
            Interlocked.Increment(ref _count);
            if (!condition) throw new InvalidOperationException(message);
        }
        public void Reject(Action action, string message)
        {
            Interlocked.Increment(ref _count);
            try { action(); }
            catch (Exception exception) when (exception is InvalidDataException or ArgumentException) { return; }
            throw new InvalidOperationException(message);
        }
        public async Task RejectAsync(Func<Task> action, string message)
        {
            Interlocked.Increment(ref _count);
            try { await action(); }
            catch (Exception exception) when (exception is InvalidDataException or IOException or ArgumentException) { return; }
            throw new InvalidOperationException(message);
        }
    }
}
