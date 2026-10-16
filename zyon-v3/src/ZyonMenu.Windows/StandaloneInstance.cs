// C# / src/ZyonMenu.Windows/StandaloneInstance.cs / .NET 8 WinForms / Windows 10+ x64 b3258.
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text;
using ZyonMenu.Core;

namespace ZyonMenu.Windows;

internal sealed class StandaloneInstance : IDisposable
{
    private const string WindowTitle = "Zyon v3 — FiveM b3258 / standalone";
    private const string AdminWindowTitle = "Zyon v3 — modo administrativo";
    private readonly Mutex _mutex;
    private bool _disposed;

    public bool IsOwner { get; }

    private StandaloneInstance(Mutex mutex, bool isOwner)
    {
        _mutex = mutex;
        IsOwner = isOwner;
    }

    [SupportedOSPlatform("windows")]
    public static StandaloneInstance Acquire()
    {
        using var identity = WindowsIdentity.GetCurrent(TokenAccessLevels.Query);
        var sid = identity.User?.Value ?? throw new InvalidOperationException("O Windows não forneceu a identificação da sessão atual.");
        // Local is scoped to the current Windows logon/terminal session; the SID separates users.
        return AcquireNamed($@"Local\ZyonMenuStandalone-b3258-{sid}");
    }

    internal static StandaloneInstance AcquireNamed(string name)
    {
        var mutex = new Mutex(initiallyOwned: false, name);
        try
        {
            bool isOwner;
            try { isOwner = mutex.WaitOne(0); }
            catch (AbandonedMutexException) { isOwner = true; }
            return new StandaloneInstance(mutex, isOwner);
        }
        catch
        {
            mutex.Dispose();
            throw;
        }
    }

    [SupportedOSPlatform("windows")]
    public static ExistingInstanceAction HandleExistingInstance(LocalMenuMode requestedMode,
        out string description)
    {
        description = "Outra instância do menu ainda está inicializando ou não pôde ser identificada.";
        var elapsed = Stopwatch.StartNew();
        while (elapsed.ElapsedMilliseconds < 1000)
        {
            nint existing = FindMenuWindow();
            if (existing == 0)
            {
                Thread.Sleep(50);
                continue;
            }
            var title = ReadWindowTitle(existing);
            GetWindowThreadProcessId(existing, out var owner);
            // Cross-published apphosts can retain the .NET runtime's PE FileVersion.
            // The window title comes from the running assembly's AppIdentity instead.
            ExistingInstancePolicy.TryParseModeWindowTitle(title, out var existingVersion, out var mode);
            var action = ExistingInstancePolicy.Decide(AppIdentity.Version, existingVersion,
                requestedMode, mode);
            description = existingVersion is null
                ? "Existe uma cópia com identificação antiga ou sem versão verificável aberta."
                : $"O menu aberto é da versão {existingVersion.Major}.{existingVersion.Minor}.{existingVersion.Build}. " +
                    $"Você está tentando abrir a versão {AppIdentity.DisplayVersion}.";
            if (action == ExistingInstanceAction.Activate)
            {
                AllowSetForegroundWindow(owner);
                ShowWindowAsync(existing, IsIconic(existing) ? 9 : 5);
                SetForegroundWindow(existing);
            }
            return action;
        }
        return ExistingInstanceAction.Unidentified;
    }

    [SupportedOSPlatform("windows")]
    private static nint FindMenuWindow()
    {
        nint fallback = 0;
        EnumWindows((window, parameter) =>
        {
            var title = ReadWindowTitle(window);
            bool matches = title == WindowTitle || title == AdminWindowTitle ||
                ExistingInstancePolicy.TryParseModeWindowTitle(title, out _, out _);
            if (!matches) return true;
            if (fallback == 0) fallback = window;
            if (IsWindowVisible(window))
            {
                fallback = window;
                return false;
            }
            return true;
        }, 0);
        return fallback;
    }

    private static string ReadWindowTitle(nint window)
    {
        int length = GetWindowTextLength(window);
        if (length <= 0 || length > 512) return "";
        var text = new StringBuilder(length + 1);
        return GetWindowText(window, text, text.Capacity) > 0 ? text.ToString() : "";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (IsOwner) _mutex.ReleaseMutex();
        _mutex.Dispose();
    }

    private delegate bool EnumWindowsCallback(nint window, nint parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsCallback callback, nint parameter);

    [DllImport("user32.dll", EntryPoint = "GetWindowTextLengthW", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(nint window);

    [DllImport("user32.dll", EntryPoint = "GetWindowTextW", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint window, StringBuilder text, int maximumCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindowAsync(nint window, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint window);
}
