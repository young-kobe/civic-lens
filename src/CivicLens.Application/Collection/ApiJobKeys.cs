using System.Globalization;

namespace CivicLens.Application.Collection;

public static class ApiJobKeys
{
    private const string ExactTimeFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    public static string ParentJobId(string key) => key[(key.LastIndexOf('/') + 1)..];

    public static string ExactTime(DateTimeOffset updateDate) =>
        updateDate.UtcDateTime.ToString(ExactTimeFormat, CultureInfo.InvariantCulture);
}
