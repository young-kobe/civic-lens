using System.Collections.Specialized;
using System.Globalization;

namespace CivicLens.Application.Collection.Bills;

public static class BillPageQuery
{
    public const string LimitParameter = "limit";
    public const string FormatParameter = "format";
    public const string OffsetParameter = "offset";
    public const int PageLimit = 250;
    public static readonly string FirstPage = $"{FormatParameter}=json&{LimitParameter}={PageLimit}";

    public static bool IsValid(NameValueCollection query, params string[] additionalParameters) =>
        query.AllKeys.All(key => key is LimitParameter or FormatParameter or OffsetParameter ||
            additionalParameters.Contains(key)) &&
        query[LimitParameter] == PageLimit.ToString(CultureInfo.InvariantCulture) &&
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
