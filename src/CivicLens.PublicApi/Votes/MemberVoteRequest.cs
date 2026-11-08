using CivicLens.PublicApi.Http;
using Microsoft.AspNetCore.Http;

namespace CivicLens.PublicApi.Votes;

public sealed record MemberVoteRequest
{
    /// <summary>One to <see cref="OfficialFilter.Maximum"/> official IDs, in the order of the query.</summary>
    public required string[] Officials { get; init; }

    /// <summary>One tracked issue ID. The answer then holds only votes that the release tags with the issue.</summary>
    public string? Issue { get; init; }

    /// <summary>Reads the query string. An invalid value throws <see cref="ArgumentException"/> that names the parameter.</summary>
    public static MemberVoteRequest Parse(IQueryCollection query) => Parse(query, OfficialFilter.Maximum);

    /// <summary>Reads a shared vote query with the shared route's official bound.</summary>
    public static MemberVoteRequest ParseShared(IQueryCollection query) => Parse(query, OfficialFilter.MaximumShared);

    private static MemberVoteRequest Parse(IQueryCollection query, int maximumOfficials)
    {
        var officials = OfficialFilter.Parse(query, maximumOfficials);
        return officials.Length == 0
            ? throw new ArgumentException("official is required.", "official")
            : new() { Officials = officials, Issue = QueryValues.Filter(query, "issue") };
    }
}
