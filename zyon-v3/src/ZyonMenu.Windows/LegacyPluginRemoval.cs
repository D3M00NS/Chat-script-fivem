// Only the old Zyon plugin can be disabled here; FiveM files and other plugins are preserved.
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace ZyonMenu.Windows;

internal enum LegacyPluginRemovalState { Disabled, Missing, GameRunning, UnknownFile, InvalidLocation, Failed }

internal sealed record LegacyPluginRemovalResult(LegacyPluginRemovalState State, string Message, string? BackupPath = null);

internal sealed class LegacyPluginRemoval
{
    internal const int MaximumPluginBytes = 1024 * 1024;
    internal const int MaximumPreferenceBytes = 64 * 1024;

    // ASI hashes recorded in downloads/ZyonMenu-v{3.2.0,3.3.0,3.3.1,3.3.2,3.3.3}-compacto.zip manifests.
    // A local install record cannot add trust for an arbitrary file.
    private static readonly string[] ReleasedHashes =
    [
        "56f988d75c8eff53db5bde5b25c4b1536c314cca6bc7772586ba8019c390f082",
        "c20e0a8557addc0fa956c59ae3df4b233c939d7f7b6bfb1091bfb1154871353f",
        "3eb6f005a510a4ff3282477db52a2cf9b60432570dbdb918423b59bcf79a6468",
        "bd66c6bfb4254f5c50131cf0f1a34036901db2d84c6b612f8373361f243cfaf2"
    ];

    private readonly HashSet<string> _trustedHashes;
    private readonly Func<DateTimeOffset> _utcNow;

    internal LegacyPluginRemoval(IEnumerable<string>? trustedHashes = null, Func<DateTimeOffset>? utcNow = null)
    {
        _trustedHashes = new HashSet<string>(trustedHashes ?? ReleasedHashes, StringComparer.OrdinalIgnoreCase);
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    internal static string? ReadPreferredRoot(string preferencePath)
    {
        try
        {
            if (!File.Exists(preferencePath) || !HasNoLinks(preferencePath)) return null;
            using var document = JsonDocument.Parse(ReadBounded(preferencePath, MaximumPreferenceBytes),
                new JsonDocumentOptions { MaxDepth = 4 });
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("FiveMRoot", out var value) || value.ValueKind != JsonValueKind.String)
                return null;
            return ValidateRoot(value.GetString());
        }
        catch (Exception error) when (IsFileError(error) || error is JsonException) { return null; }
    }

    internal static bool IsValidRoot(string? path) => ValidateRoot(path) is not null;

    internal LegacyPluginRemovalResult Disable(string rootPath, Func<bool> gameIsRunning)
    {
        try
        {
            if (gameIsRunning()) return Running();
            var root = ValidateRoot(rootPath);
            if (root is null)
                return new(LegacyPluginRemovalState.InvalidLocation,
                    "Selecione uma pasta FiveM.app local válida, sem atalhos ou links de pasta.");
            var plugins = Path.Combine(root, "plugins");
            var source = Path.Combine(plugins, "ZyonClient.asi");
            if (!Directory.Exists(plugins)) return Missing();
            if (!HasNoLinks(plugins)) return UnsafeLocation();
            if (!File.Exists(source))
            {
                // File.Exists hides dangling links and access failures. Attributes distinguishes both.
                try { _ = File.GetAttributes(source); }
                catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { return Missing(); }
            }
            if (!IsRegularLocalFile(source)) return UnsafeLocation();
            var hash = HashPlugin(source);
            if (!_trustedHashes.Contains(hash))
                return new(LegacyPluginRemovalState.UnknownFile,
                    "O arquivo ZyonClient.asi não corresponde às versões publicadas do Zyon. Ele foi preservado; confira-o manualmente.");
            // Check again immediately before the rename. Never kill a game process or request elevation.
            if (gameIsRunning()) return Running();
            if (!HasNoLinks(root) || !IsRegularLocalFile(source)) return UnsafeLocation();
            var stamp = _utcNow().UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            for (var index = 0; index < 100; index++)
            {
                var suffix = index == 0 ? "" : "-" + index.ToString(CultureInfo.InvariantCulture);
                var backup = source + "." + stamp + suffix + ".disabled";
                try { File.Move(source, backup, overwrite: false); }
                catch (IOException) when (File.Exists(backup) || Directory.Exists(backup)) { continue; }
                // Verify the moved file as well, so a source replacement cannot be reported as a verified removal.
                if (!IsRegularLocalFile(backup) || !string.Equals(HashPlugin(backup), hash, StringComparison.OrdinalIgnoreCase))
                {
                    if (!File.Exists(source) && IsRegularLocalFile(backup)) File.Move(backup, source, overwrite: false);
                    return new(LegacyPluginRemovalState.Failed,
                        "O arquivo mudou durante a operação. A desativação não pôde ser confirmada; confira a pasta plugins.");
                }
                return new(LegacyPluginRemovalState.Disabled,
                    "O plugin antigo do Zyon foi desativado. O arquivo foi preservado com extensão .disabled. Abra o FiveM novamente.", backup);
            }
            return new(LegacyPluginRemovalState.Failed,
                "Já existem muitas cópias desativadas com esse nome. Nenhum arquivo existente foi substituído.");
        }
        catch (Exception error) when (IsFileError(error))
        {
            return new(LegacyPluginRemovalState.Failed,
                "Não foi possível desativar o plugin com as permissões atuais. Nenhum pedido de administrador será feito; confira a pasta e se o FiveM está fechado.");
        }
    }

    private static LegacyPluginRemovalResult Running() => new(LegacyPluginRemovalState.GameRunning,
        "Feche o FiveM antes de desativar o plugin antigo. O menu atual funciona sem esse plugin.");
    private static LegacyPluginRemovalResult Missing() => new(LegacyPluginRemovalState.Missing,
        "O plugin antigo ZyonClient.asi não está presente nesta instalação. O menu atual funciona sem ele.");
    private static LegacyPluginRemovalResult UnsafeLocation() => new(LegacyPluginRemovalState.InvalidLocation,
        "A pasta ou o arquivo do plugin usa um link ou caminho inválido. Nenhum arquivo foi alterado.");

    private static string? ValidateRoot(string? path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || path.Length > 4096 || path.StartsWith("\\\\", StringComparison.Ordinal) ||
                path.StartsWith("//", StringComparison.Ordinal) || !Path.IsPathFullyQualified(path)) return null;
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            if (!Path.GetFileName(root).Equals("FiveM.app", StringComparison.OrdinalIgnoreCase) ||
                !Directory.Exists(root) || !HasNoLinks(root)) return null;
            var ini = Path.Combine(root, "CitizenFX.ini");
            var citizen = Path.Combine(root, "citizen");
            var subprocess = Path.Combine(root, "data", "cache", "subprocess");
            return (File.Exists(ini) && IsRegularLocalFile(ini)) ||
                (Directory.Exists(citizen) && HasNoLinks(citizen)) ||
                (Directory.Exists(subprocess) && HasNoLinks(subprocess)) ? root : null;
        }
        catch (Exception error) when (IsFileError(error)) { return null; }
    }

    private static bool IsRegularLocalFile(string path) =>
        (File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0 && HasNoLinks(path);

    private static bool HasNoLinks(string path)
    {
        var current = Path.GetFullPath(path);
        while (!string.IsNullOrEmpty(current))
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return false;
            current = Path.GetDirectoryName(current);
        }
        return true;
    }

    private static string HashPlugin(string path)
    {
        var bytes = ReadBounded(path, MaximumPluginBytes);
        if (bytes.Length < 256 || bytes[0] != 'M' || bytes[1] != 'Z')
            throw new InvalidDataException("O arquivo não é uma imagem conhecida do plugin.");
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private static byte[] ReadBounded(string path, int maximum)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > maximum) throw new InvalidDataException("Arquivo acima do limite de leitura.");
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = stream.Read(buffer, 0, Math.Min(buffer.Length, maximum + 1 - checked((int)output.Length)))) != 0)
        {
            output.Write(buffer, 0, read);
            if (output.Length > maximum) throw new InvalidDataException("O arquivo aumentou durante a leitura.");
        }
        return output.ToArray();
    }

    private static bool IsFileError(Exception error) => error is IOException or InvalidDataException or UnauthorizedAccessException or
        ArgumentException or NotSupportedException or System.Security.SecurityException;
}
