using CivicLens.Host.Components.Ui;
using CivicLens.Publication.Contracts;

namespace CivicLens.Host.Publication;

internal static class PublishedCorrectionView
{
    // The owner approves this wording. Change it here only.
    private const string TextRewrittenTitle = "Summary rewritten by the AI model";
    private const string FactsUnchangedText = "The vote facts did not change.";

    public static string AfterTitle(PublishedCorrection correction) =>
        $", {Moment.Iso(DateOnly.FromDateTime(correction.PublishedAtUtc.UtcDateTime))}. {Replaces(correction)}{(correction.FactsUnchanged ? " " + FactsUnchangedText : "")}";

    public static string TimelineText(PublishedCorrection correction) =>
        $"{Title(correction)}, replaces revision {correction.ReplacedRevisionNumber}";

    public static string TimelineEnd(PublishedCorrection correction) => correction.FactsUnchanged ? ". " + FactsUnchangedText : "";

    public static string Title(PublishedCorrection correction) =>
        correction.Note is not null ? "Correction" : correction.FactsUnchanged ? TextRewrittenTitle : "Updated from the source";

    private static string Replaces(PublishedCorrection correction) => $"Replaces revision {correction.ReplacedRevisionNumber}.";
}
