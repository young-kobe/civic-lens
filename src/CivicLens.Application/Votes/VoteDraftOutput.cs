using System.Text.Json;
using CivicLens.Application.Analysis;

namespace CivicLens.Application.Votes;

internal sealed record VoteDraftOutput(AnalysisOutputText Headline, AnalysisOutputText Summary, VoteDecidesOutput? Decides = null, string[]? IssueIds = null)
{
    public static VoteDraftOutput? Parse(string outputJson)
    {
        try
        {
            var output = JsonSerializer.Deserialize<VoteDraftOutput>(outputJson, AnalysisAnswerRules.OutputJson);
            return output is { Headline: not null, Summary: not null } ? output : null;
        }
        catch (JsonException) { return null; }
    }
}

internal sealed record VoteDecidesOutput(string Text, string? Quote, double Confidence);
