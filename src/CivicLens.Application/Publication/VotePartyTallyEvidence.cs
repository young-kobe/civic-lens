using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>
/// What storage holds for one exact vote revision: the nullable expected group count, the stored group rows, and the releases whose kept folders can hold its vote file.
/// <see cref="Releases"/> is null when no record state names the revision.
/// </summary>
public sealed record VotePartyTallyEvidence(string RecordId, int RevisionNumber, int? ExpectedGroupCount,
    IReadOnlyList<PublishedPartyTally> StoredGroups, KeptReleaseRange? Releases);
