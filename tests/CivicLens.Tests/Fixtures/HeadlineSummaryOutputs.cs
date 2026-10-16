using System.Text.Json;

namespace CivicLens.Tests.Fixtures;

internal static class HeadlineSummaryOutputs
{
    public static string Output(string headline = "Warren sponsored a housing bill", string summary = "Elizabeth Warren introduced a housing bill on January 3, 2025.",
        double headlineConfidence = 0.8, double summaryConfidence = 0.7) => JsonSerializer.Serialize(new
        {
            headline = new { text = headline, confidence = headlineConfidence },
            summary = new { text = summary, confidence = summaryConfidence }
        });
}
