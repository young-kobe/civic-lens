using System.Text.Json;
using CivicLens.Application.Analysis;

namespace CivicLens.Application.Actions;

internal sealed record ActionTextOutput(AnalysisOutputText Headline, AnalysisOutputSentences Summary, AnalysisOutputSentences WhyItMatters,
    string[]? IssueIds = null)
{
    public static ActionTextOutput? Parse(string outputJson)
    {
        try
        {
            var output = JsonSerializer.Deserialize<ActionTextOutput>(outputJson, AnalysisAnswerRules.OutputJson);
            return output is { Headline: not null, Summary.Sentences: not null, WhyItMatters.Sentences: not null } ? output : null;
        }
        catch (JsonException) { return null; }
    }
}

internal sealed record AnalysisOutputSentences(string[] Sentences, double Confidence);
