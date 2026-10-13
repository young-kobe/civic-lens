using System.Globalization;
using System.Web;

namespace CivicLens.Application.Collection.Bills;

public readonly record struct BillStreamWindow(DateOnly FirstDay, DateOnly LastDay)
{
    public const int MaximumDays = 7;
    public const int PageLimit = 250;
    private const string FromParameter = "fromDateTime";
    private const string ToParameter = "toDateTime";
    private const string DateTimeFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";
    private static readonly TimeSpan ClosingDelay = TimeSpan.FromHours(6);

    public static DateOnly LastClosedDay(DateTimeOffset now) =>
        DateOnly.FromDateTime((now.UtcDateTime - ClosingDelay).Date).AddDays(-1);

    public static BillStreamWindow? Next(DateOnly? cursor, DateOnly backfillFrom, DateTimeOffset now)
    {
        var first = cursor?.AddDays(1) ?? backfillFrom;
        var lastClosed = LastClosedDay(now);
        if (first > lastClosed) return null;
        var last = DateOnly.FromDayNumber(Math.Min(first.DayNumber + MaximumDays - 1, lastClosed.DayNumber));
        return new BillStreamWindow(first, last);
    }

    public static BillStreamWindow? TryRead(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        var query = HttpUtility.ParseQueryString(uri.Query);
        if (!TryReadDay(query[FromParameter], out var from) || !TryReadDay(query[ToParameter], out var to)) return null;
        var window = new BillStreamWindow(from.AddDays(1), to.AddDays(-1));
        return window.FirstDay <= window.LastDay ? window : null;
    }

    public bool IsBehind(DateTimeOffset now) => LastDay < LastClosedDay(now);

    public string RequestUrl(string sourceUrl) =>
        $"{sourceUrl}?{FromParameter}={Format(FirstDay.AddDays(-1))}&{ToParameter}={Format(LastDay.AddDays(1))}" +
        $"&limit={PageLimit}&format=json";

    public bool Matches(string url) => TryRead(url) == this;

    private static string Format(DateOnly day) =>
        day.ToDateTime(TimeOnly.MinValue).ToString(DateTimeFormat, CultureInfo.InvariantCulture);

    private static bool TryReadDay(string? value, out DateOnly day)
    {
        day = default;
        if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal |
            DateTimeStyles.AdjustToUniversal, out var parsed) || parsed.TimeOfDay != TimeSpan.Zero)
            return false;
        day = DateOnly.FromDateTime(parsed.UtcDateTime);
        return true;
    }
}
