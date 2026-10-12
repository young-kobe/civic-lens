namespace CivicLens.Application.Analysis;

public sealed record DocumentChangeAnalysisSettings : AnalysisSettings
{
    public DocumentChangeAnalysisSettings(long dailyTokenLimit, long? runTokenLimit = null, int concurrency = DefaultConcurrency)
        : base(dailyTokenLimit, runTokenLimit, concurrency, DocumentChangeDraftingPrompt.MinimumReservation) { }
}
