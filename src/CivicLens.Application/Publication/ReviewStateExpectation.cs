namespace CivicLens.Application.Publication;

public sealed record ReviewStateExpectation(string DraftId, int RevisionNumber, int ReviewStateVersion);
