namespace CivicLens.Application.Analysis;

public sealed record DocumentChangeDraftSettings : AnalysisSettings
{
    public DocumentChangeDraftSettings(long dailyTokenLimit, int concurrency = DefaultConcurrency)
        : base(dailyTokenLimit, concurrency, DocumentChangeDraftPrompt.MinimumReservation) { }
}
