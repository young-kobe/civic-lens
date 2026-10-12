namespace CivicLens.Application.Analysis;

public interface IAnalysisStatusStore
{
    Task<AnalysisStatus?> GetAsync(CancellationToken cancellationToken);
    Task RecordStartAsync(long? dailyTokenLimit, CancellationToken cancellationToken);
    Task RecordPauseAsync(string reason, TimeSpan? duration, CancellationToken cancellationToken);
    Task ClearOutagePauseAsync(CancellationToken cancellationToken);
}
