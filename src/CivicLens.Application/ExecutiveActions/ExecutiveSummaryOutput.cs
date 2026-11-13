using System.Text.Json;
using CivicLens.Application.Actions;
using CivicLens.Application.Analysis;

namespace CivicLens.Application.ExecutiveActions;

internal sealed record ExecutiveSummaryOutput(AnalysisOutputSentences Summary)
{
    public static ExecutiveSummaryOutput? Parse(string outputJson)
    {
        try
        {
            var output = JsonSerializer.Deserialize<ExecutiveSummaryOutput>(outputJson, AnalysisAnswerRules.OutputJson);
            return output is { Summary.Sentences: not null } ? output : null;
        }
        catch (JsonException) { return null; }
    }
}
