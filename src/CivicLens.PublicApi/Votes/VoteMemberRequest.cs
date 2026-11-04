using CivicLens.Publication.Contracts;
using CivicLens.PublicApi.Http;
using CivicLens.PublicApi.Search;
using Microsoft.AspNetCore.Http;

namespace CivicLens.PublicApi.Votes;

public sealed record VoteMemberRequest
{
    private static readonly string[] CastWords =
        [PublishedPartyMajority.Yea, PublishedPartyMajority.Nay, PublishedOfficialVote.Aye, PublishedOfficialVote.No, PublishedOfficialVote.Present, PublishedOfficialVote.NotVoting];

    /// <summary>The stored form. A plain comparison lets the filter move below the view. A word that is no cast word stays as it is, because a cast can be a candidate name.</summary>
    public string? Cast { get; init; }
    public VoteMemberCursor? Cursor { get; init; }
    public int Limit { get; init; } = SearchRequest.DefaultLimit;

    /// <summary>Reads the query string. An invalid value throws <see cref="ArgumentException"/> that names the parameter.</summary>
    public static VoteMemberRequest Parse(IQueryCollection query) => new()
    {
        Cast = StoredCast(QueryValues.Filter(query, "cast")),
        Cursor = VoteMemberCursor.Parse(QueryValues.Value(query, "cursor")),
        Limit = QueryValues.PageLimit(query)
    };

    private static string? StoredCast(string? value) =>
        value is null ? null : CastWords.FirstOrDefault(word => word.Equals(value, StringComparison.OrdinalIgnoreCase)) ?? value;
}
