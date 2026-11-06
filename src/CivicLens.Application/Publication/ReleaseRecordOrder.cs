using CivicLens.Core.Relationships;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>The one rule that lists dated rows: event day, else first publication day, newest first, then first publication time newest first, then record ID.</summary>
public static class ReleaseRecordOrder
{
    public static DateOnly Day(PublishedRecordEntry entry) => Day(entry.EventDate, entry.FirstPublishedAtUtc);

    public static DateOnly Day(DateOnly? eventDate, DateTimeOffset firstPublishedAtUtc) =>
        eventDate ?? DateOnly.FromDateTime(firstPublishedAtUtc.UtcDateTime);

    /// <summary>The Congress of the record day. The stored entry Congress column holds this value, and the profile counts use it.</summary>
    public static int? CongressOf(PublishedRecordEntry entry) => CongressCalendar.CongressOn(Day(entry));

    public static int? CongressOf(DateOnly? eventDate, DateTimeOffset firstPublishedAtUtc) =>
        CongressCalendar.CongressOn(Day(eventDate, firstPublishedAtUtc));

    public static IEnumerable<T> NewestFirst<T>(IEnumerable<T> rows, Func<T, DateOnly> dayOf, Func<T, DateTimeOffset> publishedOf,
        Func<T, string> recordIdOf) =>
        rows.OrderByDescending(dayOf).ThenByDescending(publishedOf).ThenBy(recordIdOf, StringComparer.Ordinal);

    public static IEnumerable<PublishedWithdrawal> WithdrawalsNewestFirst(IEnumerable<PublishedWithdrawal> withdrawals) =>
        withdrawals.OrderByDescending(withdrawal => withdrawal.WithdrawnAtUtc).ThenBy(withdrawal => withdrawal.RecordId, StringComparer.Ordinal);

    /// <summary>The same order for rows that can lack a day: a row with no day comes last.</summary>
    public static IEnumerable<T> NewestFirstUndatedLast<T>(IEnumerable<T> rows, Func<T, DateOnly?> dayOf, Func<T, DateTimeOffset> publishedOf,
        Func<T, string> recordIdOf) =>
        rows.OrderBy(row => dayOf(row) is null).ThenByDescending(dayOf).ThenByDescending(publishedOf).ThenBy(recordIdOf, StringComparer.Ordinal);
}
