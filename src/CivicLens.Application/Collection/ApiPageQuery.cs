using System.Collections.Specialized;
using System.Globalization;
using System.Web;
using CivicLens.Collection.Contracts;

namespace CivicLens.Application.Collection;

public static class ApiPageQuery
{
    public const string LimitParameter = "limit";
    public const string FormatParameter = "format";
    public const string OffsetParameter = "offset";
    public const int PageLimit = 250;

    public static string FirstPage(int limit) => $"{FormatParameter}=json&{LimitParameter}={limit.ToString(CultureInfo.InvariantCulture)}";

    public static bool IsValid(NameValueCollection query, int limit, params string[] additionalParameters) =>
        query.AllKeys.All(key => key is LimitParameter or FormatParameter or OffsetParameter ||
            additionalParameters.Contains(key)) &&
        query[LimitParameter] == limit.ToString(CultureInfo.InvariantCulture) &&
        query[FormatParameter] == "json" &&
        ReadOffset(query) is not null;

    public static int? ReadOffset(NameValueCollection query)
    {
        var value = query[OffsetParameter];
        return value is null ? 0 : TryReadNumber(value);
    }

    public static int? TryReadNumber(string value)
    {
        var isInteger = int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number);
        return isInteger && value == number.ToString(CultureInfo.InvariantCulture) ? number : null;
    }

    public static (string[] Segments, int Offset)? TryReadPage(string sourceUrl, string url, int limit)
    {
        var prefix = sourceUrl + "/";
        var queryStart = url.IndexOf('?', StringComparison.Ordinal);
        if (queryStart < 0 || !url.StartsWith(prefix, StringComparison.Ordinal)) return null;
        var rawQuery = url[(queryStart + 1)..];
        if (rawQuery.Contains('%') || rawQuery.Contains('+')) return null;
        var query = HttpUtility.ParseQueryString(rawQuery);
        return IsValid(query, limit) ? (url[prefix.Length..queryStart].Split('/'), ReadOffset(query)!.Value) : null;
    }

    public static string? ReadNext<TSeries>(string? next, CollectionRequest scope, TSeries series, int offset,
        Func<string, (TSeries Series, int Offset)?> tryRead, string otherSeriesMessage)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (string.IsNullOrEmpty(next)) return null;
        if (!Uri.TryCreate(next, UriKind.Absolute, out var uri) || !scope.Allows(uri))
            throw new InvalidDataException("Next page link is outside the source scope.");
        var following = tryRead(next)
            ?? throw new InvalidDataException("Next page link does not have the exact page form.");
        if (!EqualityComparer<TSeries>.Default.Equals(following.Series, series))
            throw new InvalidDataException(otherSeriesMessage);
        if (following.Offset <= offset)
            throw new InvalidDataException("Next page link does not advance the offset.");
        return next;
    }
}
