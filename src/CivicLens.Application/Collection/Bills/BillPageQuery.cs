using System.Collections.Specialized;
using System.Globalization;

namespace CivicLens.Application.Collection.Bills;

public static class BillPageQuery
{
    public const string LimitParameter = "limit";
    public const string FormatParameter = "format";
    public const string OffsetParameter = "offset";
    public const int PageLimit = 250;
    public const int SummariesPageLimit = 1;

    public static int LimitOf(ApiResource resource) => resource == ApiResource.BillSummaries ? SummariesPageLimit : PageLimit;

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
        if (value is null) return 0;
        var isInteger = int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var offset);
        return isInteger && value == offset.ToString(CultureInfo.InvariantCulture) ? offset : null;
    }
}
