using CivicLens.PublicWeb.Publication;
using Microsoft.AspNetCore.Http;

namespace CivicLens.PublicApi.Http;

/// <summary>
/// The one owner of the previous page of a keyset list (#580 item 7). A previous page takes the same cursor as a next page, in the parameter
/// <see cref="ListPageLink.PreviousName"/>. It reads the rows before the cursor row in the reverse order, and then flips them.
/// So each list keeps one order and one after test, and the URL stays as short as a next page URL. <c>KeysetSlice</c> in Application cuts the review lists by the same rule.
/// </summary>
public static class KeysetPage
{
    /// <summary>The text of the page cursor and whether it is a previous page cursor. A query that holds both parameters throws the invalid cursor <see cref="ArgumentException"/>.</summary>
    public static (string? Cursor, bool Backward) Cursor(IQueryCollection query) =>
        (QueryValues.Value(query, ListPageLink.NextName), QueryValues.Value(query, ListPageLink.PreviousName)) switch
        {
            ({ }, { }) => throw QueryValues.InvalidCursor(),
            (null, { } before) => (before, true),
            var (after, _) => (after, false)
        };

    /// <summary>The test of the rows of a page: the after test, or for a previous page the rows that the after test does not pass, without the cursor row itself.
    /// The record ID ends every order, so the cursor row is the row with the ID of the cursor. <c>IS NOT TRUE</c> keeps a row whose after test is null on the before side.</summary>
    public static string Window(string after, string recordId, string idParameter, bool backward) =>
        backward ? $" AND ({after}) IS NOT TRUE AND {recordId} <> {idParameter}" : $" AND ({after})";

    /// <summary>The order of a page read: the list order, or for a previous page each key in the reverse direction, with the nulls on the other side.</summary>
    public static string Order(string order, bool backward) => backward ? string.Join(", ", Terms(order).Select(Reverse)) : order;

    /// <summary>Cuts the rows of one read, at most <paramref name="limit"/> + 1 in the order of the read, into one page in list order and the cursors of the pages around it.
    /// A next page read with a cursor has a previous page. A previous page read always has a next page: the cursor row comes after it.
    /// An empty page links back to its own cursor, as <c>KeysetSlice</c> does.</summary>
    public static (T[] Rows, string? Next, string? Previous) Cut<T>(IReadOnlyList<T> rows, int limit, string? cursor, bool backward, Func<T, string> encode)
    {
        var page = rows.Take(limit).ToArray();
        var more = rows.Count > limit;
        if (backward) Array.Reverse(page);
        var first = page.Length > 0 ? encode(page[0]) : cursor;
        var last = page.Length > 0 ? encode(page[^1]) : cursor;
        return backward ? (page, last, more ? first : null) : (page, more ? last : null, cursor is null ? null : first);
    }

    // The keys of an ORDER BY, split at the commas outside parentheses.
    private static IEnumerable<string> Terms(string order)
    {
        var depth = 0;
        var start = 0;
        for (var index = 0; index < order.Length; index++)
        {
            depth += order[index] switch { '(' => 1, ')' => -1, _ => 0 };
            if (order[index] != ',' || depth != 0) continue;
            yield return order[start..index].Trim();
            start = index + 1;
        }
        yield return order[start..].Trim();
    }

    // Postgres puts nulls last in ascending order and first in descending order unless the key says otherwise.
    private static string Reverse(string term)
    {
        var nullsFirst = term.EndsWith(" NULLS FIRST", StringComparison.Ordinal);
        var nullsLast = term.EndsWith(" NULLS LAST", StringComparison.Ordinal);
        var key = nullsFirst || nullsLast ? term[..term.LastIndexOf(" NULLS ", StringComparison.Ordinal)] : term;
        var descending = key.EndsWith(" DESC", StringComparison.Ordinal);
        var expression = descending ? key[..^5] : key.EndsWith(" ASC", StringComparison.Ordinal) ? key[..^4] : key;
        var reversedNullsFirst = !(nullsFirst || (!nullsLast && descending));
        // The reversed key is descending exactly when the list key is ascending, and its default puts nulls first then.
        return expression + (descending ? "" : " DESC") + (reversedNullsFirst == !descending ? "" : reversedNullsFirst ? " NULLS FIRST" : " NULLS LAST");
    }
}
