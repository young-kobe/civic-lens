using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>The one rule that lists dated rows: event day, else first publication day, newest first, then record ID.</summary>
public static class ReleaseRecordOrder
{
    public static DateOnly Day(PublishedRecordEntry entry) =>
        entry.EventDate ?? DateOnly.FromDateTime(entry.FirstPublishedAtUtc.UtcDateTime);

    public static IEnumerable<T> NewestFirst<T>(IEnumerable<T> rows, Func<T, DateOnly> dayOf, Func<T, string> recordIdOf) =>
        rows.OrderByDescending(dayOf).ThenBy(recordIdOf, StringComparer.Ordinal);
}
