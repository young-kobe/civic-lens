using CivicLens.PublicApi.Http;
using Microsoft.AspNetCore.Http;

namespace CivicLens.PublicApi.Votes;

/// <summary>The repeatable <c>official</c> parameter. It names the officials of the reader, at most <see cref="Maximum"/>, as one reader has one House member and two senators.</summary>
internal static class OfficialFilter
{
    public const int Maximum = 3;

    /// <summary>Reads the officials in the order of the query. No value gives an empty array. An invalid value throws <see cref="ArgumentException"/> that names the parameter.</summary>
    public static string[] Parse(IQueryCollection query) => QueryValues.Filters(query, "official", Maximum);
}
