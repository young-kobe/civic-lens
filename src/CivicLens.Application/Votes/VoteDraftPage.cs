namespace CivicLens.Application.Votes;

public sealed record VoteDraftPage(string JobId, string ExtractionId, string ParserVersion, string NormalizationVersion, string Text);
