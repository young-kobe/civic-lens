using CivicLens.Application;
using CivicLens.Application.Analysis;

namespace CivicLens.Host.Analysis;

internal sealed class AnalysisComposition<TRun> where TRun : class
{
    private readonly IAnalysisStatusStore status;
    private readonly long? dailyTokenLimit;
    private readonly string? pauseReason;

    public AnalysisComposition(IAnalysisStatusStore status, long? dailyTokenLimit, AnalysisWorker<TRun>? worker,
        IWorkerWakeup? wakeup, string? pauseReason = null)
    {
        this.status = status;
        this.dailyTokenLimit = dailyTokenLimit;
        this.pauseReason = pauseReason;
        Worker = worker;
        Wakeup = wakeup;
    }

    public AnalysisWorker<TRun>? Worker { get; }
    public IWorkerWakeup? Wakeup { get; }

    public static AnalysisComposition<TRun> Disabled(IAnalysisStatusStore status) => new(status, null, null, null);

    public async Task RecordStartAsync(CancellationToken cancellationToken)
    {
        await status.RecordStartAsync(dailyTokenLimit, cancellationToken);
        if (pauseReason is not null) await status.RecordPauseAsync(pauseReason, null, cancellationToken);
    }
}
