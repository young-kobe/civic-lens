using System.Text.Json;
using CivicLens.Application.Analysis;

namespace CivicLens.Application.Issues;

internal sealed record IssueChoiceOutput(double Confidence, string[]? IssueIds = null)
{
    public static IssueChoiceOutput? Parse(string outputJson)
    {
        try { return JsonSerializer.Deserialize<IssueChoiceOutput>(outputJson, AnalysisAnswerRules.OutputJson); }
        catch (JsonException) { return null; }
    }
}
