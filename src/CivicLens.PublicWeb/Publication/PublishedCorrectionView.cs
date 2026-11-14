using CivicLens.PublicWeb.Components.Ui;
using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

public static class PublishedCorrectionView
{
    // The owner approves this wording. Change it here only.
    private const string TextRewrittenTitle = "Summary rewritten by the AI model";
    private const string FactsUnchangedText = "The vote facts did not change.";

    public static string AfterTitle(PublishedCorrection correction) =>
        $", {Moment.Day(correction.PublishedAtUtc)}. {Replaces(correction)}{(correction.FactsUnchanged ? " " + FactsUnchangedText : "")}";

    /// <summary>The muted record meta line of a routine code update: its history label and its day.</summary>
    public static string UpdateLine(PublishedCorrection correction) => $"{Title(correction)}, {Moment.Day(correction.PublishedAtUtc)}.";

    public static string Title(PublishedCorrection correction) =>
        correction.Note is not null ? "Correction" : correction.FactsUnchanged ? TextRewrittenTitle : "Updated from the source";

    private static string Replaces(PublishedCorrection correction) => $"Replaces revision {correction.ReplacedRevisionNumber}.";
}
