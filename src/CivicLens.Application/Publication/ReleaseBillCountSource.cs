using System.Collections.Immutable;

namespace CivicLens.Application.Publication;

/// <summary>What the tables hold for the bill counts of one release: the action and vote membership, the stored projection of each revision, and the officials.</summary>
public sealed record ReleaseBillCountSource(IReadOnlyList<PublishedRevisionBinding> Members, IReadOnlyList<BillCountActionProjection> Actions,
    IReadOnlyList<BillCountVoteProjection> Votes, IReadOnlyCollection<string> OfficialIds);

public sealed record BillCountActionProjection(string RecordId, int RevisionNumber, int Congress, DateOnly? EventOn,
    ImmutableArray<string> SponsorIds, ImmutableArray<string> CosponsorIds);

public sealed record BillCountVoteProjection(string RecordId, int RevisionNumber, int Congress);
