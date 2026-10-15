// C# / src/ZyonMenu.Windows/EspOverlayHost.cs / .NET 8 WinForms / isolated overlay message loop.
namespace ZyonMenu.Windows;

internal sealed class EspOverlayHost : IDisposable
{
    private readonly Thread _thread;
    private readonly TaskCompletionSource<EspOverlay> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Exception? _failure;
    private int _disposed;

    public EspOverlayHost(Func<OverlayFrame> readFrame)
    {
        ArgumentNullException.ThrowIfNull(readFrame);
        _thread = new Thread(() => Run(readFrame)) { IsBackground = true, Name = "Zyon ESP overlay" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        // Startup completes only after a handle exists, so even an immediate shutdown can be marshalled safely.
        _ready.Task.GetAwaiter().GetResult();
    }

    public bool IsRunning => _thread.IsAlive && Failure is null;
    public Exception? Failure => Volatile.Read(ref _failure);

    private void Run(Func<OverlayFrame> readFrame)
    {
        try
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException, true);
            using var overlay = new EspOverlay(readFrame);
            _ = overlay.Handle;
            _ready.TrySetResult(overlay);
            // No main form is shown automatically: EspOverlay controls its own visibility from fresh frames.
            Application.Run();
        }
        catch (Exception error)
        {
            Volatile.Write(ref _failure, error);
            _ready.TrySetException(error);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        if (!_thread.IsAlive) return;
        if (Thread.CurrentThread == _thread)
        {
            Application.ExitThread();
            return;
        }
        var overlay = _ready.Task.GetAwaiter().GetResult();
        try
        {
            overlay.BeginInvoke(new Action(() =>
            {
                overlay.Close();
                Application.ExitThread();
            }));
        }
        catch (InvalidOperationException) when (overlay.IsDisposed || !_thread.IsAlive) { }
        // The form and its timer are disposed by the owning STA thread after its message loop exits.
        _thread.Join();
    }
}
