// C# / src/ZyonMenu.Windows/LocalGameSession.cs / .NET 8 / Windows 10 x64 FiveM b3258 local backend.
using System.Buffers.Binary;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using ZyonMenu.Core;

namespace ZyonMenu.Windows;

internal sealed record ActorVisual(Vector2?[] Bones, float Distance, bool Player);
internal sealed record OverlayFrame(Rectangle Bounds, bool Foreground, MenuSettings Settings, ActorVisual[] Actors,
    long CapturedTimestamp)
{
    public static OverlayFrame Empty { get; } = new(Rectangle.Empty, false, new(), [], 0);
}

internal sealed class LocalGameSession : IDisposable
{
    private const uint WorldOffset = 0x25B14B0, ReplayOffset = 0x1FBD4F0, ViewOffset = 0x201DBA0;
    private const uint CameraOffset = 0x201E7D0;
    private const uint InvincibleMask = 1u << 8;
    private readonly object _gate = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Task _worker;
    private readonly EspOverlayHost _overlay;
    private readonly List<MenuEvent> _events = [];
    private GameMemory? _memory;
    // Replace this owned snapshot when settings change; never mutate a published snapshot.
    private MenuSettings _settings = NewDefaults();
    private ServerStatus _status = Waiting("Procurando o processo b3258; escolhas locais ainda não aplicadas.");
    private ulong _ped, _godPed;
    private uint _originalGodBit;
    private ulong _weapon;
    private sealed record WeaponFieldSnapshot(ulong Info, uint Hash, uint OriginalBits);
    private readonly Dictionary<ulong, WeaponFieldSnapshot> _originalWeaponValues = [];
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
        _worker = Task.Run(RunAsync);
    }

    public bool DetectionEnabled
    {
        get => Volatile.Read(ref _enabled) != 0;
        set => Interlocked.Exchange(ref _enabled, value ? 1 : 0);
    }

    public Task<ServerStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() =>
        {
            lock (_gate)
            {
                cancellationToken.ThrowIfCancellationRequested();
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
            return JsonSerializer.Serialize(new
            {
                Product = "Zyon standalone b3258",
                Version = "3.1.1",
                CapturedUtc = DateTimeOffset.UtcNow,
                DetectionEnabled,
                Overlay = new { _overlay.IsRunning, Failure = _overlay.Failure?.Message },
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
            }, new JsonSerializerOptions { WriteIndented = true });
        }
    }

    public Task PushSettingsAsync(MenuSettings settings, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        settings.Validate();
        // Copy before scheduling: later edits in the menu must not change this request.
        var snapshot = Clone(settings);
        return Task.Run(() =>
        {
            lock (_gate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ObjectDisposedException.ThrowIf(_disposed, this);
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
                AddEvent("settings", "Escolhas recebidas para aplicação no personagem local validado.");
            }
        }, cancellationToken);
    }

    public Task ReviveAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromException(new NotSupportedException(
            "Ressurreição e estado médico não foram implementados neste backend. Alterar a vida não confirma reviver."));
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
                        AddEvent("error", error.Message);
                        Detach("Estado local não confirmado: " + error.Message);
                    }
                }
            }
            while (await timer.WaitForNextTickAsync(_lifetime.Token));
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        finally { lock (_gate) Detach("Backend local encerrado."); }
    }

    private void Tick()
    {
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
            Version = "3.1.1 / b3258", Ready = true, PlayerOnline = true, PlayerId = memory.ProcessId,
            PlayerName = "Processo local b3258", SessionActive = _hasSnapshot && _applied == _revision,
            DesiredRevision = _revision, AppliedRevision = _applied, Capabilities = Capabilities(),
            Message = health <= 0 ? "Personagem sem vida; ressuscitar ainda exige implementação nativa validada."
                : "Personagem local validado; ID de processo não representa ID da cidade."
        };
        if (!_hasSnapshot) { Volatile.Write(ref _frame, OverlayFrame.Empty); return; }
        var settings = _settings;
        if (!CurrentPedMatches(_ped)) throw new InvalidOperationException("O personagem mudou durante a aplicação.");
        ApplyGodMode();
        ApplyWeapons();
        _applied = _revision;
        _status.AppliedRevision = _applied;
        _status.SessionActive = true;
        var bounds = GameWindow(memory.ProcessId, out bool foreground);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            Volatile.Write(ref _frame, OverlayFrame.Empty);
            return;
        }
        var actors = settings.Esp.Enabled || settings.Aim.Enabled ? ReadActors(position, bounds) : [];
        if (foreground && health > 0 && settings.Aim.Enabled && KeyPressed(settings.Aim.ActivationKey))
        {
            double elapsedSeconds = _lastAimTimestamp == 0 ? 1d / 30
                : (capturedTimestamp - _lastAimTimestamp) / (double)Stopwatch.Frequency;
            ApplyAim(actors, bounds, elapsedSeconds);
            _lastAimTimestamp = capturedTimestamp;
        }
        else _lastAimTimestamp = 0;
        Volatile.Write(ref _frame, new OverlayFrame(bounds, foreground, settings,
            actors.Select(item => item.Visual).ToArray(), capturedTimestamp));
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
        var memory = _memory!;
        if (!memory.TryRead(memory.ModuleBase + WorldOffset, out ulong world))
        { reason = $"não foi possível ler o mundo no offset 0x{WorldOffset:X}."; return false; }
        if (!Pointer(world))
        { reason = "o ponteiro do mundo ainda não é válido para o perfil b3258."; return false; }
        if (!memory.TryRead(world + 8, out ped))
        { reason = "não foi possível ler o ponteiro do personagem local."; return false; }
        if (!Pointer(ped))
        { reason = "aguardando a criação do personagem local."; return false; }
        if (!memory.TryRead(ped + 0x90, out position) || !Position(position))
        { reason = "a posição do personagem não foi confirmada no perfil b3258."; return false; }
        if (!memory.TryRead(ped + 0x280, out health) || !float.IsFinite(health) || health is < -1000 or > 10000)
        { reason = "a leitura de vida do personagem não corresponde ao perfil b3258."; return false; }
        if (!memory.TryRead(ped + 0x60, out Matrix4x4 matrix))
        { reason = "não foi possível ler a transformação do personagem."; return false; }
        var translation = new Vector3(matrix.M41, matrix.M42, matrix.M43);
        if (!Position(translation) || Vector3.Distance(translation, position) >= 2 ||
            !ValidBasis(new(matrix.M11, matrix.M12, matrix.M13)) ||
            !ValidBasis(new(matrix.M21, matrix.M22, matrix.M23)) ||
            !ValidBasis(new(matrix.M31, matrix.M32, matrix.M33)))
        { reason = "a base XYZ da transformação do personagem não foi confirmada."; return false; }
        // GTA's XYZ transform does not establish a required homogeneous/padding value at M44.
        return true;
    }

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
        var memory = _memory!;
        if (_settings.Player.GodMode)
        {
            uint flags = memory.Read<uint>(_ped + 0x188);
            if (_godPed != _ped) { _godPed = _ped; _originalGodBit = flags & InvincibleMask; }
            if ((flags & InvincibleMask) == 0) memory.Write(_ped + 0x188, flags | InvincibleMask);
            if ((memory.Read<uint>(_ped + 0x188) & InvincibleMask) == 0)
                throw new IOException("O bit de invulnerabilidade não permaneceu aplicado.");
        }
        else RestoreGod();
    }

    private void RestoreGod()
    {
        if (_godPed == 0 || _memory is null) return;
        if (_memory.IsAlive && CurrentPedMatches(_godPed) && _memory.TryRead(_godPed + 0x188, out uint flags))
        {
            uint restored = (flags & ~InvincibleMask) | _originalGodBit;
            if (restored != flags && !_memory.TryWrite(_godPed + 0x188, restored))
                AddEvent("restore", "A restauração do bit de invulnerabilidade não foi confirmada.");
        }
        _godPed = 0;
        _originalGodBit = 0;
    }

    private void ApplyWeapons()
    {
        var memory = _memory!;
        ulong current = 0;
        if (memory.TryRead(_ped + 0x10B8, out ulong manager) && Pointer(manager))
            memory.TryRead(manager + 0x20, out current);
        if (!Pointer(current)) current = 0;
        if (current != _weapon) { RestoreWeapons(); _weapon = current; }
        if (_weapon == 0) return;
        if (!memory.TryRead(_weapon + 0x10, out uint hash) || hash == 0) return;
        ApplyWeaponValue(_recoilOffset, _settings.Weapons.NoRecoil);
        ApplyWeaponValue(_spreadOffset, _settings.Weapons.NoSpread);
    }

    private void ApplyWeaponValue(int? offset, bool enabled)
    {
        if (offset is null) return;
        ulong address = _weapon + (uint)offset.Value;
        var memory = _memory!;
        if (!enabled)
        {
            if (_originalWeaponValues.Remove(address, out var snapshot)) RestoreWeaponField(address, snapshot);
            return;
        }
        uint bits = memory.Read<uint>(address);
        float value = BitConverter.UInt32BitsToSingle(bits);
        if (!float.IsFinite(value) || value is < 0 or > 100) throw new InvalidOperationException("O campo da arma saiu do intervalo válido.");
        uint hash = memory.Read<uint>(_weapon + 0x10);
        if (_originalWeaponValues.TryGetValue(address, out var prior) && (prior.Info != _weapon || prior.Hash != hash))
            throw new InvalidOperationException("A identidade dos dados da arma mudou durante a aplicação.");
        if (!_originalWeaponValues.ContainsKey(address) || bits != 0)
            _originalWeaponValues[address] = new WeaponFieldSnapshot(_weapon, hash, bits);
        if (bits != 0) memory.Write(address, 0u);
        if (memory.Read<uint>(address) != 0) throw new IOException("O campo da arma não confirmou a aplicação.");
    }

    private void RestoreWeaponField(ulong address, WeaponFieldSnapshot snapshot)
    {
        var memory = _memory;
        if (memory is not { IsAlive: true } || !memory.TryRead(snapshot.Info + 0x10, out uint hash) || hash != snapshot.Hash ||
            !memory.TryRead(address, out uint current) || current != 0)
        {
            AddEvent("restore", "A arma mudou ou o campo recebeu outro valor; a restauração anterior foi descartada.");
            return;
        }
        if (!memory.TryWrite(address, snapshot.OriginalBits) || !memory.TryRead(address, out uint restored) || restored != snapshot.OriginalBits)
            AddEvent("restore", "A restauração de um dado da arma não foi confirmada.");
    }

    private void RestoreWeapons()
    {
        if (_memory is { IsAlive: true })
            foreach (var (address, snapshot) in _originalWeaponValues) RestoreWeaponField(address, snapshot);
        _originalWeaponValues.Clear();
        _weapon = 0;
    }

    private sealed record TargetActor(ulong Address, Vector3[] WorldBones, Vector3 Torso, Vector2? TorsoScreen, ActorVisual Visual);

    private TargetActor[] ReadActors(Vector3 localPosition, Rectangle bounds)
    {
        var memory = _memory!;
        if (!memory.TryRead(memory.ModuleBase + ViewOffset, out ulong viewport) || !Pointer(viewport) ||
            !memory.TryRead(viewport + 0x24C, out Matrix4x4 view) ||
            !memory.TryRead(memory.ModuleBase + ReplayOffset, out ulong replay) || !Pointer(replay) ||
            !memory.TryRead(replay + 0x18, out ulong pedInterface) || !Pointer(pedInterface) ||
            !memory.TryRead(pedInterface + 0x100, out ulong list) || !Pointer(list)) return [];
        var output = new List<TargetActor>();
        for (int index = 0; index < 256 && output.Count < 64; index++)
        {
            if (!memory.TryRead(list + (ulong)index * 16, out ulong ped)) continue;
            if (!Pointer(ped) || ped == _ped || !memory.TryRead(ped + 0x280, out float health) ||
                !float.IsFinite(health) || health is <= 0 or > 10000 ||
                !memory.TryRead(ped + 0x90, out Vector3 position) || !Position(position)) continue;
            float distance = Vector3.Distance(localPosition, position);
            if (!float.IsFinite(distance) || distance > Math.Max(_settings.Esp.MaxDistance, 250)) continue;
            bool player = memory.TryRead(ped + 0x10A8, out ulong info) && Pointer(info) && memory.IsReadableRange(info, 8);
            if ((!_settings.Aim.Enabled || _settings.Aim.OnlyPlayers) && (!_settings.Esp.Enabled || _settings.Esp.OnlyPlayers) && !player) continue;
            if (!memory.TryRead(ped + 0x60, out Matrix4x4 transform) ||
                !memory.TryReadBytes(ped + 0x410, 140, out var rows)) continue;
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
                }
            }
            if (!valid || projectedCount == 0) continue;
            var torso = Vector3.Lerp(bones[8], bones[7], 0.65f);
            Vector2? torsoScreen = ProjectionMath.TryProject(torso, view, bounds.Width, bounds.Height, out var torsoPoint)
                ? torsoPoint : null;
            output.Add(new TargetActor(ped, bones, torso, torsoScreen, new ActorVisual(projected, distance, player)));
        }
        return output.ToArray();
    }

    private void ApplyAim(TargetActor[] actors, Rectangle bounds, double elapsedSeconds)
    {
        var center = new Vector2(bounds.Width / 2f, bounds.Height / 2f);
        int index = _settings.Aim.TargetBone == "Head" ? 0 : 7;
        bool torsoTarget = _settings.Aim.TargetBone == "Chest";
        Vector2? AimPoint(TargetActor item) => torsoTarget ? item.TorsoScreen : item.Visual.Bones[index];
        TargetActor? nearest = actors.Where(item => !_settings.Aim.OnlyPlayers || item.Visual.Player)
            .Where(item => AimPoint(item) is { } point && ProjectionMath.TargetDistanceWithinFov(point, center, _settings.Aim.Fov))
            .OrderBy(item => Vector2.DistanceSquared(AimPoint(item)!.Value, center)).FirstOrDefault();
        if (nearest is null) return;
        var memory = _memory!;
        if (!memory.TryRead(memory.ModuleBase + CameraOffset, out ulong camera) || !Pointer(camera) ||
            !memory.TryRead(camera + 0x60, out Vector3 eye) || !Position(eye) ||
            !memory.TryRead(camera + 0x40, out Vector3 forward) || !Position(forward)) return;
        float length = forward.Length();
        Vector3 delta = (torsoTarget ? nearest.Torso : nearest.WorldBones[index]) - eye;
        if (!float.IsFinite(length) || length is < 0.5f or > 1.5f || delta.LengthSquared() < 0.0001f) return;
        var direction = Vector3.Normalize(Vector3.Lerp(forward, Vector3.Normalize(delta),
            AimTiming.BlendFactor(_settings.Aim.Smooth, elapsedSeconds)));
        if (!Position(direction) || !CurrentPedMatches(_ped)) return;
        memory.Write(camera + 0x40, direction);
        memory.Write(camera + 0x3D0, direction);
    }

    private void ResolveWeaponFields()
    {
        var memory = _memory!;
        if (!memory.TryReadBytes(memory.ModuleBase, 4096, out var header)) return;
        if (header[0] != 'M' || header[1] != 'Z') return;
        int pe = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(0x3C, 4));
        if (pe < 0 || pe > 3000 || header[pe] != 'P' || header[pe + 1] != 'E') return;
        int count = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(pe + 6, 2));
        int table = pe + 24 + BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(pe + 20, 2));
        var recoilPattern = BytePattern.Parse("48 8B 47 40 F3 0F 10 B0 ?? ?? ?? ??");
        var spreadPattern = BytePattern.Parse("F3 0F 10 43 ?? F3 0F 59 05 ?? ?? ?? ?? F3 0F 2C C0");
        var recoil = new List<int>(); var spread = new List<int>();
        for (int section = 0; section < count && section < 96; section++)
        {
            int start = table + section * 40;
            if (start > header.Length - 40) return;
            uint flags = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(start + 36, 4));
            if ((flags & 0x20000000) == 0) continue;
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(start + 8, 4));
            uint rva = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(start + 12, 4));
            if (size == 0 || size > 32 * 1024 * 1024 || rva > memory.ImageSize || size > memory.ImageSize - rva ||
                !memory.TryReadBytes(memory.ModuleBase + rva, (int)size, out var code)) continue;
            foreach (int match in recoilPattern.FindAll(code)) recoil.Add(BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(match + 8, 4)));
            foreach (int match in spreadPattern.FindAll(code)) spread.Add(code[match + 4]);
        }
        if (recoil.Count == 1 && recoil[0] is >= 0x20 and <= 0x1000) _recoilOffset = recoil[0];
        if (spread.Count == 1 && spread[0] is >= 0x20 and <= 0x1000) _spreadOffset = spread[0];
    }

    private string[] Capabilities()
    {
        var output = new List<string> { "aim", "aim.playersOnly", "esp", "esp.boxes", "esp.skeletons", "esp.distance", "esp.playersOnly", "player.godMode" };
        if (_recoilOffset is not null) output.Add("weapons.noRecoil");
        if (_spreadOffset is not null) output.Add("weapons.noSpread");
        return output.ToArray();
    }

    private void ReleasePed()
    {
        RestoreGod(); RestoreWeapons(); _ped = 0; _hasSnapshot = false;
        _settings = NewDefaults(); _applied = 0; _lastAimTimestamp = 0;
        Volatile.Write(ref _frame, OverlayFrame.Empty);
    }

    private void Detach(string message)
    {
        ReleasePed(); _memory?.Dispose(); _memory = null;
        _recoilOffset = null; _spreadOffset = null; _status = Waiting(message);
    }

    private void AddEvent(string type, string message)
    {
        _events.Add(new MenuEvent { Id = ++_cursor, Type = type, Message = message });
        if (_events.Count > 100) _events.RemoveAt(0);
    }

    private static ServerStatus Waiting(string message, int? pid = null) => new()
    { Version = "3.1.1 / b3258", Message = message, PlayerId = pid, Capabilities = [] };
    private static MenuSettings NewDefaults()
    {
        var settings = new MenuSettings();
        settings.Aim.OnlyVisible = false; settings.Esp.OnlyVisible = false; settings.Esp.Names = false;
        return settings;
    }
    private static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
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
