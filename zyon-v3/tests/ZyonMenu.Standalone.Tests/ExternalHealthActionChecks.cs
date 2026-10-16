// C# / tests/ZyonMenu.Standalone.Tests/ExternalHealthActionChecks.cs / .NET 8 / one-shot action and stale-work regression tests.
using ZyonMenu.Windows;

namespace ZyonMenu.Standalone.Tests;

internal static class ExternalHealthActionChecks
{
    public static int Run()
    {
        int assertions = 0;
        void Check(bool condition, string message)
        {
            assertions++;
            if (!condition) throw new InvalidOperationException(message);
        }
        void Throws<T>(Action action, string message) where T : Exception
        {
            assertions++;
            try { action(); }
            catch (T) { return; }
            throw new InvalidOperationException(message);
        }

        var target = new FakeTarget();
        var trace = new HealthActionTrace();
        ExternalHealthAction.Execute(target, trace, CancellationToken.None);
        Check(target.Health == 0 && target.Writes == 1 && target.GodDisables == 1,
            "A successful request must restore owned GodMode and write health zero exactly once.");
        Check(trace.Before == 200 && trace.Readback == 0 && trace.WriteAttempted && trace.GodRestoreConfirmed,
            "A successful action must preserve both health observations and the write result.");

        target = new FakeTarget { GodRestoreResult = false };
        trace = new HealthActionTrace();
        Throws<InvalidOperationException>(() => ExternalHealthAction.Execute(target, trace, CancellationToken.None),
            "A failed God restoration must block the health write.");
        Check(target.Writes == 0 && target.Health == 200 && !trace.WriteAttempted,
            "An unconfirmed owned flag restoration must leave health untouched.");

        target = new FakeTarget();
        target.AfterGodDisable = () => target.Current = false;
        trace = new HealthActionTrace();
        Throws<InvalidOperationException>(() => ExternalHealthAction.Execute(target, trace, CancellationToken.None),
            "Replacing the ped between God restoration and writing must cancel the action.");
        Check(target.Writes == 0, "The action must not write a replacement ped or stale owner.");

        target = new FakeTarget();
        target.AfterWrite = () => target.Current = false;
        trace = new HealthActionTrace();
        Throws<InvalidOperationException>(() => ExternalHealthAction.Execute(target, trace, CancellationToken.None),
            "Replacing the target after the write must not produce a successful observation.");
        Check(target.Writes == 1 && trace.Readback is null,
            "A lost target after an in-flight write must never cause an automatic second write.");

        target = new FakeTarget();
        target.AfterWrite = () => target.Health = 100;
        trace = new HealthActionTrace();
        Throws<IOException>(() => ExternalHealthAction.Execute(target, trace, CancellationToken.None),
            "A health value overwritten before readback must report failure instead of claiming death.");
        Check(target.Writes == 1 && trace.Readback == 100,
            "An overwritten health action must keep its actual readback and must not retry silently.");

        using var preCancelled = new CancellationTokenSource();
        preCancelled.Cancel();
        target = new FakeTarget();
        trace = new HealthActionTrace();
        Throws<OperationCanceledException>(() => ExternalHealthAction.Execute(target, trace, preCancelled.Token),
            "A request cancelled or timed out while queued must not run when the lock becomes free.");
        Check(target.Reads == 0 && target.Writes == 0 && target.GodDisables == 0,
            "A cancelled queued action must have no memory side effects.");

        using var cancelledAfterRestore = new CancellationTokenSource();
        target = new FakeTarget { AfterGodDisable = cancelledAfterRestore.Cancel };
        trace = new HealthActionTrace();
        Throws<OperationCanceledException>(() => ExternalHealthAction.Execute(target, trace, cancelledAfterRestore.Token),
            "Cancellation after flag restoration must be checked before the health write.");
        Check(target.Writes == 0 && trace.GodRestoreConfirmed,
            "Cancelling between steps must preserve the completed restoration without writing health.");

        using var cancelledDuringValidation = new CancellationTokenSource();
        target = new FakeTarget();
        target.OnValidation = count => { if (count == 3) cancelledDuringValidation.Cancel(); };
        trace = new HealthActionTrace();
        Throws<OperationCanceledException>(() => ExternalHealthAction.Execute(target, trace, cancelledDuringValidation.Token),
            "A deadline expiring inside identity reads must be rechecked before the health write.");
        Check(target.Writes == 0 && trace.GodRestoreConfirmed,
            "Cancellation inside the final target validation must never cause a late queued write.");

        using var cancelledAfterWrite = new CancellationTokenSource();
        target = new FakeTarget { AfterWrite = cancelledAfterWrite.Cancel };
        trace = new HealthActionTrace();
        Throws<OperationCanceledException>(() => ExternalHealthAction.Execute(target, trace, cancelledAfterWrite.Token),
            "Cancellation during an in-flight write must not be presented as a successful readback.");
        Check(target.Writes == 1 && trace.WriteAttempted && trace.Readback is null,
            "An in-flight write can complete before cancellation, but it must never be replayed.");

        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, -1001f, 10001f })
        {
            target = new FakeTarget { Health = invalid };
            trace = new HealthActionTrace();
            Throws<InvalidOperationException>(() => ExternalHealthAction.Execute(target, trace, CancellationToken.None),
                "Invalid health must block the action before any write.");
            Check(target.Writes == 0 && target.GodDisables == 0, "An invalid profile must not modify flags or health.");
        }

        var fence = new LocalActionFence();
        long oldSettings = fence.CaptureSettings();
        long action = fence.BeginAction();
        Check(fence.IsCurrentAction(action), "A new action must own the current generation.");
        Throws<InvalidOperationException>(() => fence.ValidateSettings(oldSettings),
            "Settings captured before the action must not re-enable GodMode afterward.");
        Throws<InvalidOperationException>(() => fence.CaptureSettings(), "New settings must be rejected while an action is pending.");
        Throws<InvalidOperationException>(() => fence.BeginAction(), "A second concurrent action must be rejected rather than replayed.");
        fence.Invalidate();
        Check(!fence.IsCurrentAction(action), "Disconnect or ped replacement must invalidate the queued action.");
        fence.EndAction();
        Throws<InvalidOperationException>(() => fence.ValidateSettings(oldSettings),
            "Ending an action must not revive old queued configuration snapshots.");
        long currentSettings = fence.CaptureSettings();
        fence.ValidateSettings(currentSettings);
        Check(!fence.IsCurrentAction(action), "Completed or invalidated action identifiers must remain unusable.");
        return assertions;
    }

    private sealed class FakeTarget : IExternalHealthTarget
    {
        public bool Current { get; set; } = true;
        public bool IsCurrent { get { Validations++; OnValidation?.Invoke(Validations); return Current; } }
        public float Health { get; set; } = 200;
        public bool GodRestoreResult { get; set; } = true;
        public Action? AfterGodDisable { get; set; }
        public Action? AfterWrite { get; set; }
        public Action<int>? OnValidation { get; set; }
        public int Validations { get; private set; }
        public int Reads { get; private set; }
        public int Writes { get; private set; }
        public int GodDisables { get; private set; }
        public float ReadHealth() { Reads++; return Health; }
        public bool DisableOwnedGodMode() { GodDisables++; AfterGodDisable?.Invoke(); return GodRestoreResult; }
        public void WriteHealth(float value) { Writes++; Health = value; AfterWrite?.Invoke(); }
    }
}
