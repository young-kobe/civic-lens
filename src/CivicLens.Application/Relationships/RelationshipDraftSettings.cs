using CivicLens.Application.Analysis;

namespace CivicLens.Application.Relationships;

public sealed record RelationshipDraftSettings : AnalysisSettings
{
    public RelationshipDraftSettings(long dailyTokenLimit, int concurrency = DefaultConcurrency)
        : base(dailyTokenLimit, concurrency, RelationshipDraftPrompt.MinimumReservation) { }

    public int PromptByteCap => (int)Math.Min(RelationshipDraftTask.MaximumPromptBytes, CallTokenLimit - RelationshipDraftTask.MaximumOutputTokens);
}
