// C# / src/ZyonMenu.Windows/LocalGameSession.cs / .NET 8 / Windows 10 x64 FiveM b3258 local backend.
using System.Buffers.Binary;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using ZyonMenu.Core;

namespace ZyonMenu.Windows;

internal sealed record ActorVisual(Vector2?[] Bones, float Distance, bool Player);
internal sealed record OverlayFrame(Rectangle Bounds, bool Foreground, MenuSettings Settings, ActorVisual[] Actors,
    long CapturedTimestamp)
{
    public static OverlayFrame Empty { get; } = new(Rectangle.Empty, false, new(), [], 0);
}

internal sealed class LocalGameSession : ILocalGameSession
{
    private const uint WorldOffset = 0x25B14B0, ReplayOffset = 0x1FBD4F0, ViewOffset = 0x201DBA0;
    private const uint CameraOffset = 0x201E7D0;
    private const uint InvincibleMask = 1u << 8;
    private readonly object _gate = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Task _worker;
    private readonly EspOverlayHost _overlay;
    private readonly List<MenuEvent> _events = [];
    private readonly LocalActionFence _actionFence = new();
    private LocalHealthActionTelemetry _lastHealthAction = LocalHealthActionTelemetry.Empty;
    private long _sessionEpoch;
    private readonly Dictionary<string, FeatureStatus> _featureStates = new(StringComparer.OrdinalIgnoreCase);
    private LocalPedTelemetry _localTelemetry = new();
    private ActorScanTelemetry _actorTelemetry = new();
    private CameraTelemetry _cameraTelemetry = new();
    private WeaponTelemetry _weaponTelemetry = new();
    private SignatureTelemetry _signatureTelemetry = new();
    private GodModeTelemetry _godTelemetry = new();
    private long _tickCount, _aimWriteCount, _weaponWriteCount, _godWriteCount, _healthWriteCount;
    private DateTimeOffset? _lastTickUtc, _lastWindowUtc;
    private Rectangle _lastWindowBounds;
    private bool _lastWindowForeground;
    private GameMemory? _memory;
    // Replace this owned snapshot when settings change; never mutate a published snapshot.
    private MenuSettings _settings = NewDefaults();
    private ServerStatus _status = Waiting("Procurando o processo b3258; escolhas locais ainda não aplicadas.");
    private ulong _ped, _godPed;
    private GameMemory? _godOwner;
    private uint _originalGodBit;
    private string? _godRestoreProblem;
    private long _nextGodRestoreAttempt;
    private ulong _weapon;
    private sealed record WeaponFieldSnapshot(ulong Info, uint Hash, uint OriginalBits, GameMemory Owner, ulong Ped, string Feature);
    private readonly Dictionary<ulong, WeaponFieldSnapshot> _originalWeaponValues = [];
    private readonly Dictionary<ulong, (string Feature, string Reason)> _weaponRestoreProblems = [];
    private readonly Dictionary<ulong, long> _weaponRestoreRetryAt = [];
    private int? _recoilOffset, _spreadOffset;
    private bool _hasSnapshot, _disposed;
    private int _enabled = 1;
    private long _revision, _applied, _cursor;
    private long _lastDiscovery;
    private long _lastAimTimestamp;
    private string? _lastValidationMessage;
    private OverlayFrame _frame = OverlayFrame.Empty;

    public LocalGameSession()
    {
        _overlay = new EspOverlayHost(() => Volatile.Read(ref _frame));
        ResetFeatureStates("Aguardando processo e personagem local validados.");
        PublishFeatureStates();
        _worker = Task.Run(RunAsync);
    }

    public bool DetectionEnabled
    {
        get => Volatile.Read(ref _enabled) != 0;
        set
        {
            Interlocked.Exchange(ref _enabled, value ? 1 : 0);
            if (!value) { _actionFence.Invalidate(); Interlocked.Increment(ref _sessionEpoch); }
        }
    }

    public long PlayerEpoch => Volatile.Read(ref _sessionEpoch);

    public Task<ServerStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() =>
        {
            lock (_gate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                PublishFeatureStates();
                return Clone(_status);
            }
        }, cancellationToken);
    }

    public Task<MenuEvents> GetEventsAsync(long since, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (since < 0) throw new ArgumentOutOfRangeException(nameof(since));
        return Task.Run(() =>
        {
            lock (_gate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new MenuEvents
                {
                    Cursor = _cursor, Events = _events.Where(item => item.Id > since).Select(Clone).ToList()
                };
            }
        }, cancellationToken);
    }

    public string CaptureDiagnostics()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return ExternalStateSerialization.SerializeDiagnostics(new
            {
                Product = "Zyon standalone b3258",
                Version = AppIdentity.DisplayVersion,
                CapturedUtc = DateTimeOffset.UtcNow,
                DetectionEnabled,
                Configuration = Clone(_settings),
                HasConfigurationSnapshot = _hasSnapshot,
                Overlay = new { _overlay.IsRunning, Failure = _overlay.Failure?.Message },
                Runtime = new
                {
                    TickCount = _tickCount, LastTickUtc = _lastTickUtc,
                    Local = _localTelemetry, Actors = _actorTelemetry, Camera = _cameraTelemetry,
                    LastHealthAction = Volatile.Read(ref _lastHealthAction),
                    Weapons = _weaponTelemetry, Signatures = _signatureTelemetry, GodMode = _godTelemetry,
                    Writes = new { AimVectors = _aimWriteCount, WeaponFields = _weaponWriteCount, GodFlags = _godWriteCount, HealthValues = _healthWriteCount },
                    Restoration = new
                    {
                        GodProblem = _godRestoreProblem, GodPed = Hex(_godPed), OriginalGodBit = _originalGodBit,
                        WeaponProblems = _weaponRestoreProblems.Select(item => new { Address = Hex(item.Key), item.Value.Feature, item.Value.Reason }).ToArray()
                    },
                    Window = new { ObservedUtc = _lastWindowUtc, Bounds = _lastWindowBounds, Foreground = _lastWindowForeground },
                    Frame = new
                    {
                        _frame.CapturedTimestamp,
                        AgeMilliseconds = _frame.CapturedTimestamp > 0
                            ? (Stopwatch.GetTimestamp() - _frame.CapturedTimestamp) * 1000d / Stopwatch.Frequency : (double?)null,
                        ActorCount = _frame.Actors.Length
                    },
                    ConfigurationMeaning = "AppliedRevision indica uma configuração processada; FeatureStates registra as observações de cada função."
                },
                Status = Clone(_status),
                Events = _events.Select(Clone).ToArray(),
                Profile = new
                {
                    WorldOffset = $"0x{WorldOffset:X}", ReplayOffset = $"0x{ReplayOffset:X}",
                    ViewOffset = $"0x{ViewOffset:X}", CameraOffset = $"0x{CameraOffset:X}",
                    LocalPed = $"0x{_ped:X}", RecoilFieldOffset = _recoilOffset, SpreadFieldOffset = _spreadOffset,
                    LastValidationMessage = _lastValidationMessage
                },
                Client = ClientDiagnostics.Capture(_memory)
            });
        }
    }

    public Task PushSettingsAsync(MenuSettings settings, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        settings.Validate();
        long settingsGeneration = _actionFence.CaptureSettings();
        // Copy before scheduling: later edits in the menu must not change this request.
        var snapshot = Clone(settings);
        return Task.Run(() =>
        {
            lock (_gate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ObjectDisposedException.ThrowIf(_disposed, this);
                _actionFence.ValidateSettings(settingsGeneration);
                if (!DetectionEnabled || !_status.Ready || _memory is null || !CurrentPedMatches(_ped))
                    throw new InvalidOperationException("Não há personagem local validado para aplicar opções.");
                if (snapshot.Player.Noclip || snapshot.Player.Invisible || snapshot.Player.MuteVoice ||
                    snapshot.Weapons.FreezeAmmo || snapshot.Esp.Names || snapshot.Esp.Admins ||
                    snapshot.Esp.OnlyVisible || snapshot.Aim.OnlyVisible)
                    throw new NotSupportedException("Este perfil não confirmou noclip, invisibilidade, voz, ADMs, munição, nomes/IDs ou oclusão.");
                if (snapshot.Weapons.NoRecoil && _recoilOffset is null || snapshot.Weapons.NoSpread && _spreadOffset is null)
                    throw new NotSupportedException("A assinatura dos dados da arma não foi confirmada no executável.");
                _settings = snapshot;
                _hasSnapshot = true;
                _revision++;
                _status.DesiredRevision = _revision;
                PrepareConfiguredFeatureStates();
                PublishFeatureStates();
                AddEvent("settings", "Configuração recebida; este aceite não confirma execução ou efeito dentro do jogo.");
            }
        }, cancellationToken);
    }

    public Task ReviveAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromException(new NotSupportedException(
            "Ressurreição e estado médico não foram implementados neste backend. Alterar a vida não confirma reviver."));
    }

    public Task ReviveAsync(long expectedPlayerEpoch, CancellationToken cancellationToken = default) =>
        ReviveAsync(cancellationToken);

    public Task SetVehicleLockAsync(bool locked, CancellationToken cancellationToken = default) =>
        SetVehicleLockAsync(locked, PlayerEpoch, cancellationToken);

    public Task SetVehicleLockAsync(bool locked, long expectedPlayerEpoch, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed), this);
        return Task.FromException(new NotSupportedException(
            "Trancar ou destrancar veículo exige a operação nativa confirmada pelo módulo local; o backend externo não altera portas ou chaves."));
    }

    public Task SuicideAsync(CancellationToken cancellationToken = default) => SuicideAsync(PlayerEpoch, cancellationToken);

    public Task SuicideAsync(long expectedPlayerEpoch, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed), this);
        if (expectedPlayerEpoch != PlayerEpoch)
            throw new InvalidOperationException("O personagem mudou após a confirmação; confirme novamente a ação para o personagem atual.");
        long actionId = _actionFence.BeginAction();
        var owner = Volatile.Read(ref _memory);
        ulong ped = Volatile.Read(ref _ped);
        var requested = new LocalHealthActionTelemetry(actionId, "waiting", "Ação de saúde solicitada; aguardando validação do alvo.",
            DateTimeOffset.UtcNow, null, Hex(ped), expectedPlayerEpoch, null, null, false, false);
        Volatile.Write(ref _lastHealthAction, requested);
        return RunSuicideAsync(requested, owner, ped, cancellationToken);
    }

    private async Task RunSuicideAsync(LocalHealthActionTelemetry requested, GameMemory? owner, ulong ped, CancellationToken cancellationToken)
    {
        Task? operation = null;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token, _lifetime.Token);
            CancellationToken token = linked.Token;
            // The delegate always runs its finally block, even when cancellation happened while it was queued.
            operation = Task.Run(() =>
            {
                lock (_gate)
                {
                    var trace = new HealthActionTrace();
                    try
                    {
                        token.ThrowIfCancellationRequested();
                        ObjectDisposedException.ThrowIf(_disposed, this);
                        if (owner is null || ped == 0 || !IsHealthActionTarget(requested, owner, ped))
                            throw new InvalidOperationException("Não há personagem local da solicitação ainda validado para esta ação.");
                        ExternalHealthAction.Execute(new HealthTarget(this, requested, owner, ped), trace, token);
                        Volatile.Write(ref _lastHealthAction, requested with
                        {
                            State = "observed", FinishedUtc = DateTimeOffset.UtcNow, HealthBefore = trace.Before,
                            HealthReadback = trace.Readback, GodRestoreConfirmed = trace.GodRestoreConfirmed,
                            WriteAttempted = trace.WriteAttempted,
                            Message = "Saúde zero observada após uma única escrita externa; morte médica ou estado do servidor não foram confirmados."
                        });
                        AddEvent("health-action", "Saúde zero lida no personagem da solicitação; a ação não será repetida automaticamente.");
                    }
                    catch (Exception error) when (error is IOException or InvalidOperationException or OperationCanceledException)
                    {
                        string reason = error is OperationCanceledException
                            ? deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested
                                ? "O prazo da ação expirou; nenhuma tentativa enfileirada será executada depois do cancelamento."
                                : "A ação foi cancelada; não haverá repetição automática."
                            : error.Message;
                        Volatile.Write(ref _lastHealthAction, requested with
                        {
                            State = "blocked", Message = reason, FinishedUtc = DateTimeOffset.UtcNow,
                            HealthBefore = trace.Before, HealthReadback = trace.Readback,
                            GodRestoreConfirmed = trace.GodRestoreConfirmed, WriteAttempted = trace.WriteAttempted
                        });
                        AddEvent("health-action", reason);
                        throw;
                    }
                    finally
                    {
                        if (_hasSnapshot) CompleteConfiguration();
                        _actionFence.EndAction();
                        PublishFeatureStates();
                    }
                }
            });
            // Observe a late failure even if the caller's deadline has already ended its wait.
            _ = operation.ContinueWith(completed => { _ = completed.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            try { await operation.WaitAsync(token); }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            { throw new TimeoutException("A ação excedeu dois segundos e foi cancelada; não haverá repetição automática."); }
        }
        finally { if (operation is null) _actionFence.EndAction(); }
    }

    private bool IsHealthActionTarget(LocalHealthActionTelemetry requested, GameMemory owner, ulong ped)
    {
        bool ContextMatches() => !_disposed && DetectionEnabled && _actionFence.IsCurrentAction(requested.Id) &&
            requested.SessionEpoch == Volatile.Read(ref _sessionEpoch) && ReferenceEquals(owner, _memory) && _ped == ped;
        // Recheck the cheap guards after Win32 reads so that disconnect/replacement during validation cannot pass.
        return ContextMatches() && owner.IsAlive && CurrentPedMatches(ped) && ContextMatches();
    }

    private sealed class HealthTarget(LocalGameSession session, LocalHealthActionTelemetry requested, GameMemory owner, ulong ped)
        : IExternalHealthTarget
    {
        public bool IsCurrent => session.IsHealthActionTarget(requested, owner, ped);
        public float ReadHealth() => owner.Read<float>(ped + 0x280);
        public void WriteHealth(float value) { owner.Write(ped + 0x280, value); session._healthWriteCount++; }
        public bool DisableOwnedGodMode()
        {
            var snapshot = Clone(session._settings);
            snapshot.Player.GodMode = false;
            session._settings = snapshot;
            session._hasSnapshot = true;
            session._revision++;
            session._status.DesiredRevision = session._revision;
            bool restored = session.RestoreGod();
            session.SetFeature("player.godMode", restored ? "ready" : "blocked", restored
                ? "GodMode do menu desativado e snapshot próprio restaurado para a ação de saúde."
                : "A restauração do GodMode próprio está pendente; a ação de saúde foi bloqueada.");
            return restored;
        }
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DetectionEnabled = false;
        lock (_gate)
        {
            Detach("Detecção interrompida; restauração dos campos modificados solicitada.");
        }
        return Task.CompletedTask;
    }

    private async Task RunAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(16));
        try
        {
            do
            {
                _lifetime.Token.ThrowIfCancellationRequested();
                lock (_gate)
                {
                    try
                    {
                        if (!DetectionEnabled)
                        {
                            if (_memory is not null) Detach("Detecção interrompida.");
                        }
                        else Tick();
                    }
                    catch (Exception error) when (error is IOException or InvalidOperationException or ArgumentException)
                    {
                        var failures = _featureStates.Values.Where(item => item.State == "blocked").ToArray();
                        AddEvent("error", error.Message);
                        Detach("Estado local não confirmado: " + error.Message);
                        foreach (var failure in failures) _featureStates[failure.Name] = failure;
                    }
                    finally { PublishFeatureStates(); }
                }
            }
            while (await timer.WaitForNextTickAsync(_lifetime.Token));
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        finally { lock (_gate) { Detach("Backend local encerrado."); PublishFeatureStates(); } }
    }

    private void Tick()
    {
        _tickCount++;
        _lastTickUtc = DateTimeOffset.UtcNow;
        if (_memory is null || !_memory.IsAlive)
        {
            if (_memory is not null) Detach("O processo do jogo foi encerrado.");
            if (Environment.TickCount64 - _lastDiscovery < 2000) return;
            _lastDiscovery = Environment.TickCount64;
            Discover();
            if (_memory is null) return;
        }
        var memory = _memory;
        long capturedTimestamp = Stopwatch.GetTimestamp();
        if (!TryLocalPed(out var current, out var position, out var health, out var validationMessage))
        {
            if (_ped != 0) ReleasePed();
            _status = Waiting("Processo detectado; " + validationMessage, memory.ProcessId);
            ResetFeatureStates(validationMessage);
            if (_lastValidationMessage != validationMessage)
            {
                _lastValidationMessage = validationMessage;
                AddEvent("validation", validationMessage);
            }
            Volatile.Write(ref _frame, OverlayFrame.Empty);
            return;
        }
        _lastValidationMessage = null;
        if (_ped != current)
        {
            ReleasePed();
            _ped = current;
            AddEvent("ped", "Personagem local validado; nenhuma seleção anterior foi reaplicada.");
        }
        _status = new ServerStatus
        {
            Version = AppIdentity.DisplayVersion + " / b3258", Ready = true, PlayerOnline = true, PlayerId = memory.ProcessId,
            PlayerName = "Processo local b3258", SessionActive = _hasSnapshot && _applied == _revision,
            DesiredRevision = _revision, AppliedRevision = _applied, Capabilities = Capabilities(),
            Message = health <= 0 ? "Personagem sem vida; reviver não foi implementado neste cliente externo."
                : "Personagem validado. Configuração processada não confirma efeito; consulte o estado de cada função."
        };
        PrepareConfiguredFeatureStates();
        if (!_hasSnapshot) { Volatile.Write(ref _frame, OverlayFrame.Empty); return; }
        var settings = _settings;
        if (!CurrentPedMatches(_ped)) throw new InvalidOperationException("O personagem mudou durante a aplicação.");
        ApplyGodMode();
        ApplyWeapons();
        var bounds = GameWindow(memory.ProcessId, out bool foreground);
        _lastWindowUtc = DateTimeOffset.UtcNow;
        _lastWindowBounds = bounds;
        _lastWindowForeground = foreground;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            if (settings.Aim.Enabled) SetFeature("aim", "waiting", "A janela cliente do jogo não foi encontrada.");
            if (settings.Esp.Enabled) SetFeature("esp", "waiting", "A janela cliente do jogo não foi encontrada.");
            Volatile.Write(ref _frame, OverlayFrame.Empty);
            CompleteConfiguration();
            return;
        }
        var actors = settings.Esp.Enabled || settings.Aim.Enabled ? ReadActors(position, bounds) : [];
        if (settings.Aim.Enabled && !_actorTelemetry.ChainRead)
        {
            _lastAimTimestamp = 0;
            SetFeature("aim", "blocked", _actorTelemetry.Failure);
        }
        else if (foreground && health > 0 && settings.Aim.Enabled && KeyPressed(settings.Aim.ActivationKey))
        {
            double elapsedSeconds = _lastAimTimestamp == 0 ? 1d / 30
                : (capturedTimestamp - _lastAimTimestamp) / (double)Stopwatch.Frequency;
            ApplyAim(actors, bounds, elapsedSeconds);
            _lastAimTimestamp = capturedTimestamp;
        }
        else
        {
            _lastAimTimestamp = 0;
            if (settings.Aim.Enabled) SetFeature("aim", "waiting", !foreground ? "O jogo não está em primeiro plano."
                : health <= 0 ? "O personagem local está sem vida." : $"Aguardando a tecla {_settings.Aim.ActivationKey}.");
        }
        Volatile.Write(ref _frame, new OverlayFrame(bounds, foreground, settings,
            actors.Select(item => item.Visual).ToArray(), capturedTimestamp));
        if (settings.Esp.Enabled)
        {
            if (!_actorTelemetry.ChainRead) SetFeature("esp", "blocked", _actorTelemetry.Failure);
            else if (!_overlay.IsRunning) SetFeature("esp", "blocked", "O overlay não está em execução: " + _overlay.Failure?.Message);
            else if (!foreground) SetFeature("esp", "waiting", "O jogo não está em primeiro plano; o overlay fica oculto.");
            else if (!OverlayFrameTiming.IsFresh(capturedTimestamp, Stopwatch.GetTimestamp(), Stopwatch.Frequency, TimeSpan.FromMilliseconds(200)))
                SetFeature("esp", "blocked", "A coleta excedeu 200 ms; o overlay rejeitará este quadro antigo.");
            else if (_actorTelemetry.ActorsEligibleForEsp == 0)
                SetFeature("esp", "waiting", "Nenhum ator projetável passou pelos filtros do ESP neste quadro.");
            else SetFeature("esp", "observed", $"{_actorTelemetry.ActorsEligibleForEsp} ator(es) projetado(s) em um quadro publicado; apresentação visual não confirmada.", true);
        }
        CompleteConfiguration();
    }

    private void Discover()
    {
        var processes = Process.GetProcessesByName("FiveM_b3258_GTAProcess");
        if (processes.Length != 1)
        {
            foreach (var process in processes) process.Dispose();
            _status = Waiting(processes.Length == 0 ? "FiveM_b3258_GTAProcess.exe não encontrado."
                : "Mais de um processo b3258 encontrado; a ligação não foi escolhida automaticamente.");
            return;
        }
        var candidate = processes[0];
        int candidateId = candidate.Id;
        if (!GameMemory.TryOpen(candidate, out var memory, out string error))
        {
            candidate.Dispose();
            _status = Waiting(error, candidateId);
            return;
        }
        _memory = memory!;
        if (_memory.ImageSize <= WorldOffset + sizeof(ulong))
            throw new InvalidOperationException("A imagem do executável não corresponde ao perfil b3258 esperado.");
        _recoilOffset = null;
        _spreadOffset = null;
        ResolveWeaponFields();
        AddEvent("process", "Processo b3258 identificado; validando as estruturas antes de modificar dados.");
    }

    private bool TryLocalPed(out ulong ped, out Vector3 position, out float health) =>
        TryLocalPed(out ped, out position, out health, out _);

    private bool TryLocalPed(out ulong ped, out Vector3 position, out float health, out string reason)
    {
        ped = 0; position = default; health = 0; reason = "";
        _localTelemetry = new LocalPedTelemetry { AttemptUtc = DateTimeOffset.UtcNow };
        var memory = _memory!;
        if (!memory.TryRead(memory.ModuleBase + WorldOffset, out ulong world))
        { reason = $"não foi possível ler o mundo no offset 0x{WorldOffset:X}."; return LocalFailure(reason); }
        _localTelemetry.World = Hex(world);
        if (!Pointer(world))
        { reason = "o ponteiro do mundo ainda não é válido para o perfil b3258."; return LocalFailure(reason); }
        if (!memory.TryRead(world + 8, out ped))
        { reason = "não foi possível ler o ponteiro do personagem local."; return LocalFailure(reason); }
        _localTelemetry.Ped = Hex(ped);
        if (!Pointer(ped))
        { reason = "aguardando a criação do personagem local."; return LocalFailure(reason); }
        bool positionRead = memory.TryRead(ped + 0x90, out position);
        _localTelemetry.Position = positionRead ? DiagnosticVector.From(position) : null;
        if (!positionRead || !Position(position))
        { reason = "a posição do personagem não foi confirmada no perfil b3258."; return LocalFailure(reason); }
        bool healthRead = memory.TryRead(ped + 0x280, out health);
        _localTelemetry.Health = healthRead ? health : null;
        if (!healthRead || !float.IsFinite(health) || health is < -1000 or > 10000)
        { reason = "a leitura de vida do personagem não corresponde ao perfil b3258."; return LocalFailure(reason); }
        if (!memory.TryRead(ped + 0x60, out Matrix4x4 matrix))
        { reason = "não foi possível ler a transformação do personagem."; return LocalFailure(reason); }
        var translation = new Vector3(matrix.M41, matrix.M42, matrix.M43);
        _localTelemetry.TransformTranslation = DiagnosticVector.From(translation);
        _localTelemetry.TranslationDistance = Vector3.Distance(translation, position);
        _localTelemetry.BasisLengthsSquared = [new Vector3(matrix.M11, matrix.M12, matrix.M13).LengthSquared(),
            new Vector3(matrix.M21, matrix.M22, matrix.M23).LengthSquared(), new Vector3(matrix.M31, matrix.M32, matrix.M33).LengthSquared()];
        if (!Position(translation) || Vector3.Distance(translation, position) >= 2 ||
            !ValidBasis(new(matrix.M11, matrix.M12, matrix.M13)) ||
            !ValidBasis(new(matrix.M21, matrix.M22, matrix.M23)) ||
            !ValidBasis(new(matrix.M31, matrix.M32, matrix.M33)))
        { reason = "a base XYZ da transformação do personagem não foi confirmada."; return LocalFailure(reason); }
        // GTA's XYZ transform does not establish a required homogeneous/padding value at M44.
        _localTelemetry.Failure = "";
        return true;
    }

    private bool LocalFailure(string reason) { _localTelemetry.Failure = reason; return false; }

    private static bool ValidBasis(Vector3 value)
    {
        float length = value.LengthSquared();
        return float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z) &&
            float.IsFinite(length) && length is > 0.00000001f and < 100000000f;
    }

    private bool CurrentPedMatches(ulong expected) => _memory is { IsAlive: true } && expected != 0 &&
        TryLocalPed(out ulong current, out _, out _) && current == expected;

    private void ApplyGodMode()
    {
        _godTelemetry = new GodModeTelemetry { AttemptUtc = DateTimeOffset.UtcNow };
        var memory = _memory!;
        if (_settings.Player.GodMode)
        {
            try
            {
                uint flags = memory.Read<uint>(_ped + 0x188);
                _godTelemetry.BeforeFlags = flags;
                if (_godPed != 0 && (_godPed != _ped || !ReferenceEquals(_godOwner, memory)))
                    DiscardGodRestore("A identidade do personagem ou da ligação de memória mudou.");
                if (_godPed != _ped) { _godPed = _ped; _godOwner = memory; _originalGodBit = flags & InvincibleMask; }
                if ((flags & InvincibleMask) == 0)
                {
                    _godTelemetry.WriteAttempted = true;
                    memory.Write(_ped + 0x188, flags | InvincibleMask);
                    _godWriteCount++;
                }
                uint readback = memory.Read<uint>(_ped + 0x188);
                _godTelemetry.ReadbackFlags = readback;
                _godTelemetry.OwnedBitObserved = (readback & InvincibleMask) != 0;
                if (!_godTelemetry.OwnedBitObserved) throw new IOException("O bit de invulnerabilidade não permaneceu aplicado.");
                _godRestoreProblem = null;
                _nextGodRestoreAttempt = 0;
                _godTelemetry.Outcome = "Bit 8 observado no retorno da leitura; invulnerabilidade em gameplay não foi testada.";
                SetFeature("player.godMode", "observed", _godTelemetry.Outcome, true);
            }
            catch (Exception error) when (error is IOException or InvalidOperationException)
            {
                _godTelemetry.Outcome = error.Message;
                SetFeature("player.godMode", "blocked", error.Message);
                throw;
            }
        }
        else
        {
            bool restored = RestoreGod();
            _godTelemetry.Outcome = restored ? "Desativado; nenhum snapshot de restauração ficou pendente."
                : "Desativado, mas a restauração do bit anterior não foi confirmada.";
            SetFeature("player.godMode", restored ? "ready" : "blocked", _godTelemetry.Outcome);
        }
    }

    private bool RestoreGod()
    {
        if (_godPed == 0) return true;
        if (_memory is null) return GodRestoreFailed("A ligação do processo não está disponível para confirmar a restauração.");
        if (!ReferenceEquals(_godOwner, _memory) || !_memory.IsAlive)
        { DiscardGodRestore("A ligação de memória foi substituída ou o processo foi encerrado; o endereço anterior não será escrito."); return true; }
        if (TryLocalPed(out ulong current, out _, out _) && current != _godPed)
        { DiscardGodRestore("Outro personagem local foi validado; o snapshot do personagem anterior foi descartado."); return true; }
        if (!CurrentPedMatches(_godPed)) return GodRestoreFailed("O personagem proprietário do bit não pôde ser confirmado; nenhuma escrita de restauração foi feita.");
        if (!_memory.TryRead(_godPed + 0x188, out uint flags)) return GodRestoreFailed("As flags anteriores não puderam ser lidas para restauração.");
        uint restored = (flags & ~InvincibleMask) | _originalGodBit;
        if (restored != flags)
        {
            if (Environment.TickCount64 < _nextGodRestoreAttempt) return false;
            if (!_memory.TryWrite(_godPed + 0x188, restored)) return GodRestoreFailed("A escrita de restauração do bit falhou; snapshot preservado.");
            _godWriteCount++;
        }
        if (!_memory.TryRead(_godPed + 0x188, out uint readback) || (readback & InvincibleMask) != _originalGodBit)
            return GodRestoreFailed("O retorno da restauração do bit não confirmou o valor original; snapshot preservado.");
        _godPed = 0;
        _godOwner = null;
        _originalGodBit = 0;
        _godRestoreProblem = null;
        _nextGodRestoreAttempt = 0;
        return true;
    }

    private bool GodRestoreFailed(string reason)
    {
        if (_godRestoreProblem != reason) AddEvent("restore", reason);
        _godRestoreProblem = reason;
        _nextGodRestoreAttempt = Environment.TickCount64 + 2000;
        return false;
    }

    private void DiscardGodRestore(string reason)
    {
        AddEvent("restore-discard", "GodMode: " + reason);
        _godPed = 0; _godOwner = null; _originalGodBit = 0; _godRestoreProblem = null; _nextGodRestoreAttempt = 0;
    }

    private void ApplyWeapons()
    {
        var memory = _memory!;
        _weaponTelemetry = new WeaponTelemetry
        {
            AttemptUtc = DateTimeOffset.UtcNow,
            Recoil = new WeaponFieldTelemetry { Offset = _recoilOffset },
            Spread = new WeaponFieldTelemetry { Offset = _spreadOffset }
        };
        ulong current = 0;
        string missingState = "waiting", missingReason = "Nenhuma arma foi identificada.";
        if (!memory.TryRead(_ped + 0x10B8, out ulong manager))
        { missingState = "blocked"; missingReason = "Falha ao ler ped +0x10B8 (gerenciador da arma)."; }
        else
        {
            _weaponTelemetry.Manager = Hex(manager);
            if (Pointer(manager))
            {
                if (!memory.TryRead(manager + 0x20, out current))
                { missingState = "blocked"; missingReason = "Falha ao ler gerenciador +0x20 (dados da arma)."; }
                else if (current != 0 && !Pointer(current))
                { missingState = "blocked"; missingReason = "O ponteiro de dados da arma está fora do intervalo permitido."; }
            }
            else if (manager != 0)
            { missingState = "blocked"; missingReason = "O gerenciador da arma está fora do intervalo permitido."; }
        }
        _weaponTelemetry.Info = Hex(current);
        if (!Pointer(current)) current = 0;
        if (current != _weapon) { RestoreWeapons(); _weapon = current; }
        if (_weapon == 0) { WeaponUnavailable(missingState, missingReason); return; }
        if (!memory.TryRead(_weapon + 0x10, out uint hash))
        { WeaponUnavailable("blocked", "O hash dos dados da arma em +0x10 não pôde ser lido."); return; }
        _weaponTelemetry.Hash = hash;
        if (hash == 0) { WeaponUnavailable("waiting", "Os dados da arma ainda não contêm um hash válido."); return; }
        _weaponTelemetry.Outcome = "Cadeia e hash lidos; cada campo possui verificação independente.";
        ApplyWeaponValue("weapons.noRecoil", _recoilOffset, _settings.Weapons.NoRecoil, hash, _weaponTelemetry.Recoil);
        ApplyWeaponValue("weapons.noSpread", _spreadOffset, _settings.Weapons.NoSpread, hash, _weaponTelemetry.Spread);
        RetryPendingWeaponRestores();
    }

    private void WeaponUnavailable(string state, string reason)
    {
        _weaponTelemetry.Outcome = reason;
        if (_settings.Weapons.NoRecoil && _recoilOffset is not null) SetFeature("weapons.noRecoil", state, reason);
        if (_settings.Weapons.NoSpread && _spreadOffset is not null) SetFeature("weapons.noSpread", state, reason);
        // Turning an option off still owns its earlier write even if the current weapon chain cannot be read.
        // Start the checked restoration path so that the missing hash becomes a persistent blocked observation.
        RetryPendingWeaponRestores(includeDisabled: true);
    }

    private void ApplyWeaponValue(string feature, int? offset, bool enabled, uint expectedHash, WeaponFieldTelemetry telemetry)
    {
        if (offset is null) return;
        ulong address = _weapon + (uint)offset.Value;
        telemetry.Address = Hex(address);
        var memory = _memory!;
        if (!enabled)
        {
            bool restored = !_originalWeaponValues.TryGetValue(address, out var snapshot) || RestoreWeaponField(address, snapshot);
            telemetry.Outcome = restored ? "Desativado; nenhum valor zero foi solicitado neste ciclo."
                : "Restauração pendente; o snapshot original foi preservado.";
            if (!restored) SetFeature(feature, "blocked", _weaponRestoreProblems[address].Reason);
            return;
        }
        try
        {
            uint bits = memory.Read<uint>(address);
            telemetry.BeforeBits = bits;
            float value = BitConverter.UInt32BitsToSingle(bits);
            if (!float.IsFinite(value) || value is < 0 or > 100) throw new InvalidOperationException("O campo da arma saiu do intervalo válido.");
            uint hash = memory.Read<uint>(_weapon + 0x10);
            if (hash != expectedHash || _originalWeaponValues.TryGetValue(address, out var prior) &&
                (prior.Info != _weapon || prior.Hash != hash))
                throw new InvalidOperationException("A identidade dos dados da arma mudou durante a aplicação.");
            if (!_originalWeaponValues.ContainsKey(address) || bits != 0)
                _originalWeaponValues[address] = new WeaponFieldSnapshot(_weapon, hash, bits, memory, _ped, feature);
            telemetry.OriginalBits = _originalWeaponValues[address].OriginalBits;
            if (bits != 0)
            {
                if (!CurrentPedMatches(_ped)) throw new InvalidOperationException("O personagem mudou antes da escrita nos dados da arma.");
                telemetry.WriteAttempted = true;
                memory.Write(address, 0u);
                _weaponWriteCount++;
            }
            uint readback = memory.Read<uint>(address);
            telemetry.ReadbackBits = readback;
            telemetry.ReadbackMatched = readback == 0 && memory.TryRead(_weapon + 0x10, out uint readbackHash) && readbackHash == expectedHash;
            if (!telemetry.ReadbackMatched) throw new IOException("O valor zero ou a identidade da arma não foi confirmado no retorno da leitura.");
            _weaponRestoreProblems.Remove(address);
            _weaponRestoreRetryAt.Remove(address);
            telemetry.ObservedUtc = DateTimeOffset.UtcNow;
            telemetry.Outcome = "Bits zero e hash da arma observados no retorno; efeito nos disparos não foi testado.";
            SetFeature(feature, "observed", telemetry.Outcome, true);
        }
        catch (Exception error) when (error is IOException or InvalidOperationException)
        {
            telemetry.Outcome = error.Message;
            SetFeature(feature, "blocked", error.Message);
            throw;
        }
    }

    private bool RestoreWeaponField(ulong address, WeaponFieldSnapshot snapshot)
    {
        var memory = _memory;
        if (memory is null) return WeaponRestoreFailed(address, snapshot, "A ligação do processo não está disponível; snapshot preservado.");
        if (!ReferenceEquals(snapshot.Owner, memory) || !memory.IsAlive)
        { ForgetWeaponRestore(address, "A ligação de memória mudou ou o processo foi encerrado; nenhum endereço anterior foi escrito."); return true; }
        if (TryLocalPed(out ulong currentPed, out _, out _) && currentPed != snapshot.Ped)
        { ForgetWeaponRestore(address, "Outro personagem local foi validado; o snapshot anterior foi descartado."); return true; }
        if (!CurrentPedMatches(snapshot.Ped)) return WeaponRestoreFailed(address, snapshot, "O personagem proprietário da arma não pôde ser confirmado; nenhuma escrita foi feita.");
        if (!memory.TryRead(snapshot.Info + 0x10, out uint hash))
            return WeaponRestoreFailed(address, snapshot, "O hash da arma não pôde ser lido; snapshot preservado sem escrita.");
        if (hash == 0)
            return WeaponRestoreFailed(address, snapshot, "O hash da arma ainda é zero; a identidade não foi comprovada e o snapshot foi preservado sem escrita.");
        if (hash != snapshot.Hash)
        { ForgetWeaponRestore(address, "O hash da arma mudou; o endereço anterior não pertence mais ao snapshot."); return true; }
        if (!memory.TryRead(address, out uint current))
            return WeaponRestoreFailed(address, snapshot, "O campo da arma não pôde ser lido para restauração.");
        if (current == snapshot.OriginalBits) { ForgetWeaponRestore(address); return true; }
        if (current != 0)
        {
            ForgetWeaponRestore(address, "O campo recebeu um valor externo diferente do zero escrito pelo cliente; não será sobrescrito.");
            return true;
        }
        if (_weaponRestoreRetryAt.TryGetValue(address, out long retryAt) && Environment.TickCount64 < retryAt) return false;
        bool written = memory.TryWrite(address, snapshot.OriginalBits);
        if (written) _weaponWriteCount++;
        if (!written || !memory.TryRead(address, out uint restored) || restored != snapshot.OriginalBits ||
            !memory.TryRead(snapshot.Info + 0x10, out uint afterHash) || afterHash != snapshot.Hash)
            return WeaponRestoreFailed(address, snapshot, "A restauração ou a identidade da arma não foi confirmada no retorno; snapshot preservado.");
        ForgetWeaponRestore(address);
        return true;
    }

    private bool WeaponRestoreFailed(ulong address, WeaponFieldSnapshot snapshot, string reason)
    {
        if (!_weaponRestoreProblems.TryGetValue(address, out var previous) || previous.Reason != reason) AddEvent("restore", reason);
        _weaponRestoreProblems[address] = (snapshot.Feature, reason);
        _weaponRestoreRetryAt[address] = Environment.TickCount64 + 2000;
        return false;
    }

    private void ForgetWeaponRestore(ulong address, string? reason = null)
    {
        if (reason is not null) AddEvent("restore-discard", $"Arma em {Hex(address)}: {reason}");
        _originalWeaponValues.Remove(address); _weaponRestoreProblems.Remove(address); _weaponRestoreRetryAt.Remove(address);
    }

    private void RetryPendingWeaponRestores(bool includeDisabled = false)
    {
        long now = Environment.TickCount64;
        foreach (var (address, snapshot) in _originalWeaponValues.ToArray())
        {
            bool disabled = includeDisabled && (snapshot.Feature == "weapons.noRecoil" && !_settings.Weapons.NoRecoil ||
                snapshot.Feature == "weapons.noSpread" && !_settings.Weapons.NoSpread);
            bool hasRetry = _weaponRestoreRetryAt.TryGetValue(address, out long retryAt);
            if ((disabled || hasRetry) && (!hasRetry || now >= retryAt))
                RestoreWeaponField(address, snapshot);
        }
    }

    private void RestoreWeapons()
    {
        foreach (var (address, snapshot) in _originalWeaponValues.ToArray()) RestoreWeaponField(address, snapshot);
        _weapon = 0;
    }

    private sealed record TargetActor(ulong Address, Vector3[] WorldBones, Vector3 Torso, Vector2? TorsoScreen, ActorVisual Visual);

    private TargetActor[] ReadActors(Vector3 localPosition, Rectangle bounds)
    {
        var memory = _memory!;
        _actorTelemetry = new ActorScanTelemetry { AttemptUtc = DateTimeOffset.UtcNow };
        if (!memory.TryRead(memory.ModuleBase + ViewOffset, out ulong viewport))
            return ActorFailure($"Falha ao ler módulo +0x{ViewOffset:X} (viewport).");
        _actorTelemetry.Viewport = Hex(viewport);
        if (!Pointer(viewport)) return ActorFailure("O ponteiro do viewport não foi validado.");
        if (!memory.TryRead(viewport + 0x24C, out Matrix4x4 view))
            return ActorFailure("Falha ao ler viewport +0x24C (matriz de projeção).");
        _actorTelemetry.ViewMatrix = MatrixValues(view);
        if (!ExternalReadback.ProjectionCoefficientsValid(view))
            return ActorFailure("A matriz lida contém valores não finitos ou W inteiramente zero; não é ausência de alvos.");
        if (!memory.TryRead(memory.ModuleBase + ReplayOffset, out ulong replay))
            return ActorFailure($"Falha ao ler módulo +0x{ReplayOffset:X} (replay).");
        _actorTelemetry.Replay = Hex(replay);
        if (!Pointer(replay)) return ActorFailure("O ponteiro do replay não foi validado.");
        if (!memory.TryRead(replay + 0x18, out ulong pedInterface))
            return ActorFailure("Falha ao ler replay +0x18 (interface de personagens).");
        _actorTelemetry.PedInterface = Hex(pedInterface);
        if (!Pointer(pedInterface)) return ActorFailure("O ponteiro da interface de personagens não foi validado.");
        if (!memory.TryRead(pedInterface + 0x100, out ulong list))
            return ActorFailure("Falha ao ler interface +0x100 (lista de personagens).");
        _actorTelemetry.List = Hex(list);
        if (!Pointer(list)) return ActorFailure("O ponteiro da lista de personagens não foi validado.");
        _actorTelemetry.ChainRead = true;
        _actorTelemetry.Failure = "";
        var output = new List<TargetActor>();
        for (int index = 0; index < 256 && output.Count < 64; index++)
        {
            _actorTelemetry.SlotsSampled++;
            if (!memory.TryRead(list + (ulong)index * 16, out ulong ped)) { _actorTelemetry.SlotReadFailures++; continue; }
            if (!Pointer(ped)) { _actorTelemetry.InvalidPointers++; continue; }
            if (ped == _ped) { _actorTelemetry.LocalPedSkipped++; continue; }
            if (!memory.TryRead(ped + 0x280, out float health) || !float.IsFinite(health) || health is <= 0 or > 10000)
            { _actorTelemetry.HealthRejected++; continue; }
            if (!memory.TryRead(ped + 0x90, out Vector3 position) || !Position(position))
            { _actorTelemetry.PositionRejected++; continue; }
            float distance = Vector3.Distance(localPosition, position);
            if (!float.IsFinite(distance) || distance > Math.Max(_settings.Esp.MaxDistance, 250))
            { _actorTelemetry.DistanceFiltered++; continue; }
            bool player = memory.TryRead(ped + 0x10A8, out ulong info) && Pointer(info) && memory.IsReadableRange(info, 8);
            if ((!_settings.Aim.Enabled || _settings.Aim.OnlyPlayers) && (!_settings.Esp.Enabled || _settings.Esp.OnlyPlayers) && !player)
            { _actorTelemetry.PlayerFiltered++; continue; }
            if (!memory.TryRead(ped + 0x60, out Matrix4x4 transform)) { _actorTelemetry.TransformReadFailures++; continue; }
            if (!memory.TryReadBytes(ped + 0x410, 140, out var rows)) { _actorTelemetry.BoneReadFailures++; continue; }
            var bones = new Vector3[9];
            var projected = new Vector2?[9];
            bool valid = true;
            int projectedCount = 0;
            for (int bone = 0; bone < 9; bone++)
            {
                var relative = new Vector3(BitConverter.ToSingle(rows, bone * 16), BitConverter.ToSingle(rows, bone * 16 + 4), BitConverter.ToSingle(rows, bone * 16 + 8));
                if (!Position(relative)) { valid = false; break; }
                try { bones[bone] = ProjectionMath.TransformBone(relative, transform); }
                catch (ArgumentException) { valid = false; break; }
                if (Vector3.Distance(position, bones[bone]) > 10)
                { valid = false; break; }
                if (ProjectionMath.TryProject(bones[bone], view, bounds.Width, bounds.Height, out var point))
                {
                    projected[bone] = point;
                    projectedCount++;
                    _actorTelemetry.BonesProjected++;
                }
                else _actorTelemetry.BoneProjectionFailures++;
            }
            if (!valid) { _actorTelemetry.BoneValidationFailures++; continue; }
            if (projectedCount == 0) continue;
            var torso = Vector3.Lerp(bones[8], bones[7], 0.65f);
            Vector2? torsoScreen = ProjectionMath.TryProject(torso, view, bounds.Width, bounds.Height, out var torsoPoint)
                ? torsoPoint : null;
            output.Add(new TargetActor(ped, bones, torso, torsoScreen, new ActorVisual(projected, distance, player)));
            if (distance <= _settings.Esp.MaxDistance && (!_settings.Esp.OnlyPlayers || player))
                _actorTelemetry.ActorsEligibleForEsp++;
        }
        _actorTelemetry.ActorsProjected = output.Count;
        _actorTelemetry.SampleLimitReached = output.Count == 64;
        return output.ToArray();
    }

    private TargetActor[] ActorFailure(string reason) { _actorTelemetry.Failure = reason; return []; }

    private void ApplyAim(TargetActor[] actors, Rectangle bounds, double elapsedSeconds)
    {
        _cameraTelemetry = new CameraTelemetry { AttemptUtc = DateTimeOffset.UtcNow, TargetBone = _settings.Aim.TargetBone };
        var center = new Vector2(bounds.Width / 2f, bounds.Height / 2f);
        int index = _settings.Aim.TargetBone == "Head" ? 0 : 7;
        bool torsoTarget = _settings.Aim.TargetBone == "Chest";
        Vector2? AimPoint(TargetActor item) => torsoTarget ? item.TorsoScreen : item.Visual.Bones[index];
        TargetActor? nearest = actors.Where(item => !_settings.Aim.OnlyPlayers || item.Visual.Player)
            .Where(item => AimPoint(item) is { } point && ProjectionMath.TargetDistanceWithinFov(point, center, _settings.Aim.Fov))
            .OrderBy(item => Vector2.DistanceSquared(AimPoint(item)!.Value, center)).FirstOrDefault();
        if (nearest is null)
        {
            _cameraTelemetry.Outcome = "Nenhum alvo projetável passou pelo FOV e pelos filtros; nenhuma escrita foi feita.";
            SetFeature("aim", "waiting", _cameraTelemetry.Outcome);
            return;
        }
        Vector3 targetPosition = torsoTarget ? nearest.Torso : nearest.WorldBones[index];
        _cameraTelemetry.Target = Hex(nearest.Address);
        _cameraTelemetry.TargetPosition = DiagnosticVector.From(targetPosition);
        _cameraTelemetry.TargetDistanceFromCrosshair = Vector2.Distance(AimPoint(nearest)!.Value, center);
        var memory = _memory!;
        if (!memory.TryRead(memory.ModuleBase + CameraOffset, out ulong camera))
        { AimBlocked($"Falha ao ler módulo +0x{CameraOffset:X} (câmera)."); return; }
        _cameraTelemetry.Camera = Hex(camera);
        if (!Pointer(camera)) { AimBlocked("O ponteiro da câmera não foi validado."); return; }
        if (!memory.TryRead(camera + 0x60, out Vector3 eye)) { AimBlocked("Falha ao ler câmera +0x60 (posição candidata)."); return; }
        _cameraTelemetry.Eye = DiagnosticVector.From(eye);
        if (!Position(eye)) { AimBlocked("A posição candidata da câmera não é finita ou está fora do perfil."); return; }
        if (!memory.TryRead(camera + 0x40, out Vector3 forward)) { AimBlocked("Falha ao ler câmera +0x40 (direção candidata)."); return; }
        _cameraTelemetry.Forward = DiagnosticVector.From(forward);
        if (!Position(forward)) { AimBlocked("A direção candidata da câmera não é finita ou está fora do perfil."); return; }
        float length = forward.Length();
        _cameraTelemetry.ForwardLength = length;
        Vector3 delta = targetPosition - eye;
        _cameraTelemetry.TargetDeltaLengthSquared = delta.LengthSquared();
        if (!float.IsFinite(length) || length is < 0.5f or > 1.5f)
        { AimBlocked("O comprimento da direção candidata não corresponde ao intervalo 0,5–1,5; o layout precisa de evidência do cliente."); return; }
        if (!float.IsFinite(delta.LengthSquared()) || delta.LengthSquared() < 0.0001f)
        { AimBlocked("O vetor entre a câmera candidata e o alvo é inválido ou quase zero."); return; }
        var direction = Vector3.Normalize(Vector3.Lerp(forward, Vector3.Normalize(delta),
            AimTiming.BlendFactor(_settings.Aim.Smooth, elapsedSeconds)));
        _cameraTelemetry.RequestedDirection = DiagnosticVector.From(direction);
        if (!Position(direction)) { AimBlocked("A direção calculada não passou pela validação numérica."); return; }
        if (!CurrentPedMatches(_ped)) { AimBlocked("O personagem mudou antes da tentativa de aim."); return; }
        if (!memory.TryRead(memory.ModuleBase + CameraOffset, out ulong cameraBeforeWrite) || cameraBeforeWrite != camera)
        { AimBlocked("O ponteiro da câmera mudou antes da escrita; nenhuma escrita foi feita."); return; }
        try
        {
            _cameraTelemetry.WriteAttempted = true;
            memory.Write(camera + 0x40, direction);
            _aimWriteCount++;
            memory.Write(camera + 0x3D0, direction);
            _aimWriteCount++;
            bool primaryRead = memory.TryRead(camera + 0x40, out Vector3 primary);
            bool secondaryRead = memory.TryRead(camera + 0x3D0, out Vector3 secondary);
            if (primaryRead) _cameraTelemetry.PrimaryReadback = DiagnosticVector.From(primary);
            if (secondaryRead) _cameraTelemetry.SecondaryReadback = DiagnosticVector.From(secondary);
            if (!primaryRead || !secondaryRead)
            { AimBlocked("A leitura posterior dos dois vetores gravados falhou."); return; }
            _cameraTelemetry.ReadbackMatched = ExternalReadback.DirectionMatches(direction, primary) &&
                ExternalReadback.DirectionMatches(direction, secondary) &&
                memory.TryRead(memory.ModuleBase + CameraOffset, out ulong sameCamera) && sameCamera == camera && CurrentPedMatches(_ped);
            if (!_cameraTelemetry.ReadbackMatched)
            { AimBlocked("Os vetores gravados ou a identidade da câmera/personagem mudaram no retorno; efeito não confirmado."); return; }
            _cameraTelemetry.Outcome = "Dois vetores observados no retorno da escrita; movimento da câmera em gameplay não foi comprovado.";
            SetFeature("aim", "observed", _cameraTelemetry.Outcome, true);
        }
        catch (Exception error) when (error is IOException or InvalidOperationException)
        {
            AimBlocked(error.Message);
            throw;
        }
    }

    private void AimBlocked(string reason) { _cameraTelemetry.Outcome = reason; SetFeature("aim", "blocked", reason); }

    private void ResolveWeaponFields()
    {
        _signatureTelemetry = new SignatureTelemetry();
        var memory = _memory!;
        if (!memory.TryReadBytes(memory.ModuleBase, 4096, out var header))
        { SignatureFailure("O cabeçalho do executável não pôde ser lido."); return; }
        if (header[0] != 'M' || header[1] != 'Z') { SignatureFailure("O cabeçalho MZ do executável não foi confirmado."); return; }
        int pe = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(0x3C, 4));
        if (pe < 0 || pe > 3000 || header[pe] != 'P' || header[pe + 1] != 'E')
        { SignatureFailure("O cabeçalho PE do executável não foi confirmado."); return; }
        int count = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(pe + 6, 2));
        int table = pe + 24 + BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(pe + 20, 2));
        var recoilPattern = BytePattern.Parse("48 8B 47 40 F3 0F 10 B0 ?? ?? ?? ??");
        var spreadPattern = BytePattern.Parse("F3 0F 10 43 ?? F3 0F 59 05 ?? ?? ?? ?? F3 0F 2C C0");
        var recoil = new List<int>(); var spread = new List<int>();
        for (int section = 0; section < count && section < 96; section++)
        {
            int start = table + section * 40;
            if (start > header.Length - 40) { SignatureFailure("A tabela de seções ultrapassa o cabeçalho disponível."); return; }
            uint flags = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(start + 36, 4));
            if ((flags & 0x20000000) == 0) continue;
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(start + 8, 4));
            uint rva = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(start + 12, 4));
            if (size == 0 || size > 32 * 1024 * 1024 || rva > memory.ImageSize || size > memory.ImageSize - rva ||
                !memory.TryReadBytes(memory.ModuleBase + rva, (int)size, out var code))
            { _signatureTelemetry.ExecutableSectionsSkipped++; continue; }
            _signatureTelemetry.ExecutableSectionsRead++;
            foreach (int match in recoilPattern.FindAll(code)) recoil.Add(BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(match + 8, 4)));
            foreach (int match in spreadPattern.FindAll(code)) spread.Add(ExternalReadback.SignedDisplacement8(code[match + 4]));
        }
        _signatureTelemetry.RecoilMatchCount = recoil.Count;
        _signatureTelemetry.SpreadMatchCount = spread.Count;
        _signatureTelemetry.RecoilCandidates = recoil.Take(16).ToList();
        _signatureTelemetry.SpreadCandidates = spread.Take(16).ToList();
        _recoilOffset = ExternalReadback.UniqueFieldOffset(recoil);
        _spreadOffset = ExternalReadback.UniqueFieldOffset(spread);
        _signatureTelemetry.RecoilReason = SignatureReason(recoil, _recoilOffset);
        _signatureTelemetry.SpreadReason = SignatureReason(spread, _spreadOffset);
    }

    private void SignatureFailure(string reason)
    {
        _signatureTelemetry.RecoilReason = reason;
        _signatureTelemetry.SpreadReason = reason;
    }

    private string SignatureReason(List<int> candidates, int? offset) => offset is not null
        ? $"Assinatura única; deslocamento 0x{offset:X} resolvido no executável."
        : candidates.Count > 1 ? $"Assinatura ambígua: {candidates.Count} ocorrências; campo desativado."
        : candidates.Count == 1 ? "O deslocamento extraído está fora do intervalo permitido; campo desativado."
        : $"Assinatura ausente nas {_signatureTelemetry.ExecutableSectionsRead} seções executáveis lidas; {_signatureTelemetry.ExecutableSectionsSkipped} seções não puderam ser analisadas.";

    private void SetFeature(string name, string state, string message, bool observed = false)
    {
        if (!observed && _featureStates.TryGetValue(name, out var existing) && existing.State == state && existing.Message == message &&
            existing.ObservedUtc is null) return;
        _featureStates[name] = new FeatureStatus
        {
            Name = name, State = state, Message = message,
            ObservedUtc = observed ? DateTimeOffset.UtcNow : null
        };
    }

    private void PublishFeatureStates()
    {
        if (_godRestoreProblem is not null) SetFeature("player.godMode", "blocked", _godRestoreProblem);
        foreach (var problem in _weaponRestoreProblems.Values) SetFeature(problem.Feature, "blocked", problem.Reason);
        RefreshSuicideFeatureState();
        _status.FeatureStates = _featureStates.Values.ToList();
    }

    private void ResetFeatureStates(string reason)
    {
        foreach (string feature in new[] { "aim", "esp", "player.godMode", "weapons.noRecoil", "weapons.noSpread" })
            SetFeature(feature, "waiting", reason);
        SetUnsupportedFeatureStates();
    }

    private void PrepareConfiguredFeatureStates()
    {
        PrepareFeature("aim", _settings.Aim.Enabled);
        PrepareFeature("esp", _settings.Esp.Enabled);
        PrepareFeature("player.godMode", _settings.Player.GodMode);
        if (_recoilOffset is null) SetFeature("weapons.noRecoil", "blocked", _signatureTelemetry.RecoilReason);
        else PrepareFeature("weapons.noRecoil", _settings.Weapons.NoRecoil);
        if (_spreadOffset is null) SetFeature("weapons.noSpread", "blocked", _signatureTelemetry.SpreadReason);
        else PrepareFeature("weapons.noSpread", _settings.Weapons.NoSpread);
        SetUnsupportedFeatureStates();
    }

    private void PrepareFeature(string name, bool enabled) => SetFeature(name, _hasSnapshot && enabled ? "waiting" : "ready",
        !_hasSnapshot ? "Disponível neste perfil; nenhuma configuração foi processada para o personagem atual."
        : enabled ? "Configurado; aguardando as pré-condições e observações deste ciclo." : "Desativado na configuração recebida.");

    private void SetUnsupportedFeatureStates()
    {
        SetFeature("weapons.freezeAmmo", "unsupported", "Nenhuma cadeia de munição foi validada neste cliente externo.");
        SetFeature("player.noclip", "unsupported", "Movimento e colisão de noclip não possuem implementação externa validada.");
        SetFeature("player.revive", "unsupported", "Ressurreição e estado médico não foram implementados; editar vida não confirma reviver.");
        SetFeature("silent", "unsupported", "Não há implementação validada de Silent Aim; mover a câmera não equivale a essa função.");
        SetFeature("player.invisible", "unsupported", "Não há campo ou operação de invisibilidade validado neste cliente externo.");
        SetFeature("player.muteVoice", "unsupported", "Não há operação de voz validada neste cliente externo.");
        SetFeature("esp.admins", "unsupported", "Não há fonte externa validada para o papel administrativo dos jogadores.");
        SetFeature("esp.names", "unsupported", "A associação de nomes/IDs aos personagens não foi validada.");
        SetFeature("esp.visibility", "unsupported", "Não há teste de linha de visão validado neste perfil.");
        SetFeature("aim.visibility", "unsupported", "Não há teste de linha de visão validado neste perfil.");
    }

    private void RefreshSuicideFeatureState()
    {
        var action = Volatile.Read(ref _lastHealthAction);
        if (action.Id != 0 && action.SessionEpoch == Volatile.Read(ref _sessionEpoch))
        {
            _featureStates["player.suicide"] = new FeatureStatus
            {
                Name = "player.suicide", State = action.State, Message = action.Message,
                ObservedUtc = action.State == "observed" ? action.FinishedUtc : null
            };
        }
        else SetFeature("player.suicide", _status.Ready ? "ready" : "waiting", _status.Ready
            ? "Uma solicitação pode zerar a saúde local com leitura de retorno; o estado médico do servidor pode sobrescrever o resultado."
            : "Aguardando processo e personagem local validados.");
    }

    private void CompleteConfiguration()
    {
        // This is an acknowledgement of a processed snapshot, never a claim that every selected effect ran.
        _applied = _revision;
        _status.AppliedRevision = _applied;
        _status.SessionActive = _hasSnapshot;
    }

    private static string Hex(ulong value) => $"0x{value:X}";
    private static float[] MatrixValues(Matrix4x4 value) =>
        [value.M11, value.M12, value.M13, value.M14, value.M21, value.M22, value.M23, value.M24,
         value.M31, value.M32, value.M33, value.M34, value.M41, value.M42, value.M43, value.M44];

    private string[] Capabilities()
    {
        var output = new List<string> { "aim", "aim.playersOnly", "esp", "esp.boxes", "esp.skeletons", "esp.distance", "esp.playersOnly", "player.godMode", "player.suicide" };
        if (_recoilOffset is not null) output.Add("weapons.noRecoil");
        if (_spreadOffset is not null) output.Add("weapons.noSpread");
        return output.ToArray();
    }

    private void ReleasePed()
    {
        _actionFence.Invalidate();
        Interlocked.Increment(ref _sessionEpoch);
        RestoreGod(); RestoreWeapons(); _ped = 0; _hasSnapshot = false;
        _settings = NewDefaults(); _applied = 0; _lastAimTimestamp = 0;
        Volatile.Write(ref _frame, OverlayFrame.Empty);
    }

    private void Detach(string message)
    {
        ReleasePed(); _memory?.Dispose(); _memory = null;
        _recoilOffset = null; _spreadOffset = null; _status = Waiting(message);
        ResetFeatureStates(message);
    }

    private void AddEvent(string type, string message)
    {
        _events.Add(new MenuEvent { Id = ++_cursor, Type = type, Message = message });
        if (_events.Count > 100) _events.RemoveAt(0);
    }

    private static ServerStatus Waiting(string message, int? pid = null) => new()
    { Version = AppIdentity.DisplayVersion + " / b3258", Message = message, PlayerId = pid, Capabilities = [] };
    private static MenuSettings NewDefaults()
    {
        var settings = new MenuSettings();
        settings.Aim.OnlyVisible = false; settings.Esp.OnlyVisible = false; settings.Esp.Names = false;
        return settings;
    }
    private static T Clone<T>(T value) => ExternalStateSerialization.Clone(value);
    private static bool Pointer(ulong address) => address is >= 0x10000 and < 0x00007FFFFFFFFFFF;
    private static bool Position(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z) &&
        Math.Abs(value.X) < 100000 && Math.Abs(value.Y) < 100000 && Math.Abs(value.Z) < 100000;
    private static bool KeyPressed(string key) => (GetAsyncKeyState(key switch
    { "LMB" => 0x01, "Alt" => 0x12, "Shift" => 0x10, "Ctrl" => 0x11, _ => 0x02 }) & 0x8000) != 0;

    private static Rectangle GameWindow(int pid, out bool foreground)
    {
        nint target = 0;
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out uint owner);
            if (owner == pid && IsWindowVisible(window) && GetClientRect(window, out var rect) && rect.Right - rect.Left > 300)
            { target = window; return false; }
            return true;
        }, 0);
        foreground = target != 0 && GetForegroundWindow() == target;
        if (target == 0 || !GetClientRect(target, out var bounds)) return Rectangle.Empty;
        var point = new NativePoint();
        if (!ClientToScreen(target, ref point)) return Rectangle.Empty;
        return new Rectangle(point.X, point.Y, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
    }

    public void Dispose()
    {
        lock (_gate) { if (_disposed) return; _disposed = true; }
        DetectionEnabled = false; _lifetime.Cancel();
        _worker.GetAwaiter().GetResult();
        _overlay.Dispose(); _lifetime.Dispose();
    }

    private delegate bool EnumWindowsCallback(nint window, nint parameter);
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsCallback callback, nint parameter);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint window, out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(nint window, ref NativePoint point);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int virtualKey);
}
