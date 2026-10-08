namespace CivicLens.Application.Collection.Processing;

public sealed record EvidenceProcessingPage(IReadOnlyList<string> JobIds, EvidenceProcessingCursor? NextCursor);
