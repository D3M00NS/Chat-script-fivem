// C# / src/ZyonMenu.Windows/Program.cs / .NET 8 WinForms / Windows 10+ x64.
using ZyonMenu.Core;

namespace ZyonMenu.Windows;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        bool requestAdministration = args.Contains("--admin", StringComparer.OrdinalIgnoreCase);
        bool explicitClientModule = args.Contains("--client-module", StringComparer.OrdinalIgnoreCase);
        bool requestExternal = args.Contains("--external", StringComparer.OrdinalIgnoreCase);
        if (requestAdministration && explicitClientModule || requestExternal && (requestAdministration || explicitClientModule))
        {
            MessageBox.Show("Escolha apenas um modo ao iniciar o menu.", "Zyon", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        bool requestClientModule = !requestAdministration && !requestExternal;
        var mode = requestAdministration ? LocalMenuMode.Administration : requestExternal
            ? LocalMenuMode.External : LocalMenuMode.ClientModule;
        using var instance = StandaloneInstance.Acquire();
        if (!instance.IsOwner)
        {
            var action = StandaloneInstance.HandleExistingInstance(mode, out var description);
            if (action != ExistingInstanceAction.Activate)
            {
                string instructions = action == ExistingInstanceAction.ModeConflict
                    ? "Feche a janela atual antes de abrir outro modo."
                    : "Feche a versão anterior, inclusive se estiver oculta com Insert, e abra este executável novamente.";
                MessageBox.Show(description + Environment.NewLine + Environment.NewLine + instructions,
                    $"Zyon {AppIdentity.DisplayVersion} — outra instância aberta", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            return;
        }
        Application.Run(requestAdministration
            ? new AdminMainForm() : new MainForm(requestClientModule ? new ClientModuleSession() : null));
    }
}
