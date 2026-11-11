namespace CivicLens.Application.Publication;

public sealed record BillCountReleasePage(IReadOnlyList<PublicationReleaseSummary> Releases, string SelectionFingerprint, bool HasMore,
    bool StaleCursor = false);
