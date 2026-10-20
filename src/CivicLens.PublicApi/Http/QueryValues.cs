using System.Globalization;
using Microsoft.AspNetCore.Http;

namespace CivicLens.PublicApi.Http;

/// <summary>Reads single values from a query string. An invalid value throws <see cref="ArgumentException"/> that names the parameter.</summary>
internal static class QueryValues
{
    public static string? Value(IQueryCollection query, string name)
    {
        if (!query.TryGetValue(name, out var values)) return null;
        return values.Count == 1 ? values[0] : throw new ArgumentException($"{name} must appear once.", name);
    }

    public static int? Number(IQueryCollection query, string name, int minimum, int maximum)
    {
        var value = Value(query, name);
        if (string.IsNullOrEmpty(value)) return null;
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number >= minimum && number <= maximum
            ? number
            : throw new ArgumentException($"{name} must be a whole number from {minimum} to {maximum}.", name);
    }
}
