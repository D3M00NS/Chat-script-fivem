// C# / src/ZyonMenu.Windows/ExternalHealthAction.cs / .NET 8 / one-shot health write with identity and cancellation guards.
namespace ZyonMenu.Windows;

internal interface IExternalHealthTarget
{
    bool IsCurrent { get; }
    float ReadHealth();
    bool DisableOwnedGodMode();
    void WriteHealth(float value);
}

internal sealed class HealthActionTrace
{
    public float? Before { get; set; }
    public float? Readback { get; set; }
    public bool GodRestoreConfirmed { get; set; }
    public bool WriteAttempted { get; set; }
}

internal static class ExternalHealthAction
{
    public static void Execute(IExternalHealthTarget target, HealthActionTrace trace, CancellationToken cancellationToken)
    {
        void CheckTarget()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!target.IsCurrent) throw new InvalidOperationException("A identidade do processo/personagem mudou; a ação foi interrompida.");
            // Identity validation may itself read the game process; cancellation can arrive during those reads.
            cancellationToken.ThrowIfCancellationRequested();
        }

        CheckTarget();
        float before = target.ReadHealth();
        trace.Before = before;
        if (!float.IsFinite(before) || before is < -1000 or > 10000)
            throw new InvalidOperationException("A saúde anterior não passou pela validação do perfil.");
        CheckTarget();
        trace.GodRestoreConfirmed = target.DisableOwnedGodMode();
        if (!trace.GodRestoreConfirmed)
            throw new InvalidOperationException("A restauração do GodMode pertencente ao menu não foi confirmada; a saúde não foi escrita.");
        CheckTarget();
        trace.WriteAttempted = true;
        target.WriteHealth(0f);
        CheckTarget();
        float readback = target.ReadHealth();
        trace.Readback = readback;
        CheckTarget();
        if (readback != 0f)
            throw new IOException("A saúde zero não foi observada no retorno; o jogo ou o servidor pode ter alterado o valor.");
    }
}

internal sealed class LocalActionFence
{
    private long _generation;
    private int _running;

    public long CaptureSettings()
    {
        long generation = Volatile.Read(ref _generation);
        ValidateSettings(generation);
        return generation;
    }

    public void ValidateSettings(long generation)
    {
        if (Volatile.Read(ref _running) != 0 || generation != Volatile.Read(ref _generation))
            throw new InvalidOperationException("A configuração anterior ou concorrente à ação foi descartada; envie as escolhas atuais novamente.");
    }

    public long BeginAction()
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
            throw new InvalidOperationException("Já existe uma ação local em andamento.");
        return Interlocked.Increment(ref _generation);
    }

    public bool IsCurrentAction(long generation) => Volatile.Read(ref _running) != 0 && generation == Volatile.Read(ref _generation);
    public void EndAction() => Interlocked.Exchange(ref _running, 0);
    public void Invalidate() => Interlocked.Increment(ref _generation);
}

internal sealed record LocalHealthActionTelemetry(long Id, string State, string Message, DateTimeOffset RequestedUtc,
    DateTimeOffset? FinishedUtc, string Ped, long SessionEpoch, float? HealthBefore, float? HealthReadback,
    bool GodRestoreConfirmed, bool WriteAttempted)
{
    public static LocalHealthActionTelemetry Empty { get; } = new(0, "ready", "Nenhuma ação de saúde solicitada.", default,
        null, "0x0", 0, null, null, false, false);
}
