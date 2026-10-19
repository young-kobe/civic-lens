using CivicLens.Application.Analysis;

namespace CivicLens.Application.Votes;

public sealed record VoteDraftSettings : AnalysisSettings
{
    public VoteDraftSettings(long dailyTokenLimit, int concurrency = DefaultConcurrency)
        : base(dailyTokenLimit, concurrency, VoteDraftPrompt.MinimumReservation) { }

    public int PromptByteCap => (int)Math.Min(VoteDraftTask.MaximumPromptBytes, CallTokenLimit - VoteDraftTask.MaximumOutputTokens);
}
