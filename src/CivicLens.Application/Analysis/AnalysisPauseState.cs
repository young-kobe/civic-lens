namespace CivicLens.Application.Analysis;

internal sealed class AnalysisPauseState(TimeProvider clock)
{
    private readonly Lock gate = new();
    private int consecutiveOutages;
    private int outagePauses;
    private bool pausedUntilRestart;
    private DateTimeOffset? pausedUntil;

    public bool IsPaused(out DateTimeOffset? resumeAt)
    {
        lock (gate)
        {
            if (pausedUntil <= clock.GetUtcNow()) pausedUntil = null;
            resumeAt = pausedUntil;
            return pausedUntilRestart || pausedUntil is not null;
        }
    }

    public bool OutageBoundReached() => Interlocked.Increment(ref consecutiveOutages) >= AnalysisPolicy.OutageFailureBound;

    public TimeSpan NextOutagePause() => AnalysisPolicy.OutagePause(Interlocked.Increment(ref outagePauses) - 1);

    public void Pause(TimeSpan? duration)
    {
        lock (gate)
        {
            if (duration is null) pausedUntilRestart = true;
            else pausedUntil = clock.GetUtcNow() + duration.Value;
        }
        Interlocked.Exchange(ref consecutiveOutages, 0);
    }

    public bool RecordSuccessfulCall()
    {
        Interlocked.Exchange(ref consecutiveOutages, 0);
        return Interlocked.Exchange(ref outagePauses, 0) > 0;
    }
}
