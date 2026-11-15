using CivicLens.PublicWeb.Components.Ui;
using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

public static class PublishedCorrectionView
{
    // The owner approves this wording. Change it here only.
    private const string ModelRewriteTitle = "Summary rewritten by the AI model";
    private const string CodeRewriteTitle = "Wording rewritten by code";
    private const string SourceUpdateTitle = "Updated from the source";
    private const string FactsUnchangedText = "The vote facts did not change.";

    public static string AfterTitle(PublishedCorrection correction) =>
        $", {Moment.Day(correction.PublishedAtUtc)}. {Replaces(correction)}{(correction.IsTextRewrite ? " " + FactsUnchangedText : "")}";

    /// <summary>The muted record meta line of a routine code update: its history label and its day.</summary>
    public static string UpdateLine(PublishedCorrection correction) => $"{Title(correction)}, {Moment.Day(correction.PublishedAtUtc)}.";

    public static string Title(PublishedCorrection correction) => correction.Note is not null ? "Correction" : correction.TextRewrite switch
    {
        PublishedTextRewrite.Model => ModelRewriteTitle,
        PublishedTextRewrite.Code => CodeRewriteTitle,
        _ => SourceUpdateTitle
    };

    private static string Replaces(PublishedCorrection correction) => $"Replaces revision {correction.ReplacedRevisionNumber}.";
}
