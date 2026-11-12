using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

/// <summary>One page of the vote list. <see cref="Kinds"/> and <see cref="Issues"/> fill the filter choices. <see cref="NextCursor"/> is null on the last page.</summary>
public sealed record VoteListPageData(int Release, IReadOnlyList<VoteListRowData> Votes, RecordListFilter Filter, string? NextCursor,
    IReadOnlyList<SearchChoiceData> Kinds, IReadOnlyList<SearchChoiceData> Issues);

/// <summary><see cref="Label"/> is the label column of the view, or null. The counts are null when a cast is a candidate name.</summary>
public sealed record VoteListRowData(string RecordId, string Headline, string? Label, PublishedChamber Chamber, int RollCall, DateOnly VotedOn,
    PublishedVoteKind Kind, string? BillNumber, string Result, PublishedVoteAlignment? Alignment, int? Yea, int? Nay);
