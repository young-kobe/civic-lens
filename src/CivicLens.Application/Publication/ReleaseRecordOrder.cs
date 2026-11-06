using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>The one rule that lists dated rows: event day, else first publication day, newest first, then first publication time newest first, then record ID.</summary>
public static class ReleaseRecordOrder
{
    public static DateOnly Day(PublishedRecordEntry entry) =>
        entry.EventDate ?? DateOnly.FromDateTime(entry.FirstPublishedAtUtc.UtcDateTime);

    public static IEnumerable<T> NewestFirst<T>(IEnumerable<T> rows, Func<T, DateOnly> dayOf, Func<T, DateTimeOffset> publishedOf,
        Func<T, string> recordIdOf) =>
        rows.OrderByDescending(dayOf).ThenByDescending(publishedOf).ThenBy(recordIdOf, StringComparer.Ordinal);

    /// <summary>The brief entries of a release, newest week first. The home page shows the first one.</summary>
    public static PublishedRecordEntry[] BriefsNewestFirst(IEnumerable<PublishedRecordEntry> records) =>
        [.. NewestFirst(records.Where(entry => entry.Kind == PublishedRecordKind.Brief), Day, entry => entry.FirstPublishedAtUtc, entry => entry.RecordId)];

    public static IEnumerable<PublishedWithdrawal> WithdrawalsNewestFirst(IEnumerable<PublishedWithdrawal> withdrawals) =>
        withdrawals.OrderByDescending(withdrawal => withdrawal.WithdrawnAtUtc).ThenBy(withdrawal => withdrawal.RecordId, StringComparer.Ordinal);

    /// <summary>The same order for rows that can lack a day: a row with no day comes last.</summary>
    public static IEnumerable<T> NewestFirstUndatedLast<T>(IEnumerable<T> rows, Func<T, DateOnly?> dayOf, Func<T, DateTimeOffset> publishedOf,
        Func<T, string> recordIdOf) =>
        rows.OrderBy(row => dayOf(row) is null).ThenByDescending(dayOf).ThenByDescending(publishedOf).ThenBy(recordIdOf, StringComparer.Ordinal);
}
