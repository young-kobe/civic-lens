using CivicLens.Application.Analysis;
using CivicLens.Application.Votes;
using CivicLens.Host.Analysis;

namespace CivicLens.Host.Votes;

internal sealed record VoteDraftConfiguration(VoteDraftSettings Settings, string ApiKey)
{
    public static VoteDraftConfiguration? FromEnvironment()
    {
        var environment = AnalysisEnvironment.Read("CIVIC_LENS_VOTE_DRAFTING_", "Vote drafting");
        if (environment is null) return null;
        var settings = new VoteDraftSettings(environment.DailyTokens, environment.RunTokens,
            environment.Concurrency ?? AnalysisSettings.DefaultConcurrency);
        return new(settings, environment.ApiKey);
    }
}
