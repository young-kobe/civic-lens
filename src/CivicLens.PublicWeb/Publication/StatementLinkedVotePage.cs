namespace CivicLens.PublicWeb.Publication;

/// <summary>A bounded page of the newest votes linked to one statement.</summary>
public sealed record StatementLinkedVotePage(
    StatementLinkedVoteData[] Votes,
    string? NextCursor,
    string? PreviousCursor);
