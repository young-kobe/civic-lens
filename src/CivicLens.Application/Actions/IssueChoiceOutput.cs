using System.Text.Json;
using CivicLens.Application.Analysis;

namespace CivicLens.Application.Actions;

internal sealed record IssueChoiceOutput(double Confidence, string[]? IssueIds = null)
{
    public static IssueChoiceOutput? Parse(string outputJson)
    {
        try { return JsonSerializer.Deserialize<IssueChoiceOutput>(outputJson, AnalysisAnswerRules.OutputJson); }
        catch (JsonException) { return null; }
    }
}
