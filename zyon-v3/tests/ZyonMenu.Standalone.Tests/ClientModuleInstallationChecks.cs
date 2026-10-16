using System.Security.Cryptography;
using System.Text.Json;
using ZyonMenu.Windows;

namespace ZyonMenu.Standalone.Tests;

internal static class ClientModuleInstallationChecks
{
    public static int Run()
    {
        var assertions = 0;
        void Check(bool condition, string reason)
        {
            assertions++;
            if (!condition) throw new InvalidOperationException(reason);
        }
        var image = new byte[512];
        image[0] = (byte)'M'; image[1] = (byte)'Z'; image[200] = 12;
        var expectedHash = Convert.ToHexString(SHA256.HashData(image)).ToLowerInvariant();
        var temporary = Path.Combine(Path.GetTempPath(), "zyon-install-test-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(temporary, "FiveM.app");
        var plugins = Path.Combine(root, "plugins");
        var module = Path.Combine(plugins, "ZyonClient.asi");
        var metadata = Path.Combine(plugins, "ZyonClient-install.json");
        Directory.CreateDirectory(plugins);
        try
        {
            var inspector = new ClientModuleInstallationInspector(image, "3.3.1");
            var unknown = inspector.Inspect(null);
            Check(unknown.State == ClientModuleInstallationState.Unknown, "No selected root remains unknown.");
            Check(unknown.ModulePath is null && unknown.ExpectedSha256 == expectedHash, "Unknown state keeps expected hash only.");
            var missing = inspector.Inspect(root, "running-process");
            Check(missing.State == ClientModuleInstallationState.Missing, "Missing ASI is reported as missing.");
            Check(missing.RootSource == "running-process" && missing.RootPath == root, "Actual running-root source is preserved.");
            Check(missing.ModulePath == module && missing.ActualSha256 is null, "Missing module path is visible without invented hash.");
            File.WriteAllBytes(module, image);
            var ready = inspector.Inspect(root);
            Check(ready.State == ClientModuleInstallationState.ReadyOnDisk, "Byte-matching module is installed on disk.");
            Check(ready.ActualSha256 == expectedHash && ready.ExpectedSha256 == expectedHash, "Both hashes match actual bytes.");
            Check(ready.MetadataIssue is not null && ready.InstalledVersion is null, "Missing JSON is informational, not module rejection.");
            Check(ready.ModuleLoaded is null && ready.RunningGameProcessId is null, "Disk facts never invent module load or process facts.");
            Check(ready.Reason.Contains("ainda não confirma", StringComparison.Ordinal), "Matching bytes never claim native readiness.");
            File.WriteAllText(metadata, JsonSerializer.Serialize(new { Version = "3.3.1", Sha256 = expectedHash }));
            ready = inspector.Inspect(root);
            Check(ready.MetadataIssue is null && ready.InstalledVersion == "3.3.1", "Valid bounded record yields informational version.");
            File.WriteAllText(metadata, "{truncated-json");
            ready = inspector.Inspect(root);
            Check(ready.State == ClientModuleInstallationState.ReadyOnDisk && ready.MetadataIssue is not null,
                "Malformed record cannot override verified ASI hash.");
            File.WriteAllText(metadata, "[]");
            Check(inspector.Inspect(root).MetadataIssue is not null, "Non-object record is rejected.");
            File.WriteAllText(metadata, new string('x', ClientModuleInstallationInspector.MaximumMetadataBytes + 1));
            Check(inspector.Inspect(root).MetadataIssue is not null, "Oversized record is bounded and ignored.");
            File.WriteAllText(metadata, JsonSerializer.Serialize(new { Version = "<untrusted-version>", Sha256 = expectedHash }));
            ready = inspector.Inspect(root);
            Check(ready.InstalledVersion is null, "Untrusted record cannot insert arbitrary version text.");
            var outdatedImage = (byte[])image.Clone(); outdatedImage[200]++;
            File.WriteAllBytes(module, outdatedImage);
            File.SetLastWriteTimeUtc(module, DateTime.UtcNow.AddSeconds(3));
            var outdated = inspector.Inspect(root);
            Check(outdated.State == ClientModuleInstallationState.Outdated, "Different ASI bytes require update despite matching record hash.");
            Check(outdated.ActualSha256 != expectedHash && outdated.MetadataIssue is not null, "Record mismatch is diagnosed against actual bytes.");
            Check(outdated.SuggestedAction.Contains("Feche o FiveM", StringComparison.Ordinal), "Disk update requires restart, not live replacement.");
            File.WriteAllBytes(module, new byte[ClientModuleInstallationInspector.MaximumModuleBytes + 1]);
            Check(inspector.Inspect(root).State == ClientModuleInstallationState.Outdated, "Oversized module is rejected before allocation/hash.");
            File.WriteAllBytes(module, new byte[12]);
            Check(inspector.Inspect(root).State == ClientModuleInstallationState.Outdated, "Truncated module is rejected.");
            Check(inspector.Inspect("bad\0path").State == ClientModuleInstallationState.Unknown, "Invalid path fails safely.");

            var denied = new ClientModuleInstallationInspector(image, "3.3.1", new FaultFileSystem(new UnauthorizedAccessException()));
            Check(denied.Inspect(root).State == ClientModuleInstallationState.AccessDenied, "Local access denial has its own state.");
            Check(denied.Inspect(root).SuggestedAction.Contains("Não é necessário cargo", StringComparison.Ordinal), "Local permissions are distinct from server admin role.");
            var ioFailure = new ClientModuleInstallationInspector(image, "3.3.1", new FaultFileSystem(new IOException()));
            Check(ioFailure.Inspect(root).State == ClientModuleInstallationState.Unknown, "Unreliable I/O is not misreported as missing.");
            var cacheFiles = new CountingFileSystem(module, image);
            var cached = new ClientModuleInstallationInspector(image, "3.3.1", cacheFiles);
            Check(cached.Inspect(root).State == ClientModuleInstallationState.ReadyOnDisk, "Fake read-only FS verifies image.");
            cached.Inspect(root);
            Check(cacheFiles.Reads == 1, "Identical file stamp reuses bounded hash rather than rereading every UI poll.");
            cacheFiles.Stamp = cacheFiles.Stamp with { LastWriteTimeUtc = cacheFiles.Stamp.LastWriteTimeUtc.AddSeconds(1) };
            cached.Inspect(root);
            Check(cacheFiles.Reads == 2, "Changed stamp invalidates hash cache.");
            cacheFiles.Image = outdatedImage;
            cacheFiles.Stamp = cacheFiles.Stamp with { Length = outdatedImage.Length + 1 };
            Check(cached.Inspect(root).State == ClientModuleInstallationState.Outdated && cacheFiles.Reads == 3,
                "Changed size invalidates cache and actual data controls result.");
            Check(ready.ExpectedVersion == "3.3.1", "Expected version comes from executable, never JSON.");
        }
        finally { Directory.Delete(temporary, recursive: true); }
        return assertions;
    }

    private sealed class FaultFileSystem(Exception error) : IClientModuleInstallationFileSystem
    {
        public ClientModuleFileStamp? GetFileStamp(string path) => throw error;
        public byte[] ReadBounded(string path, int maximumBytes) => throw error;
    }

    private sealed class CountingFileSystem(string modulePath, byte[] image) : IClientModuleInstallationFileSystem
    {
        public byte[] Image { get; set; } = image;
        public ClientModuleFileStamp Stamp { get; set; } = new(image.Length, DateTime.UtcNow);
        public int Reads { get; private set; }
        public ClientModuleFileStamp? GetFileStamp(string path) => path == modulePath ? Stamp : null;
        public byte[] ReadBounded(string path, int maximumBytes) { Reads++; return Image; }
    }
}
