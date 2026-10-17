using System.ComponentModel;
using System.Diagnostics;

namespace ZyonMenu.Windows;

internal static class LegacyPluginRemovalDialog
{
    public static void Show(IWin32Window owner)
    {
        try
        {
            if (GameIsRunning())
            {
                MessageBox.Show(owner, "Feche o FiveM antes de desativar o plugin antigo. O menu atual já funciona sem esse plugin.",
                    "Desativar plugin antigo", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var root = LegacyPluginRemoval.ReadPreferredRoot(Path.Combine(local, "ZyonMenu", "client-module-location.json"));
            var defaultRoot = Path.Combine(local, "FiveM", "FiveM.app");
            if (root is null && LegacyPluginRemoval.IsValidRoot(defaultRoot)) root = defaultRoot;
            if (root is null)
            {
                using var picker = new FolderBrowserDialog
                {
                    Description = "Selecione FiveM.app. Somente o antigo ZyonClient.asi será desativado e preservado com extensão .disabled.",
                    UseDescriptionForTitle = true,
                    ShowNewFolderButton = false,
                    SelectedPath = Directory.Exists(defaultRoot) ? defaultRoot : local
                };
                if (picker.ShowDialog(owner) != DialogResult.OK) return;
                root = picker.SelectedPath;
            }
            var result = new LegacyPluginRemoval().Disable(root, GameIsRunning);
            var message = result.Message + (result.BackupPath is null ? "" : "\n\nCópia preservada:\n" + result.BackupPath);
            MessageBox.Show(owner, message, "Desativar plugin antigo", MessageBoxButtons.OK,
                result.State is LegacyPluginRemovalState.Disabled or LegacyPluginRemovalState.Missing or LegacyPluginRemovalState.GameRunning
                    ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or Win32Exception)
        {
            MessageBox.Show(owner, "Não foi possível verificar os processos locais. Feche o FiveM e tente novamente. Nenhum arquivo foi alterado.",
                "Desativar plugin antigo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
                    if (name.Equals("FiveM", StringComparison.OrdinalIgnoreCase) || name.StartsWith("FiveM_", StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                catch (InvalidOperationException) { /* Process exited during enumeration. */ }
                // An unreadable process list fails closed through the caller's error message.
            }
            return false;
        }
        finally { foreach (var process in processes) process.Dispose(); }
    }
}
