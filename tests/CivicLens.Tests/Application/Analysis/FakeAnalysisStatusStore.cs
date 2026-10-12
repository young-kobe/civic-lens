using CivicLens.Application.Analysis;

namespace CivicLens.Tests.Application.Analysis;

internal sealed class FakeAnalysisStatusStore : IAnalysisStatusStore
{
    public List<(string Reason, TimeSpan? Duration)> Pauses { get; } = [];
    public int OutageClears { get; private set; }

    public Task<AnalysisStatus?> GetAsync(CancellationToken cancellationToken) =>
        Task.FromResult<AnalysisStatus?>(null);

    public Task RecordStartAsync(long? dailyTokenLimit, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task RecordPauseAsync(string reason, TimeSpan? duration, CancellationToken cancellationToken)
    {
        lock (Pauses) Pauses.Add((reason, duration));
        return Task.CompletedTask;
    }

    public Task ClearOutagePauseAsync(CancellationToken cancellationToken)
    {
        OutageClears++;
        return Task.CompletedTask;
    }
}
