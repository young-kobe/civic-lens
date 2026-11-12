using System.Globalization;
using CivicLens.Publication.Contracts;
using CivicLens.PublicWeb.Components.Evidence;
using CivicLens.PublicWeb.Components.Ui;

namespace CivicLens.PublicWeb.Publication;

internal static class PublishedWithdrawalView
{
    /// <summary>The one sentence that says what happened. The stored withdrawal holds no decider, so the sentence names none.</summary>
    public static string Banner(PublishedWithdrawal withdrawal) =>
        $"Civic Lens withdrew this {PublishedRecordLabels.Kind(withdrawal.Kind).ToLowerInvariant()} on {Moment.Full(withdrawal.WithdrawnAtUtc)}.";

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
