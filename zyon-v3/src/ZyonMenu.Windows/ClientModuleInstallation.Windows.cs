// Windows discovery is read-only. Only RememberSelectedRoot writes a local preference after explicit setup.
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace ZyonMenu.Windows;

internal static class ClientModuleInstallation
{
    private static readonly object Sync = new();
    private static ClientModuleInstallationInspector? _inspector;
    private static DateTimeOffset _lastRefresh;
    private static string? _lastRunningRoot;
    private static string? _lastSelectedRoot;
    private static ClientModuleInstallationSnapshot _current = Unknown("A instalação do módulo local ainda não foi verificada.");
    private const string ResourceName = "ZyonMenu.ClientModule.asi";

    public static ClientModuleInstallationSnapshot Current { get { lock (Sync) return _current; } }

    public static ClientModuleInstallationSnapshot Refresh(bool force = false)
    {
        lock (Sync)
        {
            if (!force && DateTimeOffset.UtcNow - _lastRefresh < TimeSpan.FromSeconds(5)) return _current;
            _lastRefresh = DateTimeOffset.UtcNow;
            try
            {
                _inspector ??= CreateInspector();
                var running = FindRunningGame();
                if (running.ProcessId is not null && running.Root is null)
                {
                    _current = Unknown(running.Error ?? "A pasta do FiveM em execução não pôde ser identificada.") with
                    {
                        State = running.AccessDenied ? ClientModuleInstallationState.AccessDenied : ClientModuleInstallationState.Unknown,
                        RootSource = "running-process", RunningGameProcessId = running.ProcessId,
                        RunningGameProcessName = running.ProcessName, ModuleLoaded = running.ModuleLoaded
                    };
                    return _current;
                }
                if (running.Root is not null) _lastRunningRoot = running.Root;
                var preferred = running.Root is not null || _lastRunningRoot is not null ? null : _lastSelectedRoot ?? ReadSelectedRoot();
                var root = running.Root ?? _lastRunningRoot ?? preferred ?? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FiveM", "FiveM.app");
                _current = _inspector.Inspect(root,
                    running.Root is not null ? "running-process" : _lastRunningRoot is not null ? "last-running-process" : preferred is not null ? "last-selected" : "default") with
                {
                    RunningGameProcessId = running.ProcessId, RunningGameProcessName = running.ProcessName,
                    ModuleLoaded = running.ModuleLoaded
                };
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException or Win32Exception)
            {
                _current = Unknown("Este executável não permitiu verificar a instalação do módulo local. Use o pacote completo e salve o diagnóstico.");
            }
            return _current;
        }
    }

    public static void RememberSelectedRoot(string rootPath)
    {
        // Installer has already verified the ASI; a preference-write failure must not invalidate that installation.
        try
        {
            var root = NormalizeLocalRoot(rootPath);
            if (root is null) return;
            lock (Sync)
            {
                _lastSelectedRoot = root;
                _lastRunningRoot = null;
                _lastRefresh = default;
            }
            var destination = PreferencePath;
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, JsonSerializer.Serialize(new { FiveMRoot = root }));
                File.Move(temporary, destination, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            lock (Sync) _lastRefresh = default;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { /* Optional local preference; read-only probing and native readiness are unaffected. */ }
    }

    private static string PreferencePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZyonMenu", "client-module-location.json");

    private static string? ReadSelectedRoot()
    {
        try
        {
            var files = new ClientModuleInstallationFileSystem();
            if (files.GetFileStamp(PreferencePath) is null) return null;
            using var document = JsonDocument.Parse(files.ReadBounded(PreferencePath, 8192), new JsonDocumentOptions { MaxDepth = 4 });
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("FiveMRoot", out var root) || root.ValueKind != JsonValueKind.String) return null;
            return NormalizeLocalRoot(root.GetString());
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException)
        { return null; }
    }

    private static string? NormalizeLocalRoot(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 4096 || path.StartsWith("\\\\", StringComparison.Ordinal) ||
            !Path.IsPathFullyQualified(path)) return null;
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        return Path.GetFileName(root).Equals("FiveM.app", StringComparison.OrdinalIgnoreCase) ? root : null;
    }

    private static ClientModuleInstallationInspector CreateInspector()
    {
        using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("Módulo não incluído no executável.");
        if (resource.Length is < 256 or > ClientModuleInstallationInspector.MaximumModuleBytes) throw new InvalidDataException();
        var bytes = new byte[checked((int)resource.Length)];
        resource.ReadExactly(bytes);
        return new(bytes, AppIdentity.DisplayVersion);
    }

    private static ClientModuleInstallationSnapshot Unknown(string reason) => new(
        ClientModuleInstallationState.Unknown, null, null, null, null, AppIdentity.DisplayVersion,
        null, "unknown", reason, "Use Preparar módulo local com o FiveM fechado e salve o diagnóstico se a conexão não aparecer.");

    private sealed record RunningGame(string? Root = null, int? ProcessId = null, string? ProcessName = null,
        bool? ModuleLoaded = null, bool AccessDenied = false, string? Error = null);

    private static RunningGame FindRunningGame()
    {
        var processes = Process.GetProcesses();
        RunningGame? result = null;
        try
        {
            foreach (var process in processes.OrderBy(item => item.Id))
            {
                string name;
                try { name = process.ProcessName; }
                catch (Exception error) when (error is InvalidOperationException or Win32Exception) { continue; }
                if (!name.StartsWith("FiveM_", StringComparison.OrdinalIgnoreCase) ||
                    !name.EndsWith("_GTAProcess", StringComparison.OrdinalIgnoreCase)) continue;
                RunningGame candidate;
                try
                {
                    var image = process.MainModule?.FileName;
                    var parent = string.IsNullOrWhiteSpace(image) ? null : Path.GetDirectoryName(image);
                    string? root = null;
                    for (var depth = 0; parent is not null && depth < 12; depth++, parent = Path.GetDirectoryName(parent))
                    {
                        if (!Path.GetFileName(parent).Equals("FiveM.app", StringComparison.OrdinalIgnoreCase)) continue;
                        root = NormalizeLocalRoot(parent);
                        break;
                    }
                    bool? loaded = null;
                    try
                    {
                        loaded = false;
                        foreach (ProcessModule module in process.Modules)
                            if (module.ModuleName.Equals("ZyonClient.asi", StringComparison.OrdinalIgnoreCase)) { loaded = true; break; }
                    }
                    catch (Exception error) when (error is Win32Exception or InvalidOperationException or NotSupportedException)
                    { loaded = null; }
                    candidate = new(root, process.Id, name, loaded, Error: root is null ? "O processo FiveM está aberto, mas sua pasta FiveM.app não pôde ser identificada." : null);
                }
                catch (Exception error) when (error is Win32Exception or InvalidOperationException or UnauthorizedAccessException or NotSupportedException)
                {
                    candidate = new(ProcessId: process.Id, ProcessName: name,
                        AccessDenied: error is UnauthorizedAccessException || error is Win32Exception { NativeErrorCode: 5 },
                        Error: "O FiveM está aberto, mas o caminho da instalação não pôde ser consultado. Confira as permissões locais dos processos.");
                }
                if (result is null) result = candidate;
                else
                    return result with { Root = null, Error = "Há várias sessões GTA do FiveM abertas. Feche as cópias extras e clique em Verificar novamente." };
            }
        }
        finally { foreach (var process in processes) process.Dispose(); }
        return result ?? new();
    }
}
