using CivicLens.Application.Analysis;

namespace CivicLens.Application.Votes;

public sealed record VoteDraftSettings : AnalysisSettings
{
    public VoteDraftSettings(long dailyTokenLimit, long? runTokenLimit = null, int concurrency = DefaultConcurrency)
        : base(dailyTokenLimit, runTokenLimit, concurrency, VoteDraftPrompt.MinimumReservation) { }

    public int PromptByteCap => (int)Math.Min(VoteDraftTask.MaximumPromptBytes, RunTokenLimit - VoteDraftTask.MaximumOutputTokens);
}
