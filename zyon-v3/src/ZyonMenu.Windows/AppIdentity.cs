using System.Reflection;

namespace ZyonMenu.Windows;

internal static class AppIdentity
{
    public static Version Version { get; } = ReadVersion();
    public static string DisplayVersion => $"{Version.Major}.{Version.Minor}.{Version.Build}";
    public static string StandaloneTitle => $"Zyon {DisplayVersion} — FiveM b3258 / standalone";
    public static string AdministrativeTitle => $"Zyon {DisplayVersion} — modo administrativo";

    private static Version ReadVersion()
    {
        var text = typeof(AppIdentity).Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version;
        return System.Version.TryParse(text, out var version) ? version : new Version(0, 0, 0, 0);
    }
}
