using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>
/// What storage holds for one exact vote revision: the nullable expected group count, the stored group rows, and, for a positive count, the cast of each row of its member list.
/// A member party is null while the list has no pinned parties.
/// </summary>
public sealed record VotePartyTallyEvidence(string RecordId, int RevisionNumber, int? ExpectedGroupCount,
    IReadOnlyList<PublishedPartyTally> StoredGroups, IReadOnlyList<VoteMemberCast> Members);
