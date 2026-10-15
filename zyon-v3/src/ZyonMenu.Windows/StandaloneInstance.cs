// C# / src/ZyonMenu.Windows/StandaloneInstance.cs / .NET 8 WinForms / Windows 10+ x64 b3258.
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text;

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
    public static bool ActivateExistingWindow()
    {
        // The owner may still be creating its window; bound startup retries to one second.
        var elapsed = Stopwatch.StartNew();
        while (elapsed.ElapsedMilliseconds < 1000)
        {
            nint existing = 0;
            EnumWindows((window, _) =>
            {
                var length = GetWindowTextLength(window);
                if (length != WindowTitle.Length && length != AdminWindowTitle.Length) return true;
                var title = new StringBuilder(length + 1);
                if (GetWindowText(window, title, title.Capacity) > 0 &&
                    (title.ToString() == WindowTitle || title.ToString() == AdminWindowTitle))
                {
                    // Prefer the active mode, but retain a hidden window so reopening also works after Insert.
                    if (existing == 0) existing = window;
                    if (IsWindowVisible(window))
                    {
                        existing = window;
                        return false;
                    }
                }
                return true;
            }, 0);
            if (existing != 0)
            {
                GetWindowThreadProcessId(existing, out var owner);
                AllowSetForegroundWindow(owner);
                ShowWindowAsync(existing, IsIconic(existing) ? 9 : 5);
                SetForegroundWindow(existing);
                return true;
            }
            Thread.Sleep(50);
        }
        return false;
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
