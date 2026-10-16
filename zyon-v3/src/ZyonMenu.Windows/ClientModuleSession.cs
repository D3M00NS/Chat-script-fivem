// C# / optional local ASI module provider / .NET 8 Windows / authenticated, PID-bound named pipe.
using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using ZyonMenu.Core;

namespace ZyonMenu.Windows;

/// <summary>
/// The optional module owns native player/ammo operations. The wrapped provider
/// owns only existing memory Aim/ESP/recoil/spread operations. No module is loaded
/// or installed by this class, and no native address is accepted from the wire.
/// </summary>
internal sealed partial class ClientModuleSession : ILocalGameSession
{
    private static readonly string[] NativeFeatures =
        ["player.godMode", "player.noclip", "player.invisible", "weapons.freezeAmmo", "player.revive", "player.suicide", "vehicle.lock", "vehicle.unlock", "silent"];
    private static readonly ClientModuleCapabilities[] NativeCapabilities =
        [ClientModuleCapabilities.God, ClientModuleCapabilities.Noclip, ClientModuleCapabilities.Invisible,
            ClientModuleCapabilities.FreezeAmmo, ClientModuleCapabilities.ReviveNative, ClientModuleCapabilities.Suicide,
            ClientModuleCapabilities.LockVehicle, ClientModuleCapabilities.UnlockVehicle, ClientModuleCapabilities.DirectedFireExperimental];
    private readonly ILocalGameSession _external;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _io = new(1, 1), _mutations = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Task _worker;
    private readonly List<MenuEvent> _events = [];
    private readonly Func<int?> _discoverProcess;
    private readonly Func<int, CancellationToken, Task<NamedPipeClientStream>> _connect;
    private readonly Func<object?>? _installationDiagnostics;
    private Connection? _connection;
    private ClientModuleStatus? _native;
    private MenuSettings _settings = EmptySettings();
    private string _moduleMessage = "Módulo opcional não conectado; nenhuma operação nativa foi enviada.";
    private int _enabled = 1, _disposed, _actionPending;
    private long _playerEpoch = 1, _settingsEpoch, _connectionEpoch, _revision, _cursor, _externalCursor;
    private long _externalRevision, _lastConnectionAttempt;
    private bool _externalParticipated, _nativeParticipated, _hasSettings;
    private int? _identityPid, _externalPid;
    private uint _identityPed;
    private bool _identityReady, _externalReady;
    private DateTimeOffset? _nativeObservedUtc;
    private CancellationTokenSource? _actionAbort;

    private sealed class Connection(NamedPipeClientStream stream, int processId, byte[] token, long epoch) : IDisposable
    {
        public readonly NamedPipeClientStream Stream = stream;
        public readonly int ProcessId = processId;
        public readonly byte[] Token = token;
        public readonly long Epoch = epoch;
        public ulong HighestRevision, HighestNonce, SeenDesired, SeenApplied, SeenTick;
        public bool HadReadyFrame;
        public ClientModuleAction LastAction;
        public void Dispose() { Stream.Dispose(); CryptographicOperations.ZeroMemory(Token); }
    }

    internal ClientModuleSession(ILocalGameSession external, Func<int?> discoverProcess,
        Func<int, CancellationToken, Task<NamedPipeClientStream>> connect, Func<object?>? installationDiagnostics = null)
    {
        _external = external ?? throw new ArgumentNullException(nameof(external));
        _discoverProcess = discoverProcess ?? throw new ArgumentNullException(nameof(discoverProcess));
        _connect = connect ?? throw new ArgumentNullException(nameof(connect));
        _installationDiagnostics = installationDiagnostics;
        _worker = Task.Run(PollLoopAsync);
    }

    public bool DetectionEnabled
    {
        get => Volatile.Read(ref _enabled) != 0;
        set
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            Interlocked.Exchange(ref _enabled, value ? 1 : 0);
            _external.DetectionEnabled = value;
            if (!value)
            {
                Interlocked.Increment(ref _settingsEpoch);
                AbortConnection(null, "Detecção interrompida; o módulo deve restaurar seus efeitos e encerrar a lease.");
            }
        }
    }

    public long PlayerEpoch { get { lock (_gate) { RefreshIdentityLocked(); return _playerEpoch; } } }

    public async Task<ServerStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var memory = await _external.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        lock (_gate)
        {
            _externalPid = memory.PlayerId; _externalReady = memory.Ready;
            RefreshIdentityLocked();
            bool ready = NativeReadyLocked();
            var result = ExternalStateSerialization.Clone(memory);
            result.NativeClient = new NativeClientStatus
            {
                TransportConnected = _connection is not null, Ready = ready,
                ProcessId = _connection?.ProcessId, Error = _native?.Error,
                Message = _connection is not null && _externalReady && _externalPid != _connection.ProcessId
                    ? "O leitor de memória e o módulo pertencem a processos diferentes. Feche cópias extras do FiveM e detecte novamente."
                    : _moduleMessage
            };
            var capabilities = memory.Ready
                ? memory.Capabilities.Where(name => !NativeFeatures.Contains(name, StringComparer.OrdinalIgnoreCase)).ToList()
                : [];
            result.FeatureStates.RemoveAll(feature => NativeFeatures.Contains(feature.Name, StringComparer.OrdinalIgnoreCase));
            for (int index = 0; index < NativeFeatures.Length; index++)
            {
                bool supported = ready && _native!.Capabilities.HasFlag(NativeCapabilities[index]);
                if (supported) capabilities.Add(NativeFeatures[index]);
                bool action = index is >= 4 and <= 7;
                bool directedFireFailed = index == 8 && supported && _native!.Error == 13;
                bool configured = !action && IsConfigured((ClientModuleSettingsFlags)(1u << index), _settings);
                bool observed = supported && !action && !directedFireFailed && _nativeParticipated && _hasSettings &&
                    _native!.AppliedRevision == (ulong)_revision && _native.StateFlags.HasFlag(ClientModuleStateFlags.SettingsActive);
                result.FeatureStates.Add(new FeatureStatus
                {
                    Name = NativeFeatures[index],
                    State = !supported ? ready ? "unsupported" : "waiting"
                        : directedFireFailed ? "failed" : observed && configured ? "observed" : configured ? "waiting" : "ready",
                    Message = !supported ? ready ? "A API desse cliente não disponibilizou todas as operações exigidas por esta função." : _moduleMessage
                        : directedFireFailed ? _native!.Message
                        : action ? index >= 6
                            ? "Disponível pelo módulo local; a observação da porta nativa não comprova chaves ou permissões do framework do servidor."
                            : "Disponível pelo módulo local; somente a observação nativa confirma a ação. Reviver não limpa estado médico ESX/QBCore."
                        : index == 8 ? observed
                            ? "Quadro experimental processado; veja consumo no diagnóstico. Acerto não foi validado."
                            : "Disparo direcionado experimental disponível; nenhuma configuração correspondente foi observada. Munição não confirma acerto ou dano."
                        : observed ? "Configuração processada em quadro nativo recente; a observação não comprova políticas de inventário ou hospital do servidor."
                        : "Módulo local pronto; nenhuma configuração nativa correspondente foi observada.",
                    ObservedUtc = observed ? _nativeObservedUtc : null
                });
            }
            result.Capabilities = capabilities.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            result.Ready = memory.Ready || ready;
            result.PlayerOnline = memory.PlayerOnline || ready;
            if (ready && !memory.Ready)
            {
                result.PlayerId = _connection!.ProcessId;
                result.PlayerName = "Personagem validado pelo módulo local b3258";
            }
            result.Version = AppIdentity.DisplayVersion + " / externo + módulo local opcional";
            result.DesiredRevision = _revision;
            bool nativeApplied = !_nativeParticipated || ready && _native!.AppliedRevision == (ulong)_revision &&
                _native.StateFlags.HasFlag(ClientModuleStateFlags.SettingsActive);
            bool memoryApplied = !_externalParticipated || memory.Ready && memory.AppliedRevision >= _externalRevision;
            result.AppliedRevision = _hasSettings && nativeApplied && memoryApplied ? _revision : 0;
            // A live session can be waiting for a newer frame or for on-foot
            // noclip conditions. Losing an ACK must not erase the UI choices.
            result.SessionActive = _hasSettings && result.Ready &&
                (!_nativeParticipated || ready) && (!_externalParticipated || memory.Ready && memory.PlayerOnline);
            result.MedicalProvider = ready ? "native-client-module" : "";
            result.MedicalMessage = "Reviver nativo local: ESX, QBCore e hospitais personalizados mantêm seus próprios estados médicos.";
            result.Message = ready ? (_externalParticipated || memory.Ready
                ? "Módulo local e backend de memória separados por função. " + _moduleMessage
                : "Somente operações nativas estão disponíveis; Aim/ESP e leituras externas ainda não foram validados. " + _moduleMessage)
                : memory.Message + " " + _moduleMessage;
            return result;
        }
    }

    public Task<MenuEvents> GetEventsAsync(long since, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (since < 0) throw new ArgumentOutOfRangeException(nameof(since));
        lock (_gate)
            return Task.FromResult(new MenuEvents { Cursor = _cursor,
                Events = _events.Where(item => item.Id > since).Select(ExternalStateSerialization.Clone).ToList() });
    }

    public async Task PushSettingsAsync(MenuSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var snapshot = ExternalStateSerialization.Clone(settings);
        snapshot.Validate(allowExperimentalSilent: true);
        long fence = Interlocked.Read(ref _settingsEpoch);
        if (Volatile.Read(ref _actionPending) != 0) throw new InvalidOperationException("Aguarde a observação da ação antes de enviar outra configuração.");
        using var deadline = Deadline(cancellationToken);
        await _mutations.WaitAsync(deadline.Token).ConfigureAwait(false);
        try
        {
            EnsureSettingsFence(fence);
            var shadow = MemorySettings(snapshot);
            var memory = await _external.GetStatusAsync(deadline.Token).ConfigureAwait(false);
            Connection? connection;
            lock (_gate)
            {
                _externalPid = memory.PlayerId; _externalReady = memory.Ready;
                RefreshIdentityLocked();
                connection = NativeReadyLocked() ? _connection : null;
                ValidateRequestedNativeLocked(snapshot, connection is not null);
            }
            if (!memory.Ready && HasMemoryFeatures(shadow))
                throw new InvalidOperationException("Aim/ESP/recoil/spread exigem o backend de memória validado para este processo.");
            long memoryRevision = 0;
            if (memory.Ready)
            {
                await _external.PushSettingsAsync(shadow, deadline.Token).ConfigureAwait(false);
                var updated = await _external.GetStatusAsync(deadline.Token).ConfigureAwait(false);
                memoryRevision = updated.DesiredRevision;
            }
            EnsureSettingsFence(fence);
            long revision;
            lock (_gate)
            {
                if (connection is not null && (!ReferenceEquals(connection, _connection) || !NativeReadyLocked()))
                    throw new InvalidOperationException("A conexão nativa mudou durante a configuração; nenhuma repetição foi enviada.");
                _settings = snapshot; _hasSettings = true; revision = checked(++_revision);
                _externalParticipated = memory.Ready && HasMemoryFeatures(shadow); _externalRevision = memoryRevision;
                _nativeParticipated = connection is not null;
            }
            if (connection is not null)
                await SendSettingsAsync(connection, snapshot, revision, deadline.Token, fence).ConfigureAwait(false);
            lock (_gate) AddEventLocked("settings", "Configuração enviada aos provedores correspondentes; aceite não significa efeito observado no jogo.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new TimeoutException("O envio da configuração local excedeu dois segundos; nenhuma operação será repetida automaticamente."); }
        finally { _mutations.Release(); }
    }

    public Task ReviveAsync(CancellationToken cancellationToken = default) => ReviveAsync(PlayerEpoch, cancellationToken);
    public Task ReviveAsync(long expectedPlayerEpoch, CancellationToken cancellationToken = default) =>
        ExecuteActionAsync(ClientModuleAction.ReviveNative, expectedPlayerEpoch, cancellationToken);
    public Task SuicideAsync(CancellationToken cancellationToken = default) => SuicideAsync(PlayerEpoch, cancellationToken);
    public Task SuicideAsync(long expectedPlayerEpoch, CancellationToken cancellationToken = default) =>
        ExecuteActionAsync(ClientModuleAction.Suicide, expectedPlayerEpoch, cancellationToken);
    public Task SetVehicleLockAsync(bool locked, CancellationToken cancellationToken = default) =>
        SetVehicleLockAsync(locked, PlayerEpoch, cancellationToken);
    public Task SetVehicleLockAsync(bool locked, long expectedPlayerEpoch, CancellationToken cancellationToken = default) =>
        ExecuteActionAsync(locked ? ClientModuleAction.LockVehicle : ClientModuleAction.UnlockVehicle, expectedPlayerEpoch, cancellationToken);

    private static ClientModuleCapabilities CapabilityForAction(ClientModuleAction action) => action switch
    {
        ClientModuleAction.ReviveNative => ClientModuleCapabilities.ReviveNative,
        ClientModuleAction.Suicide => ClientModuleCapabilities.Suicide,
        ClientModuleAction.LockVehicle => ClientModuleCapabilities.LockVehicle,
        ClientModuleAction.UnlockVehicle => ClientModuleCapabilities.UnlockVehicle,
        _ => throw new ArgumentOutOfRangeException(nameof(action), "Operação nativa desconhecida.")
    };

    private static string ActionObservation(ClientModuleAction action) => action switch
    {
        ClientModuleAction.ReviveNative => "Ressurreição nativa observada; estado médico ESX/QBCore não foi limpo.",
        ClientModuleAction.Suicide => "Morte observada pelo módulo em quadro nativo recente.",
        ClientModuleAction.LockVehicle => "Porta nativa do veículo observada como trancada; isso não comprova chaves ou permissões do framework do servidor.",
        ClientModuleAction.UnlockVehicle => "Porta nativa do veículo observada como destrancada; isso não comprova chaves ou permissões do framework do servidor.",
        _ => throw new ArgumentOutOfRangeException(nameof(action), "Operação nativa desconhecida.")
    };

    private async Task ExecuteActionAsync(ClientModuleAction action, long expectedPlayerEpoch, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (Interlocked.CompareExchange(ref _actionPending, 1, 0) != 0)
            throw new InvalidOperationException("Já existe uma ação nativa aguardando observação.");
        long fence = Interlocked.Increment(ref _settingsEpoch);
        Connection? connection = null;
        bool entered = false;
        using var deadline = Deadline(cancellationToken);
        lock (_gate) _actionAbort = deadline;
        try
        {
            await _mutations.WaitAsync(deadline.Token).ConfigureAwait(false); entered = true;
            var prepared = PrepareAction(action, expectedPlayerEpoch);
            connection = prepared.Connection;
            uint ped = prepared.Ped;
            ulong requestedTick = prepared.NativeTick;
            MenuSettings? suicideSettings = prepared.SuicideSettings;
            long settingsRevision = prepared.SettingsRevision;
            if (suicideSettings is not null)
            {
                await SendSettingsAsync(connection, suicideSettings, settingsRevision, deadline.Token, fence, action: true).ConfigureAwait(false);
                await WaitForSettingsAsync(connection, (ulong)settingsRevision, expectedPlayerEpoch, deadline.Token).ConfigureAwait(false);
            }
            ulong nonce;
            await _io.WaitAsync(deadline.Token).ConfigureAwait(false);
            try
            {
                lock (_gate)
                {
                    RequireActionTargetLocked(expectedPlayerEpoch);
                    EnsureConnectionLocked(connection);
                    nonce = checked(++connection.HighestNonce);
                    connection.LastAction = action;
                }
                await ExchangeLockedAsync(connection, ClientModuleKind.Action,
                    ClientModuleProtocol.ActionPayload(connection.Token, nonce, action, ped), deadline.Token).ConfigureAwait(false);
            }
            finally { _io.Release(); }
            while (true)
            {
                ClientModuleStatus? native;
                lock (_gate)
                {
                    RequireActionTargetLocked(expectedPlayerEpoch);
                    EnsureConnectionLocked(connection);
                    native = _native;
                    if (native!.ActionNonce == nonce && native.Action == action)
                    {
                        if (native.ActionResult == ClientModuleActionResult.Failed)
                            throw new InvalidOperationException("A ação nativa falhou: " + native.Message);
                        if (native.ActionResult == ClientModuleActionResult.Observed)
                        {
                            bool alive = native.StateFlags.HasFlag(ClientModuleStateFlags.PedAlive);
                            bool healthResultMatches = action switch
                            {
                                ClientModuleAction.ReviveNative => alive,
                                ClientModuleAction.Suicide => !alive,
                                ClientModuleAction.LockVehicle or ClientModuleAction.UnlockVehicle => true,
                                _ => false
                            };
                            if (native.Ped != ped || native.NativeTick < requestedTick || !healthResultMatches)
                                throw new InvalidDataException("A observação da ação não corresponde ao personagem ou ao resultado solicitado.");
                            AddEventLocked("observed", ActionObservation(action));
                            return;
                        }
                    }
                }
                await Task.Delay(40, deadline.Token).ConfigureAwait(false);
                await PollConnectionAsync(connection, deadline.Token).ConfigureAwait(false);
            }
        }
        catch (Exception error) when (error is IOException or InvalidDataException or InvalidOperationException or ArgumentException or OperationCanceledException or TimeoutException)
        {
            if (connection is not null) AbortConnection(connection, "Ação encerrada sem observação válida; não será reenviada. " + error.Message);
            lock (_gate) AddEventLocked("error", "Ação nativa não confirmada. " + error.Message);
            if (error is OperationCanceledException && !cancellationToken.IsCancellationRequested)
                throw new TimeoutException("O módulo não confirmou a ação em dois segundos; a ação não será repetida.", error);
            throw;
        }
        finally
        {
            lock (_gate) if (ReferenceEquals(_actionAbort, deadline)) _actionAbort = null;
            if (entered) _mutations.Release();
            Interlocked.Exchange(ref _actionPending, 0);
        }
    }

    private (Connection Connection, uint Ped, ulong NativeTick, MenuSettings? SuicideSettings, long SettingsRevision)
        PrepareAction(ClientModuleAction action, long expectedPlayerEpoch)
    {
        // Complete target capture and capability rejection in a synchronous
        // scope, before the action can complete its task or await pipe I/O.
        lock (_gate)
        {
            RequireActionTargetLocked(expectedPlayerEpoch);
            var capability = CapabilityForAction(action);
            if (!_native!.Capabilities.HasFlag(capability)) throw new NotSupportedException("O módulo não anunciou essa operação nativa.");
            MenuSettings? suicideSettings = null;
            long settingsRevision = 0;
            if (action == ClientModuleAction.Suicide)
            {
                // Replace the owned snapshot before dispatch. Queued old GodMode
                // updates are fenced, and no new settings enter during the action.
                suicideSettings = ExternalStateSerialization.Clone(_settings);
                suicideSettings.Player.GodMode = false;
                suicideSettings.Player.Noclip = false;
                _settings = suicideSettings; _hasSettings = true; _nativeParticipated = true;
                settingsRevision = checked(++_revision);
            }
            return (_connection!, _native.Ped, _native.NativeTick, suicideSettings, settingsRevision);
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        Interlocked.Exchange(ref _enabled, 0);
        Interlocked.Increment(ref _settingsEpoch);
        Connection? connection;
        CancellationTokenSource? actionAbort;
        lock (_gate) { connection = _connection; actionAbort = _actionAbort; }
        TryCancel(actionAbort);
        try
        {
            using var deadline = Deadline(cancellationToken);
            if (connection is not null)
            {
                await _io.WaitAsync(deadline.Token).ConfigureAwait(false);
                try { await ExchangeLockedAsync(connection, ClientModuleKind.Disconnect,
                    ClientModuleProtocol.TokenPayload(connection.Token), deadline.Token).ConfigureAwait(false); }
                finally { _io.Release(); }
            }
        }
        catch (Exception error) when (error is IOException or InvalidDataException or InvalidOperationException or ArgumentException or OperationCanceledException) { }
        finally
        {
            AbortConnection(connection, "Conexão local encerrada; restauração nativa solicitada, sem anunciar sua conclusão.");
            await _external.DisconnectAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public string CaptureDiagnostics()
    {
        string external = _external.CaptureDiagnostics();
        object? installation;
        try { installation = _installationDiagnostics?.Invoke(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or Win32Exception)
        { installation = new { Available = false, Message = error.Message }; }
        using var parsed = JsonDocument.Parse(external);
        lock (_gate)
            return ExternalStateSerialization.SerializeDiagnostics(new
            {
                Product = "Zyon optional local client module", Version = AppIdentity.DisplayVersion,
                CapturedUtc = DateTimeOffset.UtcNow, DetectionEnabled, PlayerEpoch = _playerEpoch,
                Installation = installation,
                Configuration = ExternalStateSerialization.Clone(_settings),
                Native = new { ProcessId = _connection?.ProcessId, ConnectionEpoch = _connectionEpoch,
                    State = _native, ObservedUtc = _nativeObservedUtc, ReadyFresh = NativeReadyLocked(),
                    FrameAgeMilliseconds = _native is null ? (long?)null : Environment.TickCount64 - (long)_native.NativeTick,
                    Message = _moduleMessage, DesiredRevision = _revision,
                    Meaning = "Only matching action nonce + native frame observation confirms the requested action. Native revive does not reset server medical state; door observations do not prove framework keys or server permissions." },
                External = parsed.RootElement.Clone(), Events = _events.Select(ExternalStateSerialization.Clone).ToArray()
            });
    }

    private async Task PollLoopAsync()
    {
        try
        {
            while (!_lifetime.IsCancellationRequested)
            {
                if (DetectionEnabled)
                {
                    try
                    {
                        using var deadline = Deadline(_lifetime.Token);
                        var memory = await _external.GetStatusAsync(deadline.Token).ConfigureAwait(false);
                        var events = await _external.GetEventsAsync(_externalCursor, deadline.Token).ConfigureAwait(false);
                        lock (_gate)
                        {
                            _externalPid = memory.PlayerId; _externalReady = memory.Ready;
                            foreach (var item in events.Events.Where(item => item.Id > _externalCursor))
                                AddEventLocked("external/" + item.Type, item.Message);
                            _externalCursor = Math.Max(_externalCursor, events.Cursor);
                        }
                        int? processId = _discoverProcess();
                        Connection? connection;
                        lock (_gate) connection = _connection;
                        if (connection is not null && connection.ProcessId != processId)
                        { AbortConnection(connection, "O processo b3258 mudou ou deixou de ser único; conexão nativa encerrada."); connection = null; }
                        if (processId is not null && connection is null && Environment.TickCount64 - _lastConnectionAttempt >= 1000)
                        {
                            _lastConnectionAttempt = Environment.TickCount64;
                            await OpenConnectionAsync(processId.Value, deadline.Token).ConfigureAwait(false);
                        }
                        else if (connection is not null) await PollConnectionAsync(connection, deadline.Token).ConfigureAwait(false);
                        else lock (_gate) RefreshIdentityLocked();
                    }
                    catch (Exception error) when (error is IOException or InvalidDataException or Win32Exception or InvalidOperationException or ArgumentException or OperationCanceledException or TimeoutException)
                    {
                        if (_lifetime.IsCancellationRequested) break;
                        AbortConnection(null, "Módulo local indisponível: " + error.Message);
                    }
                }
                await Task.Delay(200, _lifetime.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
    }

    private async Task OpenConnectionAsync(int processId, CancellationToken cancellationToken)
    {
        await _io.WaitAsync(cancellationToken).ConfigureAwait(false);
        Connection? candidate = null;
        try
        {
            lock (_gate) if (!DetectionEnabled || _connection is not null) return;
            var stream = await _connect(processId, cancellationToken).ConfigureAwait(false);
            candidate = new Connection(stream, processId, RandomNumberGenerator.GetBytes(16), Interlocked.Increment(ref _connectionEpoch));
            await ClientModuleProtocol.WriteFrameAsync(stream, ClientModuleKind.Hello,
                ClientModuleProtocol.TokenPayload(candidate.Token), cancellationToken).ConfigureAwait(false);
            var state = await ClientModuleProtocol.ReadStatusAsync(stream, candidate.Token, cancellationToken).ConfigureAwait(false);
            ClientModuleProtocol.ValidateResponse(state, 0, 0, ClientModuleAction.None);
            lock (_gate)
            {
                if (!DetectionEnabled || _disposed != 0) return;
                _connection = candidate;
                _nativeParticipated = false;
                Interlocked.Increment(ref _settingsEpoch);
                AcceptStatusLocked(candidate, state);
                AddEventLocked("connected", "Módulo local autenticado no PID do jogo; opções anteriores não são reproduzidas automaticamente.");
                candidate = null;
            }
        }
        finally { candidate?.Dispose(); _io.Release(); }
    }

    private async Task PollConnectionAsync(Connection connection, CancellationToken cancellationToken)
    {
        using var deadline = Deadline(cancellationToken);
        await _io.WaitAsync(deadline.Token).ConfigureAwait(false);
        try { await ExchangeLockedAsync(connection, ClientModuleKind.Poll,
            ClientModuleProtocol.TokenPayload(connection.Token), deadline.Token).ConfigureAwait(false); }
        finally { _io.Release(); }
    }

    private async Task SendSettingsAsync(Connection connection, MenuSettings snapshot, long revision,
        CancellationToken cancellationToken, long fence, bool action = false)
    {
        await _io.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!action) EnsureSettingsFence(fence);
            else if (fence != Interlocked.Read(ref _settingsEpoch)) throw new OperationCanceledException("A sessão da ação mudou.");
            lock (_gate) { EnsureConnectionLocked(connection); connection.HighestRevision = (ulong)revision; }
            await ExchangeLockedAsync(connection, ClientModuleKind.Settings,
                ClientModuleProtocol.SettingsPayload(connection.Token, (ulong)revision, NativeFlags(snapshot), (uint)snapshot.Player.NoclipSpeed, snapshot.Silent),
                cancellationToken).ConfigureAwait(false);
        }
        finally { _io.Release(); }
    }

    private async Task WaitForSettingsAsync(Connection connection, ulong revision, long epoch, CancellationToken cancellationToken)
    {
        while (true)
        {
            lock (_gate)
            {
                RequireActionTargetLocked(epoch); EnsureConnectionLocked(connection);
                if (_native!.AppliedRevision == revision && _native.StateFlags.HasFlag(ClientModuleStateFlags.SettingsActive)) return;
            }
            await Task.Delay(40, cancellationToken).ConfigureAwait(false);
            await PollConnectionAsync(connection, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ExchangeLockedAsync(Connection connection, ClientModuleKind kind, byte[] payload, CancellationToken cancellationToken)
    {
        try
        {
            lock (_gate) EnsureConnectionLocked(connection, allowDisabled: kind == ClientModuleKind.Disconnect);
            await ClientModuleProtocol.WriteFrameAsync(connection.Stream, kind, payload, cancellationToken).ConfigureAwait(false);
            var state = await ClientModuleProtocol.ReadStatusAsync(connection.Stream, connection.Token, cancellationToken).ConfigureAwait(false);
            lock (_gate)
            {
                EnsureConnectionLocked(connection, allowDisabled: kind == ClientModuleKind.Disconnect);
                ClientModuleProtocol.ValidateResponse(state, connection.HighestRevision, connection.HighestNonce, connection.LastAction);
                AcceptStatusLocked(connection, state);
            }
        }
        catch { AbortConnection(connection, "Transporte nativo interrompido; pedidos dessa conexão não serão repetidos."); throw; }
    }

    private void AcceptStatusLocked(Connection connection, ClientModuleStatus status)
    {
        bool fresh = ClientModuleProtocol.IsReadyFresh(status, Environment.TickCount64);
        if (connection.HadReadyFrame && !fresh)
            throw new IOException("A execução nativa deixou de ser confirmada; a conexão foi revogada para não reaplicar efeitos antigos.");
        connection.HadReadyFrame |= fresh;
        bool nonzeroAppliedRegressed = status.AppliedRevision != 0 && status.AppliedRevision < connection.SeenApplied;
        bool zeroClaimsActive = status.AppliedRevision == 0 && status.StateFlags.HasFlag(ClientModuleStateFlags.SettingsActive);
        if (status.NativeTick < connection.SeenTick || status.DesiredRevision < connection.SeenDesired || nonzeroAppliedRegressed || zeroClaimsActive)
            throw new InvalidDataException("O estado nativo regrediu dentro da mesma conexão.");
        connection.SeenTick = status.NativeTick; connection.SeenDesired = status.DesiredRevision;
        connection.SeenApplied = Math.Max(connection.SeenApplied, status.AppliedRevision);
        _native = status;
        long age = Environment.TickCount64 - (long)status.NativeTick;
        _nativeObservedUtc = age is >= 0 and <= 1000 ? DateTimeOffset.UtcNow.AddMilliseconds(-age) : null;
        _moduleMessage = status.Message.Length == 0 ? "Aguardando uma observação nativa recente." : status.Message;
        RefreshIdentityLocked();
    }

    private bool NativeReadyLocked() => DetectionEnabled && _connection is not null && _native is not null &&
        (!_externalReady || _externalPid == _connection.ProcessId) && ClientModuleProtocol.IsReadyFresh(_native, Environment.TickCount64);

    private void RefreshIdentityLocked()
    {
        int? pid = _connection?.ProcessId;
        uint ped = _native?.Ped ?? 0;
        bool ready = NativeReadyLocked();
        if (_identityPid != pid || _identityPed != ped || _identityReady != ready)
        {
            _identityPid = pid; _identityPed = ped; _identityReady = ready;
            Interlocked.Increment(ref _playerEpoch);
        }
    }

    private void RequireActionTargetLocked(long expectedEpoch)
    {
        RefreshIdentityLocked();
        if (!NativeReadyLocked() || expectedEpoch != _playerEpoch)
            throw new InvalidOperationException("O personagem, processo ou quadro nativo mudou desde a solicitação; a ação foi cancelada.");
    }

    private void EnsureConnectionLocked(Connection connection, bool allowDisabled = false)
    {
        if (_disposed != 0 || !allowDisabled && !DetectionEnabled || !ReferenceEquals(_connection, connection))
            throw new InvalidOperationException("A conexão nativa já foi encerrada ou substituída.");
    }

    private void EnsureSettingsFence(long fence)
    {
        if (_disposed != 0 || !DetectionEnabled || fence != Interlocked.Read(ref _settingsEpoch) || Volatile.Read(ref _actionPending) != 0)
            throw new InvalidOperationException("Esta configuração foi substituída por uma ação ou por outra sessão; não foi reaplicada.");
    }

    private void ValidateRequestedNativeLocked(MenuSettings settings, bool ready)
    {
        var requested = NativeFlags(settings);
        if (requested == 0) return;
        if (!ready) throw new InvalidOperationException("O módulo local não confirmou um quadro nativo recente para essas opções.");
        if (((uint)requested & ~(uint)_native!.Capabilities) != 0)
            throw new NotSupportedException("Uma das opções não é suportada pelo módulo conectado.");
    }

    private void AbortConnection(Connection? expected, string message)
    {
        Connection? dispose;
        CancellationTokenSource? actionAbort;
        lock (_gate)
        {
            if (expected is not null && !ReferenceEquals(expected, _connection)) return;
            dispose = _connection; _connection = null; _native = null; _nativeObservedUtc = null;
            _nativeParticipated = false; _hasSettings = false;
            if (_moduleMessage != message) AddEventLocked("module", message);
            _moduleMessage = message;
            if (dispose is not null) Interlocked.Increment(ref _settingsEpoch);
            actionAbort = _actionAbort;
            RefreshIdentityLocked();
        }
        TryCancel(actionAbort);
        dispose?.Dispose();
    }

    private static void TryCancel(CancellationTokenSource? source)
    {
        // Cancellation can complete pipe continuations that acquire _gate.
        // Never wait for those callbacks while holding the session gate.
        try { source?.Cancel(); }
        catch (ObjectDisposedException) { /* The corresponding action already completed. */ }
    }

    private void AddEventLocked(string type, string message)
    {
        _events.Add(new MenuEvent { Id = ++_cursor, Type = type, Message = message });
        if (_events.Count > 200) _events.RemoveAt(0);
    }

    private CancellationTokenSource Deadline(CancellationToken token)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        source.CancelAfter(TimeSpan.FromSeconds(2));
        return source;
    }

    private static MenuSettings EmptySettings()
    {
        var settings = new MenuSettings();
        settings.Aim.OnlyVisible = false; settings.Silent.OnlyVisible = false;
        settings.Esp.Names = false; settings.Esp.OnlyVisible = false;
        return settings;
    }

    private static MenuSettings MemorySettings(MenuSettings settings)
    {
        var shadow = ExternalStateSerialization.Clone(settings);
        shadow.Player.GodMode = false; shadow.Player.Noclip = false; shadow.Player.Invisible = false;
        shadow.Weapons.FreezeAmmo = false;
        shadow.Silent.Enabled = false; shadow.Silent.DrawFov = false;
        return shadow;
    }

    private static bool HasMemoryFeatures(MenuSettings settings) => settings.Aim.Enabled || settings.Aim.DrawFov ||
        settings.Esp.Enabled || settings.Weapons.NoRecoil || settings.Weapons.NoSpread;

    private static ClientModuleSettingsFlags NativeFlags(MenuSettings settings) =>
        (settings.Player.GodMode ? ClientModuleSettingsFlags.God : 0) |
        (settings.Player.Noclip ? ClientModuleSettingsFlags.Noclip : 0) |
        (settings.Player.Invisible ? ClientModuleSettingsFlags.Invisible : 0) |
        (settings.Weapons.FreezeAmmo ? ClientModuleSettingsFlags.FreezeAmmo : 0) |
        (settings.Silent.Enabled ? ClientModuleSettingsFlags.DirectedFireExperimental : 0);

    private static bool IsConfigured(ClientModuleSettingsFlags flag, MenuSettings settings) => NativeFlags(settings).HasFlag(flag);

    private static int? DiscoverProcess()
    {
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess) return null;
        var processes = Process.GetProcessesByName("FiveM_b3258_GTAProcess");
        try { return processes.Length == 1 ? processes[0].Id : null; }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    private static async Task<NamedPipeClientStream> ConnectPipeAsync(int processId, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("O módulo local precisa do Windows.");
        var stream = new NamedPipeClientStream(".", "ZyonClientBridge-v2-" + processId,
            PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, TokenImpersonationLevel.Identification);
        try
        {
            await stream.ConnectAsync(250, cancellationToken).ConfigureAwait(false);
            if (!GetNamedPipeServerProcessId(stream.SafePipeHandle, out uint serverPid) || serverPid != (uint)processId)
                throw new IOException("O processo proprietário do pipe não corresponde ao PID do FiveM.");
            return stream;
        }
        catch { stream.Dispose(); throw; }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Interlocked.Exchange(ref _enabled, 0); Interlocked.Increment(ref _settingsEpoch);
        _lifetime.Cancel(); AbortConnection(null, "Provedor de módulo local encerrado.");
        try { _worker.Wait(TimeSpan.FromSeconds(2)); } catch (AggregateException) { }
        _external.Dispose();
        // A canceled caller may still be releasing a semaphore; no wait handle
        // is allocated for these gates, so disposal must not race those releases.
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint serverProcessId);
}
