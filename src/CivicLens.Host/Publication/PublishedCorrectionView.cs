using CivicLens.Host.Components.Ui;
using CivicLens.Publication.Contracts;

namespace CivicLens.Host.Publication;

internal static class PublishedCorrectionView
{
    public static string Heading(PublishedCorrection correction) =>
        $"{Title(correction)}, {Moment.Iso(DateOnly.FromDateTime(correction.PublishedAtUtc.UtcDateTime))}. {Replaces(correction)}";

    public static TimelineEvent Event(PublishedCorrection correction) =>
        new(DateOnly.FromDateTime(correction.PublishedAtUtc.UtcDateTime), $"{Title(correction)}. {Replaces(correction)}",
            TimeOnly.FromDateTime(correction.PublishedAtUtc.UtcDateTime));

    private static string Title(PublishedCorrection correction) => correction.Note is null ? "Updated from the source" : "Correction";

    private static string Replaces(PublishedCorrection correction) => $"Replaces revision {correction.ReplacedRevisionNumber}.";
}
