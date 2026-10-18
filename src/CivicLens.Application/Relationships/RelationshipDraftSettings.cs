using CivicLens.Application.Analysis;

namespace CivicLens.Application.Relationships;

public sealed record RelationshipDraftSettings : AnalysisSettings
{
    public RelationshipDraftSettings(long dailyTokenLimit, long? runTokenLimit = null, int concurrency = DefaultConcurrency)
        : base(dailyTokenLimit, runTokenLimit, concurrency, RelationshipDraftPrompt.MinimumReservation) { }

    public int PromptByteCap => (int)Math.Min(RelationshipDraftTask.MaximumPromptBytes, RunTokenLimit - RelationshipDraftTask.MaximumOutputTokens);
}
