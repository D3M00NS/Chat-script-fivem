// C# / Windows client-only module setup. No server files or remote installation.
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace ZyonMenu.Windows;

internal static class ClientModuleInstaller
{
    internal const string ResourceName = "ZyonMenu.ClientModule.asi";

    public static void ShowSetup(IWin32Window owner)
    {
        try
        {
            if (GameIsRunning())
            {
                MessageBox.Show(owner, "Feche o FiveM antes de preparar o módulo local. Depois da instalação, abra o FiveM novamente.",
                    "Módulo local", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using var image = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
                ?? throw new InvalidOperationException("Este executável não contém o módulo local. Use o pacote completo da versão 3.3.1 ou posterior.");
            using var buffer = new MemoryStream();
            image.CopyTo(buffer);
            var bytes = buffer.ToArray();
            if (bytes.Length is < 256 or > 16 * 1024 * 1024 || bytes[0] != 'M' || bytes[1] != 'Z')
                throw new InvalidDataException("O módulo incluído no executável não passou pela verificação de integridade.");
            using var picker = new FolderBrowserDialog
            {
                Description = "Selecione a pasta FiveM.app da sua instalação do FiveM. O módulo será copiado apenas para plugins.",
                UseDescriptionForTitle = true,
                SelectedPath = ClientModuleInstallation.Refresh().RootPath ?? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FiveM", "FiveM.app"),
                ShowNewFolderButton = false
            };
            if (picker.ShowDialog(owner) != DialogResult.OK) return;
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(picker.SelectedPath));
            if (!Directory.Exists(root) || !Path.GetFileName(root).Equals("FiveM.app", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Selecione a pasta FiveM.app, e não a pasta do GTA ou do servidor.");
            if (root.StartsWith("\\\\", StringComparison.Ordinal) || !LooksLikeFiveMRoot(root))
                throw new InvalidOperationException("Esta pasta não contém os arquivos de uma instalação local do FiveM. Selecione a FiveM.app usada para abrir seu jogo.");
            if (GameIsRunning())
                throw new InvalidOperationException("O FiveM foi iniciado durante a preparação. Feche o jogo e tente novamente.");
            var plugins = Path.Combine(root, "plugins");
            Directory.CreateDirectory(plugins);
            var destination = Path.Combine(plugins, "ZyonClient.asi");
            var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            string? backup = null;
            bool unchanged = false;
            if (File.Exists(destination))
            {
                var previousHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(destination))).ToLowerInvariant();
                unchanged = previousHash == hash;
                if (!unchanged)
                {
                    backup = destination + ".previous-" + previousHash + ".bak";
                    if (!File.Exists(backup)) File.Copy(destination, backup);
                    if (Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(backup))).ToLowerInvariant() != previousHash)
                        throw new IOException("Não foi possível preservar a cópia anterior do módulo.");
                }
            }
            var temporary = Path.Combine(plugins, ".zyon-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                if (!unchanged)
                {
                    File.WriteAllBytes(temporary, bytes);
                    File.Move(temporary, destination, overwrite: true);
                }
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            if (Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(destination))).ToLowerInvariant() != hash)
                throw new IOException("A cópia do módulo local não corresponde ao arquivo incluído no executável.");
            File.WriteAllText(Path.Combine(plugins, "ZyonClient-install.json"), JsonSerializer.Serialize(new
            {
                Product = "Zyon client module", Version = AppIdentity.DisplayVersion,
                Sha256 = hash, InstalledUtc = DateTimeOffset.UtcNow,
                PreviousBackup = backup is null ? null : Path.GetFileName(backup)
            }, new JsonSerializerOptions { WriteIndented = true }));
            ClientModuleInstallation.RememberSelectedRoot(root);
            MessageBox.Show(owner,
                "Módulo preparado em:\n" + destination +
                "\n\nAbra o FiveM novamente e mantenha o menu no modo CLIENTE LOCAL. O aplicativo detectará o módulo automaticamente." +
                "\n\nO servidor precisa permitir plugins/ScriptHook. O menu informará quando o módulo não estiver disponível." +
                "\n\nPara remover, feche o FiveM e apague somente ZyonClient.asi e ZyonClient-install.json da pasta plugins." +
                (backup is null ? "" : "\nA cópia anterior foi preservada com extensão .bak."),
                "Módulo local preparado", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(owner, error.Message, "Módulo local não preparado", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static bool GameIsRunning()
    {
        var processes = Process.GetProcesses();
        try
        {
            foreach (var process in processes)
            {
                try
                {
                    var name = process.ProcessName;
                    if (name.Equals("FiveM", StringComparison.OrdinalIgnoreCase) ||
                        name.StartsWith("FiveM_", StringComparison.OrdinalIgnoreCase)) return true;
                }
                catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception)
                { /* A process can exit during enumeration. */ }
            }
            return false;
        }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    private static bool LooksLikeFiveMRoot(string root) => File.Exists(Path.Combine(root, "CitizenFX.ini")) ||
        Directory.Exists(Path.Combine(root, "citizen")) || Directory.Exists(Path.Combine(root, "data", "cache", "subprocess"));
}
