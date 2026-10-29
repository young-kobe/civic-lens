using CivicLens.Application.Publication;
using CivicLens.Publication.Contracts;

namespace CivicLens.Host.Publication;

internal sealed record PublishedOfficialRow(DateOnly Date, string RecordId, string Headline, PublishedRecordKind Kind, string? Detail, string? Cast,
    DateTimeOffset PublishedAtUtc, bool AgainstParty, string? FilterKeys = null)
{
    public static PublishedOfficialRow Of(PublishedRecordEntry entry) => new(
        ReleaseRecordOrder.Day(entry), entry.RecordId, entry.Headline, entry.Kind, null, null, entry.FirstPublishedAtUtc, false);

    public static PublishedOfficialRow Of(PublishedOfficialVote vote) => new(
        vote.VotedOn, vote.RecordId, vote.Headline, PublishedRecordKind.Vote, $"{PublishedOfficialView.RollCall(vote)}. Result of the vote: {vote.Result}", vote.Cast, DateTimeOffset.MinValue, VotesWithParty.IsBreak(vote));
}
