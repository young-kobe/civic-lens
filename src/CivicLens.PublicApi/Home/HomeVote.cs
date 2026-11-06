using CivicLens.Publication.Contracts;

namespace CivicLens.PublicApi.Home;

/// <summary>One substantive vote of the home card. <c>Yea</c> and <c>Nay</c> count the member rows of the vote, as in the vote views. <c>Result</c> holds the source words and <c>ResultWords</c> the plain words of <see cref="PlainVoteWords"/>.
/// <c>BillLine</c> is the line of the first bill record of the vote bill when records are ordered by record ID. It is null when that record has no line or the vote has no bill record.
/// <c>BillLineSource</c> is <see cref="PublishedWording.BillLineSource"/> when <c>BillLine</c> is not null, else null.</summary>
public sealed record HomeVote(
    string RecordId, DateOnly VotedOn, PublishedChamber Chamber, int Congress, int Session, int RollCall, string Headline, string? Label, string Result,
    string ResultWords, int Yea, int Nay, string? BillType, int? BillNumber, string? BillLine, string? BillLineSource);
