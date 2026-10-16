// Read-only disk inspection. A matching ASI is an installation fact, never proof of native execution.
using System.Security.Cryptography;
using System.Text.Json;

namespace ZyonMenu.Windows;

internal enum ClientModuleInstallationState { Unknown, Missing, Outdated, ReadyOnDisk, AccessDenied }

internal sealed record ClientModuleInstallationSnapshot(
    ClientModuleInstallationState State,
    string? RootPath,
    string? ModulePath,
    string? ExpectedSha256,
    string? ActualSha256,
    string ExpectedVersion,
    string? InstalledVersion,
    string RootSource,
    string Reason,
    string SuggestedAction,
    string? MetadataIssue = null)
{
    public int? RunningGameProcessId { get; init; }
    public string? RunningGameProcessName { get; init; }
    // Image presence only; neither an authorization check nor proof of native execution.
    public bool? ModuleLoaded { get; init; }
}

internal readonly record struct ClientModuleFileStamp(long Length, DateTime LastWriteTimeUtc);

internal interface IClientModuleInstallationFileSystem
{
    ClientModuleFileStamp? GetFileStamp(string path);
    byte[] ReadBounded(string path, int maximumBytes);
}

internal sealed class ClientModuleInstallationFileSystem : IClientModuleInstallationFileSystem
{
    public ClientModuleFileStamp? GetFileStamp(string path)
    {
        // File.Exists suppresses access errors; opening attributes preserves that distinction.
        try
        {
            var file = new FileInfo(path);
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.Directory) != 0) throw new IOException("O caminho do módulo é uma pasta.");
            return new(file.Length, file.LastWriteTimeUtc);
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { return null; }
    }

    public byte[] ReadBounded(string path, int maximumBytes)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length > maximumBytes) throw new InvalidDataException("O arquivo excede o tamanho permitido para diagnóstico.");
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = stream.Read(buffer, 0, Math.Min(buffer.Length, maximumBytes + 1 - checked((int)output.Length)))) != 0)
        {
            output.Write(buffer, 0, read);
            if (output.Length > maximumBytes) throw new InvalidDataException("O arquivo mudou de tamanho durante a leitura.");
        }
        return output.ToArray();
    }
}

internal sealed class ClientModuleInstallationInspector
{
    internal const int MaximumModuleBytes = 1024 * 1024;
    internal const int MaximumMetadataBytes = 16 * 1024;
    private readonly IClientModuleInstallationFileSystem _files;
    private readonly string _expectedHash;
    private readonly string _expectedVersion;
    private string? _cachedPath;
    private ClientModuleFileStamp? _cachedStamp;
    private string? _cachedHash;
    private DateTimeOffset _cachedUtc;

    public ClientModuleInstallationInspector(byte[] expectedImage, string expectedVersion,
        IClientModuleInstallationFileSystem? files = null)
    {
        if (expectedImage.Length is < 256 or > MaximumModuleBytes || expectedImage[0] != 'M' || expectedImage[1] != 'Z')
            throw new InvalidDataException("O executável não contém uma imagem válida do módulo local.");
        _expectedHash = Convert.ToHexString(SHA256.HashData(expectedImage)).ToLowerInvariant();
        _expectedVersion = expectedVersion;
        _files = files ?? new ClientModuleInstallationFileSystem();
    }

    public ClientModuleInstallationSnapshot Inspect(string? rootPath, string rootSource = "selected")
    {
        string? modulePath = null;
        string? actualHash = null;
        string? installedVersion = null;
        string? metadataIssue = null;
        ClientModuleInstallationSnapshot Result(ClientModuleInstallationState state, string reason, string action) =>
            new(state, rootPath, modulePath, _expectedHash, actualHash, _expectedVersion, installedVersion,
                rootSource, reason, action, metadataIssue);
        if (string.IsNullOrWhiteSpace(rootPath))
            return Result(ClientModuleInstallationState.Unknown, "A pasta FiveM.app ainda não foi identificada.", "Use Preparar módulo local e selecione sua pasta FiveM.app.");
        try
        {
            rootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
            modulePath = Path.Combine(rootPath, "plugins", "ZyonClient.asi");
            var stamp = _files.GetFileStamp(modulePath);
            if (stamp is null)
                return Result(ClientModuleInstallationState.Missing, "O módulo ZyonClient.asi não está instalado nesta pasta do FiveM.",
                    "Feche o FiveM e use Preparar módulo local nesta instalação.");
            if (stamp.Value.Length is < 256 or > MaximumModuleBytes)
                return Result(ClientModuleInstallationState.Outdated, "O arquivo instalado tem tamanho inválido para o módulo desta versão.",
                    "Feche o FiveM e prepare o módulo local novamente.");
            if (_cachedPath == modulePath && _cachedStamp == stamp && _cachedHash is not null &&
                DateTimeOffset.UtcNow - _cachedUtc < TimeSpan.FromSeconds(60)) actualHash = _cachedHash;
            else
            {
                actualHash = Convert.ToHexString(SHA256.HashData(_files.ReadBounded(modulePath, MaximumModuleBytes))).ToLowerInvariant();
                _cachedPath = modulePath;
                _cachedStamp = stamp;
                _cachedHash = actualHash;
                _cachedUtc = DateTimeOffset.UtcNow;
            }
            try
            {
                var metadataPath = Path.Combine(rootPath, "plugins", "ZyonClient-install.json");
                if (_files.GetFileStamp(metadataPath) is null) metadataIssue = "Registro de instalação ausente; o hash do ASI foi verificado diretamente.";
                else
                {
                    using var document = JsonDocument.Parse(_files.ReadBounded(metadataPath, MaximumMetadataBytes), new JsonDocumentOptions { MaxDepth = 8 });
                    var record = document.RootElement;
                    if (record.ValueKind != JsonValueKind.Object) throw new JsonException();
                    if (record.TryGetProperty("Version", out var version) && version.ValueKind == JsonValueKind.String)
                    {
                        var text = version.GetString();
                        if (text?.Length <= 64 && Version.TryParse(text, out var parsed)) installedVersion = parsed.ToString();
                    }
                    if (!record.TryGetProperty("Sha256", out var hash) || hash.ValueKind != JsonValueKind.String ||
                        !string.Equals(hash.GetString(), actualHash, StringComparison.OrdinalIgnoreCase))
                        metadataIssue = "O registro de instalação difere do arquivo ASI; o hash do arquivo é o dado usado.";
                }
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
            {
                metadataIssue = "O registro de instalação está inválido ou não pôde ser lido; o hash do ASI foi verificado diretamente.";
            }
            if (actualHash != _expectedHash)
                return Result(ClientModuleInstallationState.Outdated, "O módulo instalado é diferente do incluído neste executável.",
                    "Feche o FiveM e use Preparar módulo local para atualizar o ASI; depois abra o jogo novamente.");
            return Result(ClientModuleInstallationState.ReadyOnDisk,
                "O módulo desta versão está no disco. Isso ainda não confirma que o FiveM o carregou ou autorizou sua execução.",
                "Abra o FiveM novamente após a instalação e aguarde a conexão do módulo. O servidor precisa permitir plugins/ScriptHook.");
        }
        catch (UnauthorizedAccessException)
        {
            return Result(ClientModuleInstallationState.AccessDenied, "Não foi possível ler a pasta do módulo com as permissões atuais.",
                "Confira a pasta FiveM.app selecionada e as permissões de leitura do arquivo. Não é necessário cargo de administrador do servidor.");
        }
        catch (Exception error) when (error is IOException or ArgumentException or NotSupportedException)
        {
            return Result(ClientModuleInstallationState.Unknown, "Não foi possível verificar com segurança o arquivo do módulo local.",
                "Confira a pasta FiveM.app e prepare novamente o módulo com o jogo fechado.");
        }
    }
}
