namespace CivicLens.Application.Analysis;

public interface IDocumentChangeAnalysisStatusStore
{
    Task<DocumentChangeAnalysisStatus?> GetAsync(CancellationToken cancellationToken);
    Task RecordStartAsync(long? dailyTokenLimit, CancellationToken cancellationToken);
    Task RecordPauseAsync(string reason, TimeSpan? duration, CancellationToken cancellationToken);
    Task ClearOutagePauseAsync(CancellationToken cancellationToken);
}
