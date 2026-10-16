using System.Diagnostics;
using ZyonMenu.Core;
using ZyonMenu.Windows;

namespace ZyonMenu.Standalone.Tests;

internal static class ExistingInstanceChecks
{
    public static bool TryHandleChild(string[] args, out int exitCode)
    {
        exitCode = 0;
        if (args.Length != 2 || args[0] is not ("--mutex-probe" or "--mutex-abandon")) return false;
        using var instance = StandaloneInstance.AcquireNamed(args[1]);
        if (!instance.IsOwner) { exitCode = 17; return true; }
        if (args[0] == "--mutex-abandon") Environment.Exit(0);
        return true;
    }

    public static int Run()
    {
        var assertions = 0;
        void Check(bool condition, string message)
        {
            assertions++;
            if (!condition) throw new InvalidOperationException(message);
        }
        var current = new Version(3, 1, 2, 0);
        Check(ExistingInstancePolicy.TryParseModeWindowTitle("Zyon 3.2.0 — FiveM b3258 / cliente local", out var moduleVersion, out var moduleMode) &&
            moduleVersion == new Version(3, 2, 0, 0) && moduleMode == LocalMenuMode.ClientModule,
            "A client module must have a distinct startup identity.");
        Check(ExistingInstancePolicy.Decide(new Version(3, 2, 0, 0), moduleVersion, LocalMenuMode.External, moduleMode) == ExistingInstanceAction.ModeConflict,
            "Reopening the external backend must not silently activate a client-module session.");
        Check(ExistingInstancePolicy.Decide(new Version(3, 2, 0, 0), moduleVersion, LocalMenuMode.ClientModule, LocalMenuMode.External) == ExistingInstanceAction.ModeConflict,
            "A client-module launch must not reactivate the external backend.");
        Check(ExistingInstancePolicy.Decide(new Version(3, 2, 0, 0), moduleVersion, LocalMenuMode.ClientModule, moduleMode) == ExistingInstanceAction.Activate,
            "The same client-module version can reactivate its window.");
        Check(!ExistingInstancePolicy.TryParseWindowTitle("Zyon 3.2.0 — FiveM b3258 / cliente local", out _, out _),
            "The legacy two-mode parser must not mistake the module for the external backend.");
        Check(ExistingInstancePolicy.TryParseModeWindowTitle(AppIdentity.ClientModuleTitle, out moduleVersion, out moduleMode) &&
            moduleVersion == AppIdentity.Version && moduleMode == LocalMenuMode.ClientModule,
            "AppIdentity and the client-module startup parser must agree.");
        Check(ExistingInstancePolicy.TryParseWindowTitle("Zyon 3.1.2 — FiveM b3258 / standalone", out var titleVersion, out var titleMode) &&
            titleVersion == current && titleMode == false, "The running title must normalize its three components to the four-component assembly version.");
        Check(ExistingInstancePolicy.Decide(current, titleVersion, false, titleMode) == ExistingInstanceAction.Activate,
            "The same version must reactivate even when the executable's PE version belongs to the .NET apphost.");
        Check(ExistingInstancePolicy.TryParseWindowTitle("Zyon 3.1.3 — FiveM b3258 / standalone", out titleVersion, out titleMode) &&
            ExistingInstancePolicy.Decide(current, titleVersion, false, titleMode) == ExistingInstanceAction.VersionConflict,
            "A future versioned title must be found and treated as a version conflict.");
        Check(ExistingInstancePolicy.TryParseWindowTitle("Zyon 3.1.2 — modo administrativo", out titleVersion, out titleMode) &&
            ExistingInstancePolicy.Decide(current, titleVersion, false, titleMode) == ExistingInstanceAction.ModeConflict,
            "A versioned administrative title must be identified without changing the default mode.");
        Check(ExistingInstancePolicy.TryParseWindowTitle(AppIdentity.StandaloneTitle, out titleVersion, out titleMode) &&
            titleVersion == AppIdentity.Version && titleMode == false, "AppIdentity and the parser must share the actual title contract.");
        foreach (var invalid in new[] { "Zyon v3 — FiveM b3258 / standalone", "Zyon 3.1 — FiveM b3258 / standalone",
            "Zyon 3.1.2.0 — FiveM b3258 / standalone", "Zyon 03.1.2 — FiveM b3258 / standalone",
            "Zyon 3.1.2 — other", "Other 3.1.2 — FiveM b3258 / standalone", "Zyon 3.1.2 — FiveM b3258 / standalone extra" })
            Check(!ExistingInstancePolicy.TryParseWindowTitle(invalid, out titleVersion, out titleMode) && titleVersion is null,
                "Legacy or malformed titles must not be treated as a verified matching menu.");
        Check(ExistingInstancePolicy.Decide(current, new(3, 0, 0, 0), false, false) == ExistingInstanceAction.VersionConflict,
            "Opening a new menu must not silently reactivate the 3.0 executable.");
        Check(ExistingInstancePolicy.Decide(current, new(3, 1, 1, 0), false, false) == ExistingInstanceAction.VersionConflict,
            "The previous package must be identified as a different version.");
        Check(ExistingInstancePolicy.Decide(current, new(3, 2, 0, 0), false, false) == ExistingInstanceAction.VersionConflict,
            "Opening an older package must not silently attach to a newer instance.");
        Check(ExistingInstancePolicy.Decide(current, current, false, true) == ExistingInstanceAction.ModeConflict,
            "A standalone launch must not silently reactivate the administrative mode.");
        Check(ExistingInstancePolicy.Decide(current, current, true, false) == ExistingInstanceAction.ModeConflict,
            "An explicit administrative launch must not reactivate the external window.");
        Check(ExistingInstancePolicy.Decide(current, current, false, false) == ExistingInstanceAction.Activate,
            "The same standalone version can activate its current window.");
        Check(ExistingInstancePolicy.Decide(current, null, false, false) == ExistingInstanceAction.Unidentified,
            "Unverifiable versions must not be treated as matching.");
        Check(ExistingInstancePolicy.Decide(current, current, false, null) == ExistingInstanceAction.Unidentified,
            "An unverifiable mode must not be assumed standalone.");

        // Keep this method synchronous: a Mutex must be released by its acquiring thread.
        var name = "ZyonStartupChecks-" + Guid.NewGuid().ToString("N");
        using (var owner = StandaloneInstance.AcquireNamed(name))
        {
            Check(owner.IsOwner, "Initial process must acquire the mutex.");
            Check(Probe("--mutex-probe", name) == 17, "A second process must not create another game writer.");
        }
        Check(Probe("--mutex-probe", name) == 0, "The mutex must become available after a clean shutdown.");
        Check(Probe("--mutex-abandon", name) == 0, "Abandonment test process must own its mutex.");
        Check(Probe("--mutex-probe", name) == 0, "A terminated owner must not permanently block startup.");
        return assertions;
    }

    private static int Probe(string action, string name)
    {
        string executable = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot locate test host.");
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(typeof(ExistingInstanceChecks).Assembly.Location);
        start.ArgumentList.Add(action);
        start.ArgumentList.Add(name);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start mutex probe.");
        if (!process.WaitForExit(10000))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            throw new TimeoutException("Mutex probe did not complete.");
        }
        return process.ExitCode;
    }
}
