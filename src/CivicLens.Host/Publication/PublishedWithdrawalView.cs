using System.Globalization;
using CivicLens.Publication.Contracts;
using CivicLens.PublicWeb.Components.Evidence;
using CivicLens.PublicWeb.Components.Ui;

namespace CivicLens.Host.Publication;

internal static class PublishedWithdrawalView
{
    public static IReadOnlyList<Fact> Facts(PublishedWithdrawal withdrawal) =>
    [
        new("Kind", PublishedRecordLabels.Kind(withdrawal.Kind)),
        new("Record", withdrawal.RecordId),
        new("Withdrawn revision", withdrawal.WithdrawnRevisionNumber.ToString(CultureInfo.InvariantCulture)),
        new("First published", Moment.Full(withdrawal.FirstPublishedAtUtc)),
        new("Withdrawn", Moment.Full(withdrawal.WithdrawnAtUtc)),
        new("Note", withdrawal.Note)
    ];
}
