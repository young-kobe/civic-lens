namespace CivicLens.Application.Analysis;

public sealed record DocumentChangeDraftSettings : AnalysisSettings
{
    public DocumentChangeDraftSettings(long dailyTokenLimit, long? runTokenLimit = null, int concurrency = DefaultConcurrency)
        : base(dailyTokenLimit, runTokenLimit, concurrency, DocumentChangeDraftPrompt.MinimumReservation) { }
}
