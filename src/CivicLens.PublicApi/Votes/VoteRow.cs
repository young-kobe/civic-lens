using CivicLens.Publication.Contracts;

namespace CivicLens.PublicApi.Votes;

/// <summary>One row of the view <c>public_api.votes</c>. The counts are null when a cast is a candidate name.</summary>
public sealed record VoteRow
{
    public required string RecordId { get; init; }
    public required int RevisionNumber { get; init; }
    public required PublishedChamber Chamber { get; init; }
    public required int Congress { get; init; }
    public required int Session { get; init; }
    public required int RollCall { get; init; }
    public required DateOnly VotedOn { get; init; }
    public required PublishedVoteKind Kind { get; init; }
    public string? BillType { get; init; }
    public int? BillNumber { get; init; }
    public required string Headline { get; init; }
    public required string Summary { get; init; }
    public required PublishedTextOrigin TextOrigin { get; init; }
    public string? Label { get; init; }
    public string? Question { get; init; }
    public required string Result { get; init; }
    public PublishedVoteAlignment? Alignment { get; init; }
    public int? Yea { get; init; }
    public int? Nay { get; init; }
    public int? Present { get; init; }
    public int? NotVoting { get; init; }
}
