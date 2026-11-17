using CivicLens.PublicApi.Http;
using Microsoft.AspNetCore.Http;

namespace CivicLens.PublicApi.Statements;

public sealed record StatementLinkedVoteRequest
{
    public const int DefaultLimit = 10;

    public StatementLinkedVoteCursor? Cursor { get; init; }
    public bool Backward { get; init; }
    public int Limit { get; init; } = DefaultLimit;

    public static StatementLinkedVoteRequest Parse(IQueryCollection query)
    {
        var (cursor, backward) = KeysetPage.Cursor(query);
        return new() { Cursor = StatementLinkedVoteCursor.Parse(cursor), Backward = backward };
    }
}
