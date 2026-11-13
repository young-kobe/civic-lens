using CivicLens.PublicWeb.Components.Evidence;
using CivicLens.PublicWeb.Components.Ui;

namespace CivicLens.PublicWeb.Publication;

/// <summary>The one owner of the approval and first publication rows of a fact list. A time that the data does not hold gets no row, so a page never invents one.</summary>
public static class PublishedTimeFacts
{
    public static IEnumerable<Fact> Of(DateTimeOffset? approvedAtUtc, DateTimeOffset? firstPublishedAtUtc)
    {
        if (approvedAtUtc is { } approved) yield return new("Approved", Moment.Full(approved));
        if (firstPublishedAtUtc is { } first) yield return new("First published", Moment.Full(first));
    }
}
