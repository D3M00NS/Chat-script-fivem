using System.Security.Cryptography;
using System.Text.Json;
using ZyonMenu.Windows;

namespace ZyonMenu.Standalone.Tests;

internal static class LegacyPluginRemovalChecks
{
    public static int Run()
    {
        var assertions = 0;
        void Check(bool condition, string message)
        {
            assertions++;
            if (!condition) throw new InvalidOperationException(message);
        }
        var temporary = Path.Combine(Path.GetTempPath(), "zyon-plugin-removal-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(temporary, "FiveM.app");
        var plugins = Path.Combine(root, "plugins");
        var source = Path.Combine(plugins, "ZyonClient.asi");
        Directory.CreateDirectory(plugins);
        File.WriteAllText(Path.Combine(root, "CitizenFX.ini"), "[Game]\n");
        var image = new byte[512];
        image[0] = (byte)'M'; image[1] = (byte)'Z'; image[320] = 42;
        var hash = Convert.ToHexString(SHA256.HashData(image));
        var clock = new DateTimeOffset(2026, 10, 17, 12, 30, 0, TimeSpan.Zero);
        var remover = new LegacyPluginRemoval([hash], () => clock);
        try
        {
            Check(LegacyPluginRemoval.IsValidRoot(root), "A real local FiveM.app marker is accepted.");
            Check(!LegacyPluginRemoval.IsValidRoot(temporary), "The parent folder is not a FiveM root.");
            Check(!LegacyPluginRemoval.IsValidRoot("FiveM.app"), "Relative paths are refused.");
            Check(!LegacyPluginRemoval.IsValidRoot("\\\\server\\share\\FiveM.app"), "UNC roots are refused.");
            Check(!LegacyPluginRemoval.IsValidRoot("//server/share/FiveM.app"), "Slash UNC roots are refused.");
            Check(!LegacyPluginRemoval.IsValidRoot("invalid\0path"), "Invalid paths fail safely.");

            Check(remover.Disable(root, () => false).State == LegacyPluginRemovalState.Missing, "A missing plugin is harmless.");
            File.WriteAllBytes(source, image);
            var otherPlugin = Path.Combine(plugins, "OtherPlugin.asi");
            var gameDll = Path.Combine(root, "ScriptHookV.dll");
            var record = Path.Combine(plugins, "ZyonClient-install.json");
            File.WriteAllText(otherPlugin, "other plugin");
            File.WriteAllText(gameDll, "game managed library");
            File.WriteAllText(record, JsonSerializer.Serialize(new { Product = "Zyon client module", Sha256 = hash }));
            var running = remover.Disable(root, () => true);
            Check(running.State == LegacyPluginRemovalState.GameRunning && File.Exists(source), "An open game prevents modification.");
            var calls = 0;
            Check(remover.Disable(root, () => ++calls == 2).State == LegacyPluginRemovalState.GameRunning && calls == 2 && File.Exists(source),
                "The game is checked again immediately before a move.");
            var untrusted = new LegacyPluginRemoval().Disable(root, () => false);
            Check(untrusted.State == LegacyPluginRemovalState.UnknownFile && File.Exists(source), "Production trust cannot be supplied by matching local metadata.");
            var collision = source + ".20261017-123000.disabled";
            File.WriteAllText(collision, "existing backup");
            var disabled = remover.Disable(root, () => false);
            Check(disabled.State == LegacyPluginRemovalState.Disabled && disabled.BackupPath == source + ".20261017-123000-1.disabled",
                "An existing backup gets a unique name without being overwritten.");
            Check(!File.Exists(source) && File.ReadAllBytes(disabled.BackupPath!).SequenceEqual(image), "The verified plugin is preserved with a non-ASI extension.");
            Check(File.ReadAllText(collision) == "existing backup", "An older backup is unchanged.");
            Check(File.ReadAllText(otherPlugin) == "other plugin" && File.ReadAllText(gameDll) == "game managed library",
                "Other plugins and FiveM libraries are preserved.");
            Check(File.Exists(record), "Historical installation metadata is preserved.");
            Check(remover.Disable(root, () => false).State == LegacyPluginRemovalState.Missing, "Disabling twice has no additional side effect.");

            File.WriteAllBytes(source, image);
            var different = image.ToArray(); different[320]++;
            File.WriteAllBytes(source, different);
            Check(remover.Disable(root, () => false).State == LegacyPluginRemovalState.UnknownFile && File.ReadAllBytes(source).SequenceEqual(different),
                "A changed plugin is preserved even if its name matches.");
            File.WriteAllBytes(source, new byte[LegacyPluginRemoval.MaximumPluginBytes + 1]);
            Check(remover.Disable(root, () => false).State == LegacyPluginRemovalState.Failed && File.Exists(source), "Oversized plugins are bounded and preserved.");
            File.WriteAllBytes(source, new byte[12]);
            Check(remover.Disable(root, () => false).State == LegacyPluginRemovalState.Failed && File.Exists(source), "Truncated images are preserved.");
            File.Delete(source);
            Directory.CreateDirectory(source);
            Check(remover.Disable(root, () => false).State == LegacyPluginRemovalState.InvalidLocation && Directory.Exists(source), "A directory named as a plugin is preserved.");
            Directory.Delete(source);

            var preference = Path.Combine(temporary, "client-module-location.json");
            File.WriteAllText(preference, JsonSerializer.Serialize(new { FiveMRoot = root }));
            Check(LegacyPluginRemoval.ReadPreferredRoot(preference) == root, "An existing local preference selects a validated root.");
            File.WriteAllText(preference, "{unfinished");
            Check(LegacyPluginRemoval.ReadPreferredRoot(preference) is null, "Malformed preference is ignored.");
            File.WriteAllText(preference, "[]");
            Check(LegacyPluginRemoval.ReadPreferredRoot(preference) is null, "Non-object preference is ignored.");
            File.WriteAllText(preference, JsonSerializer.Serialize(new { FiveMRoot = temporary }));
            Check(LegacyPluginRemoval.ReadPreferredRoot(preference) is null, "A preference cannot bypass FiveM root validation.");
            File.WriteAllText(preference, new string(' ', LegacyPluginRemoval.MaximumPreferenceBytes + 1));
            Check(LegacyPluginRemoval.ReadPreferredRoot(preference) is null, "Preference reads have a 64 KiB bound.");
            File.Delete(preference);
            Check(LegacyPluginRemoval.ReadPreferredRoot(preference) is null, "A missing preference is harmless.");

            var linked = Path.Combine(temporary, "Linked", "FiveM.app");
            Directory.CreateDirectory(Path.GetDirectoryName(linked)!);
            try
            {
                Directory.CreateSymbolicLink(linked, root);
                Check(!LegacyPluginRemoval.IsValidRoot(linked), "A linked FiveM root is refused.");
                Directory.Delete(linked);
                var outside = Path.Combine(temporary, "outside.asi");
                File.WriteAllBytes(outside, image);
                File.CreateSymbolicLink(source, outside);
                Check(remover.Disable(root, () => false).State == LegacyPluginRemovalState.InvalidLocation && File.Exists(outside),
                    "A linked plugin cannot move a file outside the validated root.");
                File.Delete(source);
            }
            catch (Exception error) when (error is PlatformNotSupportedException or UnauthorizedAccessException)
            { /* Windows may require a local privilege to create test-only symlinks. */ }
        }
        finally { Directory.Delete(temporary, recursive: true); }
        return assertions;
    }
}
