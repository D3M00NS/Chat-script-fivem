// C# / injectable local provider / real named pipes and a fake memory provider; no FiveM execution.
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text.Json;
using ZyonMenu.Core;
using ZyonMenu.Windows;

namespace ZyonMenu.Standalone.Tests;

internal static class ClientModuleSessionChecks
{
    public static int Run() => RunAsync().GetAwaiter().GetResult();

    private static async Task<int> RunAsync()
    {
        var checks = new Checks();
        Func<Checks, Task>[] scenarios = [SeparationAsync, PendingAcknowledgementAsync, NativeOnlyAndStalenessAsync,
            AuthorizationLossAsync, TargetEpochAsync, SettingsActionFenceAsync, NonceAndObservationAsync, MalformedPollRecoveryAsync,
            TimeoutNoReplayAsync, ExperimentalDirectedFireSettingsAsync, VehicleActionsAsync, VehicleCapabilitiesAsync, VehicleTimeoutNoReplayAsync,
            VehicleTargetAndCancellationAsync, DisconnectAndDisposeAsync];
        foreach (var scenario in scenarios)
        {
            // Bound the invocation as well as its awaits: an accidentally held
            // Monitor must fail this scenario without blocking the test runner.
            try { await Task.Run(() => scenario(checks)).WaitAsync(TimeSpan.FromSeconds(15)); }
            catch (TimeoutException exception)
            { throw new InvalidOperationException("Provider scenario exceeded its bounded deadline: " + scenario.Method.Name, exception); }
        }
        return checks.Count;
    }

    private static async Task SeparationAsync(Checks checks)
    {
        var external = new MemoryProvider { Ready = true, ProcessId = 101 };
        await using var pipe = new ModulePipe();
        using var provider = new ClientModuleSession(external, () => 101, pipe.ConnectAsync);
        await ReadyAsync(provider);
        var settings = Settings();
        settings.Player.GodMode = settings.Player.Noclip = settings.Player.Invisible = true;
        settings.Weapons.FreezeAmmo = settings.Weapons.NoRecoil = settings.Weapons.NoSpread = true;
        settings.Player.NoclipSpeed = 17;
        settings.Aim.Enabled = settings.Esp.Enabled = true;
        await provider.PushSettingsAsync(settings);
        // Mutating the caller afterwards must not change either owned snapshot.
        settings.Player.NoclipSpeed = 44;
        settings.Player.Invisible = false;
        var shadow = external.Pushed.Single();
        checks.That(!shadow.Player.GodMode && !shadow.Player.Noclip && !shadow.Player.Invisible && !shadow.Weapons.FreezeAmmo,
            "Native-owned flags must never be applied by the memory provider.");
        checks.That(shadow.Aim.Enabled && shadow.Esp.Enabled && shadow.Weapons.NoRecoil && shadow.Weapons.NoSpread,
            "Memory Aim/ESP/recoil/spread must remain owned by the memory provider.");
        var request = pipe.Requests.Single(item => item.Kind == ClientModuleKind.Settings);
        checks.That(BinaryPrimitives.ReadUInt32LittleEndian(request.Payload.AsSpan(24)) == 15 &&
            BinaryPrimitives.ReadUInt32LittleEndian(request.Payload.AsSpan(28)) == 17,
            "The native request must contain only native flags and the captured noclip speed.");
        var status = await provider.GetStatusAsync();
        checks.That(status.AppliedRevision == status.DesiredRevision && status.SessionActive,
            "A combined configuration may report applied only after both providers observe their corresponding revision.");
        checks.That(status.Capabilities.Contains("aim.enabled") && status.Capabilities.Contains("player.noclip") &&
            status.FeatureStates.Single(item => item.Name == "player.godMode").State == "observed",
            "Combined status must preserve memory capabilities and use native observations for native features.");
        checks.That(external.Revives == 0 && external.Suicides == 0, "Health actions must not be delegated to the memory backend.");
        using var diagnostic = JsonDocument.Parse(provider.CaptureDiagnostics());
        checks.That(diagnostic.RootElement.GetProperty("Configuration").GetProperty("Player").GetProperty("NoclipSpeed").GetInt32() == 17,
            "Diagnostics must own a snapshot rather than retain mutable UI settings.");
        checks.That(diagnostic.RootElement.GetProperty("External").GetProperty("MemoryProvider").GetBoolean(),
            "Combined diagnostics must retain the external provider payload separately.");

        external.AppliedOffset = -1;
        var memoryPending = Settings(); memoryPending.Weapons.NoRecoil = true;
        await provider.PushSettingsAsync(memoryPending);
        status = await provider.GetStatusAsync();
        checks.That(status.SessionActive && status.AppliedRevision == 0,
            "An unapplied external revision must remain unconfirmed without erasing the live session.");
        checks.That(external.Pushed.Count == 2, "An unapplied memory revision must not be silently retried.");
        pipe.ThrowIfFaulted();
    }

    private static async Task PendingAcknowledgementAsync(Checks checks)
    {
        var external = new MemoryProvider { Ready = true, ProcessId = 101 };
        await using var pipe = new ModulePipe();
        using var provider = new ClientModuleSession(external, () => 101, pipe.ConnectAsync);
        await ReadyAsync(provider);
        var settings = Settings(); settings.Player.Noclip = true;
        await provider.PushSettingsAsync(settings);
        var lifecycle = new StandaloneUiPolicy();
        var status = await provider.GetStatusAsync();
        checks.That(status.SessionActive && !lifecycle.Observe(true, status.Ready, status.PlayerOnline, status.SessionActive, status.PlayerId),
            "An applied native configuration must establish the live UI session.");

        // The external reader is optional when none of its functions was requested.
        external.Ready = false;
        status = await provider.GetStatusAsync();
        checks.That(status.SessionActive && status.AppliedRevision == status.DesiredRevision &&
            !lifecycle.Observe(true, status.Ready, status.PlayerOnline, status.SessionActive, status.PlayerId),
            "Losing unused memory reads must not reset a native-only configuration.");

        pipe.AutoApplySettings = false;
        settings.Player.NoclipSpeed = 23;
        await provider.PushSettingsAsync(settings);
        status = await provider.GetStatusAsync();
        checks.That(status.SessionActive && status.AppliedRevision == 0 &&
            !lifecycle.Observe(true, status.Ready, status.PlayerOnline, status.SessionActive, status.PlayerId),
            "Changing noclip speed before its native frame ACK must preserve the live UI choices.");
        checks.That(pipe.Connections == 1, "Waiting for a native frame ACK must not replace the connection.");

        pipe.AutoApplySettings = true;
        await EventuallyAsync(async () => (await provider.GetStatusAsync()).AppliedRevision == 2,
            "The pending configuration was not observed after native processing resumed.");
        pipe.SettingsActive = false;
        await EventuallyAsync(async () => (await provider.GetStatusAsync()).AppliedRevision == 0,
            "The waiting-flight frame was not surfaced.");
        status = await provider.GetStatusAsync();
        checks.That(status.Ready && status.SessionActive &&
            !lifecycle.Observe(true, status.Ready, status.PlayerOnline, status.SessionActive, status.PlayerId),
            "A fresh waiting-flight frame without SettingsActive must not clear toggles or tear down the native lease.");
        checks.That(pipe.Connections == 1 && pipe.Count(ClientModuleKind.Settings) == 2,
            "Waiting-flight must not reconnect or replay the latest settings.");
        pipe.SettingsActive = true;
        await EventuallyAsync(async () => (await provider.GetStatusAsync()).AppliedRevision == 2,
            "Native processing did not resume after waiting-flight.");
        checks.That(pipe.Connections == 1, "Resuming a paused native frame must retain the authenticated connection.");
        pipe.ThrowIfFaulted();
    }

    private static async Task NativeOnlyAndStalenessAsync(Checks checks)
    {
        var external = new MemoryProvider { Ready = false };
        await using var pipe = new ModulePipe();
        using var provider = new ClientModuleSession(external, () => 101, pipe.ConnectAsync);
        await ReadyAsync(provider);
        var settings = Settings(); settings.Player.Noclip = true;
        await provider.PushSettingsAsync(settings);
        var status = await provider.GetStatusAsync();
        checks.That(status.Ready && status.PlayerId == 101 && status.Capabilities.Contains("player.noclip") &&
            !status.Capabilities.Contains("aim.enabled"),
            "A native-only session must expose native capabilities without inventing memory readiness.");
        checks.That(external.Pushed.Count == 0, "An unavailable external provider must not receive native-only settings.");
        settings.Aim.Enabled = true;
        await checks.ThrowsAsync<InvalidOperationException>(() => provider.PushSettingsAsync(settings),
            "Memory features must be rejected when only the native module is ready.");
        checks.That(pipe.Count(ClientModuleKind.Settings) == 1, "Rejected mixed settings must not reach the native module.");

        long beforeStale = provider.PlayerEpoch;
        pipe.StaleFrames = true;
        await EventuallyAsync(async () => !(await provider.GetStatusAsync()).Ready, "Native status did not become stale.");
        status = await provider.GetStatusAsync();
        checks.That(!status.Capabilities.Contains("player.revive") && !status.Capabilities.Contains("player.noclip") &&
            !status.SessionActive && status.AppliedRevision == 0,
            "Stale native frames must remove capabilities and applied/session claims.");
        await checks.ThrowsAsync<InvalidOperationException>(() => provider.ReviveAsync(beforeStale),
            "An action must never be sent against an expired native frame.");
        checks.That(pipe.Count(ClientModuleKind.Action) == 0, "An expired-frame action must have no wire side effect.");
        pipe.StaleFrames = false;
        await ReadyAsync(provider);
        checks.That(provider.PlayerEpoch != beforeStale, "Losing and recovering native readiness must invalidate saved UI action targets.");
        await checks.ThrowsAsync<InvalidOperationException>(() => provider.ReviveAsync(beforeStale),
            "Recovering a fresh frame must not make a stale action target valid again.");
        pipe.ThrowIfFaulted();
    }

    private static async Task TargetEpochAsync(Checks checks)
    {
        var external = new MemoryProvider { Ready = true, ProcessId = 101 };
        await using var pipe = new ModulePipe();
        int discoveredPid = 101;
        using var provider = new ClientModuleSession(external, () => Volatile.Read(ref discoveredPid), pipe.ConnectAsync);
        await ReadyAsync(provider);
        long oldPed = provider.PlayerEpoch;
        pipe.Ped = 0xCAFE;
        await EventuallyAsync(() => Task.FromResult(provider.PlayerEpoch != oldPed), "Replacing the native ped did not change its epoch.");
        await checks.ThrowsAsync<InvalidOperationException>(() => provider.ReviveAsync(oldPed),
            "A queued revive for a replaced ped must be rejected before transmission.");
        checks.That(pipe.Count(ClientModuleKind.Action) == 0, "A replaced-ped revive must not reach the module.");
        await ReadyAsync(provider);
        long oldPid = provider.PlayerEpoch;
        external.ProcessId = 202;
        var status = await provider.GetStatusAsync();
        checks.That(!status.Capabilities.Contains("player.revive") && status.Capabilities.Contains("aim.enabled"),
            "Different external and native PIDs must suppress native capabilities without mixing player identities.");
        await checks.ThrowsAsync<InvalidOperationException>(() => provider.ReviveAsync(oldPid),
            "A PID mismatch must reject native actions even when both independent providers claim readiness.");
        checks.That(pipe.Count(ClientModuleKind.Action) == 0 && external.Revives == 0,
            "A mismatched action must not fall back to a different provider or process.");
        long beforeProcessChange = provider.PlayerEpoch;
        Volatile.Write(ref discoveredPid, 202);
        await ReadyAsync(provider);
        using var diagnostic = JsonDocument.Parse(provider.CaptureDiagnostics());
        checks.That(pipe.Connections == 2 && pipe.ClosedConnections >= 1 &&
            diagnostic.RootElement.GetProperty("Native").GetProperty("ProcessId").GetInt32() == 202,
            "Changing the discovered game PID must close the old lease and authenticate a new connection.");
        await checks.ThrowsAsync<InvalidOperationException>(() => provider.ReviveAsync(beforeProcessChange),
            "A new process must not inherit an action targeted at the previous process.");
        checks.That(pipe.Count(ClientModuleKind.Action) == 0, "A stale process action must remain unsent after reconnection.");
        pipe.ThrowIfFaulted();
    }

    private static async Task AuthorizationLossAsync(Checks checks)
    {
        var external = new MemoryProvider { Ready = false };
        await using var pipe = new ModulePipe { NativeReady = false };
        using var provider = new ClientModuleSession(external, () => 101, pipe.ConnectAsync);
        await EventuallyAsync(() => Task.FromResult(pipe.Count(ClientModuleKind.Hello) == 1), "The initial waiting module did not connect.");
        await Task.Delay(260);
        checks.That(pipe.Connections == 1 && pipe.ClosedConnections == 0 && !(await provider.GetStatusAsync()).Ready,
            "An initial preflight wait must retain its connection without claiming native readiness.");
        pipe.NativeReady = true;
        await ReadyAsync(provider);
        var settings = Settings(); settings.Player.GodMode = true;
        await provider.PushSettingsAsync(settings);
        checks.That((await provider.GetStatusAsync()).FeatureStates.Single(item => item.Name == "player.godMode").State == "observed",
            "The authorization-loss scenario must begin with an observed native GodMode configuration.");
        long previousTarget = provider.PlayerEpoch;

        pipe.NativeReady = false;
        await EventuallyAsync(() => Task.FromResult(pipe.ClosedConnections >= 1), "Losing native authorization did not close the previously ready lease.");
        await EventuallyAsync(() => Task.FromResult(pipe.Connections >= 2), "No fresh lease was opened after native authorization was lost.");
        var status = await provider.GetStatusAsync();
        checks.That(!status.SessionActive && !status.Capabilities.Contains("player.godMode") && status.AppliedRevision == 0,
            "Revoking native authorization must clear the old applied session and native capability claims.");
        checks.That(provider.PlayerEpoch != previousTarget && pipe.Count(ClientModuleKind.Settings) == 1,
            "Authorization loss must invalidate action targets and must never forward the old active settings to a new lease.");
        var helloTokens = pipe.Requests.Where(item => item.Kind == ClientModuleKind.Hello).Select(item => item.Payload).ToArray();
        checks.That(helloTokens.Length == 2 && !helloTokens[0].SequenceEqual(helloTokens[1]),
            "Recovering authorization must use a new authentication token rather than revive the old session.");

        pipe.NativeReady = true;
        await ReadyAsync(provider);
        await Task.Delay(260);
        status = await provider.GetStatusAsync();
        checks.That(!status.SessionActive && pipe.Count(ClientModuleKind.Settings) == 1 && pipe.Count(ClientModuleKind.Action) == 0,
            "A newly authorized native frame must remain unconfigured until a new explicit user request.");
        await provider.PushSettingsAsync(settings);
        var nativeSettings = pipe.Requests.Where(item => item.Kind == ClientModuleKind.Settings).ToArray();
        checks.That(nativeSettings.Length == 2 && nativeSettings[1].Payload.AsSpan(0, ClientModuleProtocol.TokenLength).SequenceEqual(helloTokens[1]) &&
            (await provider.GetStatusAsync()).SessionActive,
            "Only a fresh explicit request may apply native settings on the replacement lease.");
        pipe.ThrowIfFaulted();
    }

    private static async Task SettingsActionFenceAsync(Checks checks)
    {
        var external = new MemoryProvider { Ready = true, ProcessId = 101 };
        await using var pipe = new ModulePipe { AutoObserveActions = false };
        using var provider = new ClientModuleSession(external, () => 101, pipe.ConnectAsync);
        await ReadyAsync(provider);
        external.BlockNextPush();
        var stale = Settings(); stale.Player.GodMode = true;
        Task push = provider.PushSettingsAsync(stale);
        await external.PushEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Task suicide = provider.SuicideAsync(provider.PlayerEpoch);
        await checks.ThrowsAsync<InvalidOperationException>(() => provider.PushSettingsAsync(Settings()),
            "New settings must be refused while a native action owns the mutation fence.");
        external.ReleasePush.TrySetResult();
        await checks.ThrowsAsync<InvalidOperationException>(() => push,
            "An old settings request queued before an action must not later re-enable native GodMode.");
        await EventuallyAsync(() => Task.FromResult(pipe.Count(ClientModuleKind.Action) == 1), "The queued suicide was not transmitted.");
        await checks.ThrowsAsync<InvalidOperationException>(() => provider.ReviveAsync(provider.PlayerEpoch),
            "Two native actions must not run concurrently.");
        checks.That(pipe.Requests.Where(item => item.Kind == ClientModuleKind.Settings)
            .All(item => (BinaryPrimitives.ReadUInt32LittleEndian(item.Payload.AsSpan(24)) & 3) == 0),
            "Suicide must clear GodMode/noclip and must fence any older enabling settings.");
        var requests = pipe.Requests.Where(item => item.Kind is ClientModuleKind.Settings or ClientModuleKind.Action).ToArray();
        checks.That(requests.Length == 2 && requests[0].Kind == ClientModuleKind.Settings && requests[1].Kind == ClientModuleKind.Action,
            "Suicide must observe its disabling configuration before dispatching exactly one health action.");
        var action = requests[1];
        checks.That(BinaryPrimitives.ReadUInt64LittleEndian(action.Payload.AsSpan(16)) == 1 &&
            BinaryPrimitives.ReadUInt32LittleEndian(action.Payload.AsSpan(24)) == (uint)ClientModuleAction.Suicide &&
            BinaryPrimitives.ReadUInt32LittleEndian(action.Payload.AsSpan(28)) == pipe.Ped,
            "The action must carry a unique nonce, explicit operation and captured ped.");
        checks.That(!suicide.IsCompleted, "A pending action receipt must never be treated as an observed death.");
        pipe.AutoObserveActions = true;
        await suicide.WaitAsync(TimeSpan.FromSeconds(3));
        checks.That(pipe.Count(ClientModuleKind.Action) == 1 && external.Suicides == 0,
            "An observed suicide must issue one native action and no fallback memory health write.");
        checks.That((await provider.GetEventsAsync(0)).Events.Any(item => item.Type == "observed"),
            "Only a matching native observation may produce an observed event.");
        pipe.ThrowIfFaulted();
    }

    private static async Task NonceAndObservationAsync(Checks checks)
    {
        foreach (string corruption in new[] { "nonce", "result", "ped" })
        {
            var external = new MemoryProvider { Ready = false };
            await using var pipe = new ModulePipe();
            pipe.Transform = (request, status) => request.Kind != ClientModuleKind.Action ? status : corruption switch
            {
                "nonce" => status with { ActionNonce = status.ActionNonce + 1 },
                "result" => status with { StateFlags = status.StateFlags & ~ClientModuleStateFlags.PedAlive },
                _ => status with { Ped = status.Ped + 1 }
            };
            using var provider = new ClientModuleSession(external, () => 101, pipe.ConnectAsync);
            await ReadyAsync(provider);
            await checks.ThrowsAnyAsync(() => provider.ReviveAsync(provider.PlayerEpoch),
                "A foreign nonce, wrong alive state or replacement ped must never confirm a revive: " + corruption);
            checks.That(pipe.Count(ClientModuleKind.Action) == 1 &&
                !(await provider.GetEventsAsync(0)).Events.Any(item => item.Type == "observed"),
                "An invalid native observation must not be replayed or reported as observed: " + corruption);
            await EventuallyAsync(() => Task.FromResult(pipe.ClosedConnections > 0), "An invalid observation did not close its lease.");
            checks.That(pipe.ClosedConnections > 0, "Invalid observations must close the offending native connection.");
            pipe.ThrowIfFaulted();
        }
    }

    private static async Task TimeoutNoReplayAsync(Checks checks)
    {
        var external = new MemoryProvider { Ready = false };
        await using var pipe = new ModulePipe { AutoObserveActions = false };
        using var provider = new ClientModuleSession(external, () => 101, pipe.ConnectAsync);
        await ReadyAsync(provider);
        await checks.ThrowsAsync<TimeoutException>(() => provider.ReviveAsync(provider.PlayerEpoch),
            "A fresh module that only acknowledges Pending must time out instead of claiming a completed revive.");
        checks.That(pipe.Count(ClientModuleKind.Action) == 1, "A timed out action must have been sent once only.");
        await EventuallyAsync(() => Task.FromResult(pipe.Connections >= 2), "The provider did not open a new lease after timing out.");
        await ReadyAsync(provider);
        await Task.Delay(260);
        checks.That(pipe.Count(ClientModuleKind.Action) == 1 && pipe.Count(ClientModuleKind.Settings) == 0,
            "Reconnecting after an action timeout must never replay an old action or configuration.");
        checks.That(!(await provider.GetEventsAsync(0)).Events.Any(item => item.Type == "observed"),
            "Timeout and reconnection must never synthesize a successful observation.");
        pipe.ThrowIfFaulted();
    }

    private static async Task VehicleActionsAsync(Checks checks)
    {
        foreach (bool alive in new[] { true, false })
        {
            var external = new MemoryProvider { Ready = false };
            await using var pipe = new ModulePipe { Alive = alive };
            using var provider = new ClientModuleSession(external, () => 101, pipe.ConnectAsync);
            await ReadyAsync(provider);
            var settings = Settings(); settings.Player.GodMode = settings.Player.Noclip = true;
            await provider.PushSettingsAsync(settings);
            long epoch = provider.PlayerEpoch;
            await provider.SetVehicleLockAsync(true, epoch);
            await provider.SetVehicleLockAsync(false);
            var actions = pipe.Requests.Where(item => item.Kind == ClientModuleKind.Action).ToArray();
            checks.That(actions.Length == 2 &&
                BinaryPrimitives.ReadUInt32LittleEndian(actions[0].Payload.AsSpan(24)) == (uint)ClientModuleAction.LockVehicle &&
                BinaryPrimitives.ReadUInt32LittleEndian(actions[1].Payload.AsSpan(24)) == (uint)ClientModuleAction.UnlockVehicle,
                "Vehicle buttons must dispatch the explicit lock/unlock operation without substituting a health action.");
            checks.That(BinaryPrimitives.ReadUInt64LittleEndian(actions[0].Payload.AsSpan(16)) == 1 &&
                BinaryPrimitives.ReadUInt64LittleEndian(actions[1].Payload.AsSpan(16)) == 2 &&
                actions.All(item => BinaryPrimitives.ReadUInt32LittleEndian(item.Payload.AsSpan(28)) == pipe.Ped),
                "Each door action must carry a unique nonce and the captured ped.");
            checks.That(pipe.Count(ClientModuleKind.Settings) == 1 &&
                BinaryPrimitives.ReadUInt32LittleEndian(pipe.Requests.Single(item => item.Kind == ClientModuleKind.Settings).Payload.AsSpan(24)) == 3,
                "Door actions must preserve GodMode/noclip rather than dispatch disabling settings.");
            var status = await provider.GetStatusAsync();
            checks.That(status.SessionActive && status.FeatureStates.Single(item => item.Name == "player.godMode").State == "observed" &&
                status.FeatureStates.Single(item => item.Name == "player.noclip").State == "observed",
                "An observed door action must retain the applied native configuration.");
            var observations = (await provider.GetEventsAsync(0)).Events.Where(item => item.Type == "observed").ToArray();
            checks.That(observations.Length == 2 && observations.All(item => item.Message.Contains("framework", StringComparison.Ordinal)) &&
                external.VehicleActions == 0 && external.Revives == 0 && external.Suicides == 0,
                "Vehicle success requires its native observation and must not claim framework keys or delegate a memory/health action.");
            using var diagnostics = JsonDocument.Parse(provider.CaptureDiagnostics());
            uint state = diagnostics.RootElement.GetProperty("Native").GetProperty("State").GetProperty("StateFlags").GetUInt32();
            checks.That(((state & (uint)ClientModuleStateFlags.PedAlive) != 0) == alive,
                "Lock/unlock observations must preserve and accept the existing alive state.");
            pipe.ThrowIfFaulted();
        }
    }

    private static async Task ExperimentalDirectedFireSettingsAsync(Checks checks)
    {
        var external = new MemoryProvider { Ready = true, ProcessId = 101 };
        await using var pipe = new ModulePipe();
        using var provider = new ClientModuleSession(external, () => 101, pipe.ConnectAsync);
        await ReadyAsync(provider);
        var settings = Settings();
        settings.Player.GodMode = true;
        settings.Silent.Enabled = settings.Silent.OnlyVisible = true;
        settings.Silent.Fov = 157; settings.Silent.TargetBone = "Chest"; settings.Silent.ActivationKey = "Ctrl";
        await provider.PushSettingsAsync(settings);
        var request = pipe.Requests.Single(item => item.Kind == ClientModuleKind.Settings);
        checks.That(request.Payload.Length == 48 &&
            BinaryPrimitives.ReadUInt32LittleEndian(request.Payload.AsSpan(24)) == 257 &&
            BinaryPrimitives.ReadUInt32LittleEndian(request.Payload.AsSpan(32)) == 157 &&
            BinaryPrimitives.ReadUInt32LittleEndian(request.Payload.AsSpan(36)) == 2 &&
            BinaryPrimitives.ReadUInt32LittleEndian(request.Payload.AsSpan(40)) == 4 &&
            BinaryPrimitives.ReadUInt32LittleEndian(request.Payload.AsSpan(44)) == 3,
            "Directed fire must carry only the explicit experimental flag and validated FOV/bone/key/options in the v2 settings payload.");
        var shadow = external.Pushed.Single();
        shadow.Validate();
        checks.That(!shadow.Silent.Enabled && !shadow.Silent.DrawFov && !shadow.Player.GodMode,
            "The wrapped external backend must never receive enabled experimental Silent Aim or native-owned player flags.");
        var status = await provider.GetStatusAsync();
        var feature = status.FeatureStates.Single(item => item.Name == "silent");
        checks.That(status.Capabilities.Contains("silent") && feature.State == "observed" &&
            feature.Message.Contains("experimental", StringComparison.Ordinal) &&
            feature.Message.Contains("Acerto não foi validado", StringComparison.Ordinal),
            "A processed experimental setting must explicitly avoid claiming a hit or damage observation.");
        pipe.Transform = (_, native) => native with { Error = 13, Message = "Experimental: consumo de munição não observado; disparo não confirmado." };
        await EventuallyAsync(async () => (await provider.GetStatusAsync()).FeatureStates.Single(item => item.Name == "silent").State == "failed",
            "The experimental dispatch failure did not reach its individual feature state.");
        status = await provider.GetStatusAsync();
        feature = status.FeatureStates.Single(item => item.Name == "silent");
        checks.That(feature.ObservedUtc is null && feature.Message.Contains("não confirmado", StringComparison.Ordinal) &&
            status.Ready && status.SessionActive && status.FeatureStates.Single(item => item.Name == "player.godMode").State == "observed",
            "An isolated experimental failure must preserve other native effects without claiming the directed shot was observed.");
        settings.Silent.Fov = 499;
        using var diagnostic = JsonDocument.Parse(provider.CaptureDiagnostics());
        checks.That(diagnostic.RootElement.GetProperty("Configuration").GetProperty("Silent").GetProperty("Fov").GetInt32() == 157,
            "The experimental configuration must remain an owned snapshot after caller mutations.");
        pipe.Capabilities = (ClientModuleCapabilities)255;
        await EventuallyAsync(async () => !(await provider.GetStatusAsync()).Capabilities.Contains("silent"),
            "Revoking experimental native support did not remove Silent Aim capability.");
        status = await provider.GetStatusAsync();
        checks.That(status.FeatureStates.Single(item => item.Name == "silent").State == "unsupported",
            "A wrapped memory Silent Aim capability must not override missing native experimental support.");
        await checks.ThrowsAsync<NotSupportedException>(() => provider.PushSettingsAsync(settings),
            "Enabled experimental directed fire must be rejected when its native capability is missing.");
        checks.That(pipe.Count(ClientModuleKind.Settings) == 1 && external.Pushed.Count == 1 && pipe.Count(ClientModuleKind.Action) == 0,
            "Unsupported experimental settings must not trigger a native replay or a memory fallback.");
        pipe.ThrowIfFaulted();
    }

    private static async Task VehicleCapabilitiesAsync(Checks checks)
    {
        var external = new MemoryProvider { Ready = true, ProcessId = 101 };
        await using var pipe = new ModulePipe { Capabilities = (ClientModuleCapabilities)63 | ClientModuleCapabilities.LockVehicle };
        using var provider = new ClientModuleSession(external, () => 101, pipe.ConnectAsync);
        await ReadyAsync(provider);
        // This regression reproduces a synchronous rejected operation followed
        // by a pipe await that can resume on a different managed thread.
        var sessionGate = typeof(ClientModuleSession).GetField("_gate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(provider)!;
        checks.That(!Monitor.IsEntered(sessionGate), "A ready provider must not leave its Monitor held by the awaiting caller.");
        var status = await provider.GetStatusAsync();
        checks.That(!Monitor.IsEntered(sessionGate), "A synchronous status result must release the provider Monitor before returning.");
        checks.That(status.Capabilities.Contains("vehicle.lock") && !status.Capabilities.Contains("vehicle.unlock") &&
            status.FeatureStates.Single(item => item.Name == "vehicle.lock").State == "ready" &&
            status.FeatureStates.Single(item => item.Name == "vehicle.unlock").State == "unsupported",
            "Vehicle capabilities must come only from the corresponding native bit, never a wrapped memory capability.");
        checks.That(status.FeatureStates.Single(item => item.Name == "vehicle.lock").Message.Contains("framework", StringComparison.Ordinal),
            "Door capability descriptions must distinguish native door state from server framework keys.");
        await checks.ThrowsAsync<NotSupportedException>(() => provider.SetVehicleLockAsync(false),
            "An unannounced unlock operation must be refused before writing the pipe.");
        checks.That(!Monitor.IsEntered(sessionGate), "Rejecting an unsupported operation must release the Monitor before completing its task.");
        checks.That(pipe.Count(ClientModuleKind.Action) == 0 && external.VehicleActions == 0,
            "An unsupported native door operation must not fall back to the memory provider.");
        await provider.SetVehicleLockAsync(true);
        checks.That(pipe.Count(ClientModuleKind.Action) == 1,
            "Rejecting unsupported unlock must not prevent an explicitly supported lock on the same ready session.");
        pipe.Capabilities = (ClientModuleCapabilities)63;
        await EventuallyAsync(async () => !(await provider.GetStatusAsync()).Capabilities.Contains("vehicle.lock"),
            "Revoking the lock capability did not update the provider.");
        await checks.ThrowsAsync<NotSupportedException>(() => provider.SetVehicleLockAsync(true),
            "A revoked native lock capability must be checked again for each action.");
        checks.That(!Monitor.IsEntered(sessionGate), "Rejecting a revoked capability must also release the provider Monitor.");
        checks.That(pipe.Count(ClientModuleKind.Action) == 1 && external.VehicleActions == 0,
            "Revoked vehicle operations must remain unsent and must not fall back to memory.");
        pipe.ThrowIfFaulted();
    }

    private static async Task VehicleTimeoutNoReplayAsync(Checks checks)
    {
        var external = new MemoryProvider { Ready = false };
        await using var pipe = new ModulePipe { AutoObserveActions = false };
        using var provider = new ClientModuleSession(external, () => 101, pipe.ConnectAsync);
        await ReadyAsync(provider);
        await checks.ThrowsAsync<TimeoutException>(() => provider.SetVehicleLockAsync(true),
            "Pending is only a receipt; a door action must time out without a matching native observation.");
        checks.That(pipe.Count(ClientModuleKind.Action) == 1 && pipe.Count(ClientModuleKind.Settings) == 0,
            "A timed-out door request must send exactly one action and no configuration changes.");
        await EventuallyAsync(() => Task.FromResult(pipe.Connections >= 2), "The door timeout did not close the old lease.");
        await ReadyAsync(provider);
        pipe.AutoObserveActions = true;
        await Task.Delay(260);
        checks.That(pipe.Count(ClientModuleKind.Action) == 1 &&
            !(await provider.GetEventsAsync(0)).Events.Any(item => item.Type == "observed"),
            "A later fresh lease must not replay or report success for the timed-out door action.");
        pipe.ThrowIfFaulted();
    }

    private static async Task VehicleTargetAndCancellationAsync(Checks checks)
    {
        var external = new MemoryProvider { Ready = false };
        await using var pipe = new ModulePipe { AutoObserveActions = false };
        using var provider = new ClientModuleSession(external, () => 101, pipe.ConnectAsync);
        await ReadyAsync(provider);
        long previousEpoch = provider.PlayerEpoch;
        pipe.Ped++;
        await EventuallyAsync(() => Task.FromResult(provider.PlayerEpoch != previousEpoch), "Replacing the ped did not invalidate its vehicle action target.");
        await checks.ThrowsAsync<InvalidOperationException>(() => provider.SetVehicleLockAsync(true, previousEpoch),
            "A door action confirmed for another ped must be rejected before transmission.");
        using var beforeSend = new CancellationTokenSource(); beforeSend.Cancel();
        await checks.ThrowsAsync<OperationCanceledException>(() => provider.SetVehicleLockAsync(false, beforeSend.Token),
            "A canceled door request must remain unsent.");
        checks.That(pipe.Count(ClientModuleKind.Action) == 0 && pipe.Count(ClientModuleKind.Settings) == 0,
            "Stale and pre-canceled door actions must have no native side effects.");

        using var afterSend = new CancellationTokenSource();
        Task pending = provider.SetVehicleLockAsync(false, afterSend.Token);
        await EventuallyAsync(() => Task.FromResult(pipe.Count(ClientModuleKind.Action) == 1), "The cancelable unlock was not transmitted.");
        checks.That(!pending.IsCompleted, "An acknowledged but unobserved unlock must remain pending.");
        afterSend.Cancel();
        await checks.ThrowsAsync<OperationCanceledException>(() => pending,
            "Caller cancellation must abort an unobserved vehicle operation.");
        await EventuallyAsync(() => Task.FromResult(pipe.Connections >= 2), "Canceling a transmitted door action did not replace its lease.");
        await ReadyAsync(provider);

        Task replacedPed = provider.SetVehicleLockAsync(true);
        await EventuallyAsync(() => Task.FromResult(pipe.Count(ClientModuleKind.Action) == 2), "The second vehicle action was not sent.");
        pipe.Ped++;
        await checks.ThrowsAnyAsync(() => replacedPed,
            "Changing the ped while a door action is pending must invalidate its eventual result.");
        await EventuallyAsync(() => Task.FromResult(pipe.Connections >= 3), "The changed-ped vehicle action did not close its lease.");
        await ReadyAsync(provider);
        pipe.AutoObserveActions = true;
        await Task.Delay(260);
        checks.That(pipe.Count(ClientModuleKind.Action) == 2 && pipe.Count(ClientModuleKind.Settings) == 0 &&
            !(await provider.GetEventsAsync(0)).Events.Any(item => item.Type == "observed"),
            "Canceled and changed-ped vehicle requests must never be replayed or reported as observed after reconnecting.");
        pipe.ThrowIfFaulted();
    }

    private static async Task MalformedPollRecoveryAsync(Checks checks)
    {
        var external = new MemoryProvider { Ready = false };
        await using var pipe = new ModulePipe();
        int corrupt = 1;
        pipe.Transform = (request, status) => request.Kind == ClientModuleKind.Poll && Interlocked.Exchange(ref corrupt, 0) != 0
            ? status with { NativeTick = 1 } : status;
        using var provider = new ClientModuleSession(external, () => 101, pipe.ConnectAsync);
        await ReadyAsync(provider);
        await EventuallyAsync(() => Task.FromResult(pipe.Connections >= 2), "A regressive poll frame killed the worker instead of closing and replacing the lease.");
        await ReadyAsync(provider);
        checks.That(pipe.ClosedConnections >= 1 && pipe.Count(ClientModuleKind.Action) == 0 && pipe.Count(ClientModuleKind.Settings) == 0,
            "Recovering from invalid status must close the old lease without replaying user mutations.");
        checks.That((await provider.GetEventsAsync(0)).Events.Any(item => item.Type == "connected") &&
            !(await provider.GetEventsAsync(0)).Events.Any(item => item.Type == "observed"),
            "Transport recovery must remain separate from successful gameplay observations.");
        pipe.ThrowIfFaulted();
    }

    private static async Task DisconnectAndDisposeAsync(Checks checks)
    {
        var external = new MemoryProvider { Ready = false };
        await using var pipe = new ModulePipe();
        var provider = new ClientModuleSession(external, () => 101, pipe.ConnectAsync);
        try
        {
            await ReadyAsync(provider);
            var settings = Settings(); settings.Player.Invisible = true;
            await provider.PushSettingsAsync(settings);
            await provider.DisconnectAsync();
            await EventuallyAsync(() => Task.FromResult(pipe.ClosedConnections > 0), "Disconnect did not close its native pipe.");
            checks.That(pipe.Count(ClientModuleKind.Disconnect) == 1 && external.Disconnects == 1 && !provider.DetectionEnabled,
                "Explicit disconnect must request native restoration and stop both providers.");
            int connections = pipe.Connections;
            await Task.Delay(260);
            checks.That(pipe.Connections == connections && !(await provider.GetStatusAsync()).Capabilities.Contains("player.invisible"),
                "A disconnected provider must not reconnect or retain native capabilities.");
        }
        finally { provider.Dispose(); }
        provider.Dispose();
        checks.That(external.Disposals == 1, "Disposal must release the wrapped provider exactly once.");
        await checks.ThrowsAsync<ObjectDisposedException>(() => provider.GetStatusAsync(),
            "A disposed provider must reject status reads instead of resurrecting its lease.");
        pipe.ThrowIfFaulted();

        external = new MemoryProvider { Ready = false };
        await using var pendingPipe = new ModulePipe { AutoObserveActions = false };
        provider = new ClientModuleSession(external, () => 101, pendingPipe.ConnectAsync);
        await ReadyAsync(provider);
        Task pending = provider.ReviveAsync(provider.PlayerEpoch);
        await EventuallyAsync(() => Task.FromResult(pendingPipe.Count(ClientModuleKind.Action) == 1), "Pending action was not sent before disposal.");
        provider.Dispose();
        await checks.ThrowsAnyAsync(() => pending, "Disposing a provider must cancel an in-flight unobserved action.");
        await EventuallyAsync(() => Task.FromResult(pendingPipe.ClosedConnections > 0), "Disposal did not close the native lease.");
        checks.That(pendingPipe.Count(ClientModuleKind.Action) == 1 && external.Disposals == 1,
            "Disposal must close the transport without replaying an outstanding action.");
        pendingPipe.ThrowIfFaulted();
    }

    private static MenuSettings Settings()
    {
        var settings = new MenuSettings();
        settings.Aim.OnlyVisible = settings.Silent.OnlyVisible = settings.Esp.OnlyVisible = settings.Esp.Names = false;
        return settings;
    }

    private static Task ReadyAsync(ClientModuleSession provider) => EventuallyAsync(async () =>
        (await provider.GetStatusAsync()).Capabilities.Contains("player.revive"), "The test module did not become native-ready.");

    private static async Task EventuallyAsync(Func<Task<bool>> condition, string message)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            while (!await condition()) await Task.Delay(15, deadline.Token);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested) { throw new InvalidOperationException(message); }
    }

    private sealed class Checks
    {
        public int Count { get; private set; }
        public void That(bool condition, string message)
        {
            Count++;
            if (!condition) throw new InvalidOperationException(message);
        }
        public async Task ThrowsAsync<T>(Func<Task> action, string message) where T : Exception
        {
            Count++;
            try { await action(); }
            catch (T) { return; }
            throw new InvalidOperationException(message);
        }
        public async Task ThrowsAnyAsync(Func<Task> action, string message)
        {
            Count++;
            try { await action(); }
            catch (Exception error) when (error is IOException or InvalidDataException or InvalidOperationException or OperationCanceledException or TimeoutException) { return; }
            throw new InvalidOperationException(message);
        }
    }

    private sealed class MemoryProvider : ILocalGameSession
    {
        private readonly object _gate = new();
        private readonly List<MenuSettings> _pushed = [];
        private int _ready, _processId, _pushCount, _disconnects, _disposals, _blockPush;
        public bool Ready { get => Volatile.Read(ref _ready) != 0; set => Volatile.Write(ref _ready, value ? 1 : 0); }
        public int ProcessId { get => Volatile.Read(ref _processId); set => Volatile.Write(ref _processId, value); }
        public bool DetectionEnabled { get; set; } = true;
        public long PlayerEpoch => 1;
        public int AppliedOffset { get; set; }
        public int Revives { get; private set; }
        public int Suicides { get; private set; }
        public int VehicleActions { get; private set; }
        public int Disconnects => Volatile.Read(ref _disconnects);
        public int Disposals => Volatile.Read(ref _disposals);
        public List<MenuSettings> Pushed { get { lock (_gate) return _pushed.Select(ExternalStateSerialization.Clone).ToList(); } }
        public TaskCompletionSource PushEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleasePush { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void BlockNextPush() => Interlocked.Exchange(ref _blockPush, 1);
        public Task<ServerStatus> GetStatusAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long revision = Volatile.Read(ref _pushCount);
            return Task.FromResult(new ServerStatus
            {
                Ready = Ready, PlayerOnline = Ready, PlayerId = Ready ? ProcessId : null,
                DesiredRevision = revision, AppliedRevision = Math.Max(0, revision + AppliedOffset),
                Capabilities = Ready ? ["aim.enabled", "esp.enabled", "weapons.noRecoil", "player.godMode", "player.suicide", "vehicle.lock", "vehicle.unlock", "silent"] : [],
                FeatureStates = [new() { Name = "player.godMode", State = "observed", Message = "memory sentinel" }],
                Message = "memory provider mock"
            });
        }
        public Task<MenuEvents> GetEventsAsync(long since, CancellationToken cancellationToken = default) => Task.FromResult(new MenuEvents());
        public async Task PushSettingsAsync(MenuSettings settings, CancellationToken cancellationToken = default)
        {
            lock (_gate) _pushed.Add(ExternalStateSerialization.Clone(settings));
            Interlocked.Increment(ref _pushCount);
            if (Interlocked.Exchange(ref _blockPush, 0) != 0)
            {
                PushEntered.TrySetResult();
                await ReleasePush.Task.WaitAsync(cancellationToken);
            }
        }
        public Task ReviveAsync(CancellationToken cancellationToken = default) { Revives++; return Task.CompletedTask; }
        public Task ReviveAsync(long expectedPlayerEpoch, CancellationToken cancellationToken = default) => ReviveAsync(cancellationToken);
        public Task SuicideAsync(CancellationToken cancellationToken = default) { Suicides++; return Task.CompletedTask; }
        public Task SuicideAsync(long expectedPlayerEpoch, CancellationToken cancellationToken = default) => SuicideAsync(cancellationToken);
        public Task SetVehicleLockAsync(bool locked, CancellationToken cancellationToken = default) { VehicleActions++; return Task.CompletedTask; }
        public Task SetVehicleLockAsync(bool locked, long expectedPlayerEpoch, CancellationToken cancellationToken = default) => SetVehicleLockAsync(locked, cancellationToken);
        public Task DisconnectAsync(CancellationToken cancellationToken = default)
        { Interlocked.Increment(ref _disconnects); Ready = false; return Task.CompletedTask; }
        public string CaptureDiagnostics() => "{\"MemoryProvider\":true}";
        public void Dispose() => Interlocked.Increment(ref _disposals);
    }

    private sealed record Request(ClientModuleKind Kind, byte[] Payload);

    /// <summary>Protocol peer only: closing its pipe observes lease loss, not real game restoration.</summary>
    private sealed class ModulePipe : IAsyncDisposable
    {
        private readonly CancellationTokenSource _lifetime = new();
        private readonly ConcurrentQueue<Request> _requests = new();
        private readonly ConcurrentQueue<Task> _servers = new();
        private readonly ConcurrentQueue<Exception> _faults = new();
        private uint _ped = 0xBEEF;
        private int _connections, _closed, _autoObserve = 1, _autoApply = 1, _settingsActive = 1, _nativeReady = 1;
        private uint _capabilities = 511;
        private long _frozenTick;
        public bool Alive { get; init; } = true;
        public ClientModuleCapabilities Capabilities { get => (ClientModuleCapabilities)Volatile.Read(ref _capabilities);
            set => Volatile.Write(ref _capabilities, (uint)value); }
        public uint Ped { get => Volatile.Read(ref _ped); set => Volatile.Write(ref _ped, value); }
        public bool StaleFrames { get => Interlocked.Read(ref _frozenTick) != 0;
            set => Interlocked.Exchange(ref _frozenTick, value ? Environment.TickCount64 : 0); }
        public bool AutoObserveActions { get => Volatile.Read(ref _autoObserve) != 0; set => Volatile.Write(ref _autoObserve, value ? 1 : 0); }
        public bool AutoApplySettings { get => Volatile.Read(ref _autoApply) != 0; set => Volatile.Write(ref _autoApply, value ? 1 : 0); }
        public bool SettingsActive { get => Volatile.Read(ref _settingsActive) != 0; set => Volatile.Write(ref _settingsActive, value ? 1 : 0); }
        public bool NativeReady { get => Volatile.Read(ref _nativeReady) != 0; set => Volatile.Write(ref _nativeReady, value ? 1 : 0); }
        public Func<Request, ClientModuleStatus, ClientModuleStatus>? Transform { get; set; }
        public int Connections => Volatile.Read(ref _connections);
        public int ClosedConnections => Volatile.Read(ref _closed);
        public Request[] Requests => _requests.ToArray();
        public int Count(ClientModuleKind kind) => _requests.Count(item => item.Kind == kind);

        public async Task<NamedPipeClientStream> ConnectAsync(int processId, CancellationToken cancellationToken)
        {
            if (processId <= 0) throw new InvalidOperationException("Unexpected test process.");
            string name = "zyon-provider-test-" + Guid.NewGuid().ToString("N");
            var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            _servers.Enqueue(ServeAsync(server, _lifetime.Token));
            var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
            try { await client.ConnectAsync(cancellationToken); return client; }
            catch { client.Dispose(); throw; }
        }

        private async Task ServeAsync(NamedPipeServerStream server, CancellationToken cancellationToken)
        {
            try
            {
                using (server)
                {
                    await server.WaitForConnectionAsync(cancellationToken);
                    Interlocked.Increment(ref _connections);
                    byte[]? token = null;
                    ulong desired = 0, applied = 0, nonce = 0;
                    ClientModuleAction action = ClientModuleAction.None;
                    ClientModuleActionResult result = ClientModuleActionResult.None;
                    bool alive = Alive;
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        byte[] headerBytes = new byte[ClientModuleProtocol.HeaderLength];
                        await server.ReadExactlyAsync(headerBytes, cancellationToken);
                        var header = ClientModuleProtocol.DecodeHeader(headerBytes);
                        byte[] payload = new byte[header.PayloadLength];
                        await server.ReadExactlyAsync(payload, cancellationToken);
                        if (token is null)
                        {
                            if (header.Kind != ClientModuleKind.Hello) throw new InvalidDataException("First frame was not Hello.");
                            token = payload;
                        }
                        else if (!payload.AsSpan(0, ClientModuleProtocol.TokenLength).SequenceEqual(token))
                            throw new InvalidDataException("Provider changed the authentication token mid-connection.");
                        var request = new Request(header.Kind, payload);
                        _requests.Enqueue(request);
                        if (header.Kind == ClientModuleKind.Settings)
                            desired = BinaryPrimitives.ReadUInt64LittleEndian(payload.AsSpan(16));
                        if (AutoApplySettings) applied = desired;
                        if (header.Kind == ClientModuleKind.Action)
                        {
                            nonce = BinaryPrimitives.ReadUInt64LittleEndian(payload.AsSpan(16));
                            action = (ClientModuleAction)BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(24));
                            result = ClientModuleActionResult.Pending;
                        }
                        if (result == ClientModuleActionResult.Pending && AutoObserveActions)
                        {
                            result = ClientModuleActionResult.Observed;
                            if (action == ClientModuleAction.ReviveNative) alive = true;
                            else if (action == ClientModuleAction.Suicide) alive = false;
                        }
                        long tick = Interlocked.Read(ref _frozenTick);
                        ulong sampled = (ulong)(tick != 0 ? tick : Environment.TickCount64);
                        var status = new ClientModuleStatus(sampled, desired, SettingsActive ? applied : 0, nonce,
                            Capabilities,
                            (NativeReady ? ClientModuleStateFlags.Ready : 0) | (alive ? ClientModuleStateFlags.PedAlive : 0) |
                            (applied > 0 && SettingsActive ? ClientModuleStateFlags.SettingsActive : 0),
                            action, result, Ped, 3258, 0, "test native frame");
                        status = Transform?.Invoke(request, status) ?? status;
                        await ClientModuleProtocol.WriteFrameAsync(server, ClientModuleKind.Status,
                            ClientModuleProtocol.EncodeStatus(token, status), cancellationToken);
                    }
                }
            }
            catch (Exception error) when (error is EndOfStreamException or OperationCanceledException or ObjectDisposedException) { }
            catch (IOException) { /* The provider may close a pipe while a response is in flight. */ }
            catch (Exception error) { _faults.Enqueue(error); }
            finally { server.Dispose(); Interlocked.Increment(ref _closed); }
        }

        public void ThrowIfFaulted()
        {
            if (_faults.TryPeek(out var error)) throw new InvalidOperationException("Named-pipe peer failed.", error);
        }

        public async ValueTask DisposeAsync()
        {
            _lifetime.Cancel();
            await Task.WhenAll(_servers.ToArray()).WaitAsync(TimeSpan.FromSeconds(3));
            _lifetime.Dispose();
            ThrowIfFaulted();
        }
    }
}
