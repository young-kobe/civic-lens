namespace CivicLens.Application.Publication;

public sealed record CorrectionHistoryBackfillFailure(int ReleaseNumber, string Kind, string RecordId, string Reason);
