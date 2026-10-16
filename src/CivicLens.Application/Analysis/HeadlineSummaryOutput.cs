using System.Text.Json;

namespace CivicLens.Application.Analysis;

internal sealed record HeadlineSummaryOutput(AnalysisOutputText Headline, AnalysisOutputText Summary)
{
    public static HeadlineSummaryOutput? Parse(string outputJson)
    {
        try
        {
            var output = JsonSerializer.Deserialize<HeadlineSummaryOutput>(outputJson, AnalysisAnswerRules.OutputJson);
            return output is { Headline: not null, Summary: not null } ? output : null;
        }
        catch (JsonException) { return null; }
    }
}

internal sealed record AnalysisOutputText(string Text, double Confidence);
