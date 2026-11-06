using CivicLens.PublicApi.Http;
using Microsoft.AspNetCore.Http;

namespace CivicLens.PublicApi.Votes;

public sealed record MemberVoteRequest
{
    public required string Official { get; init; }

    /// <summary>Reads the query string. An invalid value throws <see cref="ArgumentException"/> that names the parameter.</summary>
    public static MemberVoteRequest Parse(IQueryCollection query) => new()
    {
        Official = QueryValues.Filter(query, "official") ?? throw new ArgumentException("official is required.", "official")
    };
}
