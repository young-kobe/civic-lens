using CivicLens.Host.Components.Ui;
using CivicLens.Publication.Contracts;

namespace CivicLens.Host.Publication;

internal static class PublishedCorrectionView
{
    public static string AfterTitle(PublishedCorrection correction) =>
        $", {Moment.Iso(DateOnly.FromDateTime(correction.PublishedAtUtc.UtcDateTime))}. {Replaces(correction)}";

    public static string TimelineText(PublishedCorrection correction) =>
        $"{Title(correction)}, replaces revision {correction.ReplacedRevisionNumber}";

    public static string Title(PublishedCorrection correction) => correction.Note is null ? "Updated from the source" : "Correction";

    private static string Replaces(PublishedCorrection correction) => $"Replaces revision {correction.ReplacedRevisionNumber}.";
}
