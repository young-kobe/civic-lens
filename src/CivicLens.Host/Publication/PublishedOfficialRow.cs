using CivicLens.Application.Actions;
using CivicLens.Application.Publication;
using CivicLens.Publication.Contracts;

namespace CivicLens.Host.Publication;

public sealed record PublishedOfficialRow(PublishedRecordKind Kind, DateOnly Date, string RecordId, string Headline, string Badge, string? Detail, string? Cast,
    DateTimeOffset PublishedAtUtc, bool AgainstParty, string? FilterKeys = null)
{
    public static PublishedOfficialRow Of(PublishedRecordEntry entry, ActionBillStatus? billStatus) => new(
        entry.Kind, ReleaseRecordOrder.Day(entry), entry.RecordId, entry.Headline, PublishedOfficialView.RowKind(entry),
        billStatus is null ? null : PublishedOfficialView.StatusLine(billStatus), null, entry.FirstPublishedAtUtc, false);

    public static PublishedOfficialRow Of(PublishedOfficialVote vote) => new(
        PublishedRecordKind.Vote, vote.VotedOn, vote.RecordId, vote.Headline, PublishedRecordLabels.Kind(PublishedRecordKind.Vote), $"{PublishedOfficialView.RollCall(vote)}. Result of the vote: {vote.Result}", vote.Cast, DateTimeOffset.MinValue, VotesWithParty.IsBreak(vote));
}
