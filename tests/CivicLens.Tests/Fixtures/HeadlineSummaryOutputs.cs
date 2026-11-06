using System.Text.Json;
using System.Text.Json.Nodes;

namespace CivicLens.Tests.Fixtures;

internal static class HeadlineSummaryOutputs
{
    public static string Output(string headline = "Warren sponsored a housing bill", string summary = "Elizabeth Warren introduced a housing bill on January 3, 2025.",
        double headlineConfidence = 0.8, double summaryConfidence = 0.7, string[]? issueIds = null, (string Text, string Quote)? decides = null)
    {
        var output = new JsonObject
        {
            ["headline"] = JsonSerializer.SerializeToNode(new { text = headline, confidence = headlineConfidence }),
            ["summary"] = JsonSerializer.SerializeToNode(new { text = summary, confidence = summaryConfidence })
        };
        if (decides is { } answer) output["decides"] = JsonSerializer.SerializeToNode(new { text = answer.Text, quote = answer.Quote, confidence = 0.6 });
        if (issueIds is not null) output["issueIds"] = JsonSerializer.SerializeToNode(issueIds);
        return output.ToJsonString();
    }
}
