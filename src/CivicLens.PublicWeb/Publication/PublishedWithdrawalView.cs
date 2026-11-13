using System.Globalization;
using CivicLens.Publication.Contracts;
using CivicLens.PublicWeb.Components.Evidence;
using CivicLens.PublicWeb.Components.Ui;

namespace CivicLens.PublicWeb.Publication;

internal static class PublishedWithdrawalView
{
    /// <summary>The banner title and the last crumb of the trail. The record ID is not a name that a reader knows.</summary>
    public const string RecordTitle = "Withdrawn record";

    /// <summary>The one sentence that says what happened. The stored withdrawal holds no decider, so the sentence names none.</summary>
    public static string Banner(PublishedWithdrawal withdrawal) =>
        $"Civic Lens withdrew this {PublishedRecordLabels.KindInSentence(withdrawal.Kind)} on {Moment.Full(withdrawal.WithdrawnAtUtc)}.";

    /// <summary>The owner-approved sentence that tells the reader to drop the withdrawn record.</summary>
    public const string DoNotRely = "Do not rely on it. The page keeps this notice so old links do not break.";

    public static IReadOnlyList<Fact> Facts(PublishedWithdrawal withdrawal) =>
    [
        new("Kind", PublishedRecordLabels.Kind(withdrawal.Kind)),
        new("Withdrawn revision", withdrawal.WithdrawnRevisionNumber.ToString(CultureInfo.InvariantCulture)),
        .. PublishedTimeFacts.Of(null, withdrawal.FirstPublishedAtUtc),
        new("Withdrawn", Moment.Full(withdrawal.WithdrawnAtUtc)),
        new("Note", withdrawal.Note)
    ];

    /// <summary>The record ID is not a name that a reader knows, so it sits in the source fold and not in the facts.</summary>
    public static IReadOnlyList<Fact> SourceFacts(PublishedWithdrawal withdrawal) => [new("Record", withdrawal.RecordId)];
}
