namespace CivicLens.Application.Analysis;

internal sealed record HeadlineSummaryOutput(AnalysisOutputText Headline, AnalysisOutputText Summary);

internal sealed record AnalysisOutputText(string Text, double Confidence);
