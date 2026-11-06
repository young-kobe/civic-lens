using CivicLens.PublicApi.Http;
using Microsoft.AspNetCore.Http;

namespace CivicLens.PublicApi.Votes;

/// <summary>The repeatable <c>official</c> parameter. It names the officials of the reader, at most <see cref="Maximum"/>, as one reader has one House member and two senators.</summary>
internal static class OfficialFilter
{
    public const int Maximum = 3;

    /// <summary>Reads the officials in the order of the query. No value gives an empty array. An invalid value throws <see cref="ArgumentException"/> that names the parameter.</summary>
    public static string[] Parse(IQueryCollection query)
    {
        const string name = "official";
        if (!query.TryGetValue(name, out var values)) return [];
        if (values.Count > Maximum) throw new ArgumentException($"{name} holds at most {Maximum} values.", name);
        var officials = new string[values.Count];
        for (var index = 0; index < officials.Length; index++)
            officials[index] = QueryValues.FilterValue(values[index], name) ?? throw new ArgumentException($"{name} must not be empty.", name);
        return officials.Distinct(StringComparer.Ordinal).Count() == officials.Length ? officials : throw new ArgumentException($"{name} must not repeat a value.", name);
    }
}
