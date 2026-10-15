// C# / src/ZyonMenu.Windows/Program.cs / .NET 8 WinForms / Windows 10+ x64.
namespace ZyonMenu.Windows;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        using var instance = StandaloneInstance.Acquire();
        if (!instance.IsOwner)
        {
            StandaloneInstance.ActivateExistingWindow();
            return;
        }
        ApplicationConfiguration.Initialize();
        Application.Run(args.Contains("--admin", StringComparer.OrdinalIgnoreCase)
            ? new AdminMainForm() : new MainForm());
    }
}
