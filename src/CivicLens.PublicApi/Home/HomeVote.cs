using CivicLens.Publication.Contracts;

namespace CivicLens.PublicApi.Home;

/// <summary>One substantive vote of the home card. <c>Yea</c> and <c>Nay</c> count the member rows of the vote, as in the vote views. <c>Result</c> holds the source words and <c>ResultWords</c> the plain words of <see cref="PlainVoteWords"/>.
/// <c>BillLine</c> is the line of the bill record that the vote page links, which is the stored <c>bill_record_id</c> of the vote. It is null when that record has no line or the vote has no linked bill.
/// <c>BillLineSource</c> is <see cref="PublishedWording.BillLineSource"/> when <c>BillLine</c> is not null, else null.
/// <c>Present</c> and <c>NotVoting</c> read the same stored counts as the vote rows of <c>/v1/votes</c>.</summary>
public sealed record HomeVote(
    string RecordId, DateOnly VotedOn, PublishedChamber Chamber, int Congress, int Session, int RollCall, string Headline, string? Label, string Result,
    string ResultWords, int Yea, int Nay, string? BillType, int? BillNumber, string? BillLine, string? BillLineSource)
{
    public PublishedVoteAlignment? Alignment { get; init; }
    public int? Present { get; init; }
    public int? NotVoting { get; init; }
}
