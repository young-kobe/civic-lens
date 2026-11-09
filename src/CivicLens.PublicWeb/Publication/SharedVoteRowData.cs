namespace CivicLens.PublicWeb.Publication;

public sealed record SharedVoteRowData(
    string RecordId, DateOnly VotedOn, int Congress, int Session, int RollCall, string Headline, string? Label,
    string KindWords, string ResultWords, SharedVoteCastData[] Casts)
{
    public bool? MembersVotedTheSame { get; init; }
}
