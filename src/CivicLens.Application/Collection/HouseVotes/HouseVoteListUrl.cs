using CivicLens.Collection.Contracts;

namespace CivicLens.Application.Collection.HouseVotes;

public readonly record struct HouseVoteListUrl(HouseVoteSession Session, int Offset)
{
    public static string FirstPage(string sourceUrl, HouseVoteSession session) =>
        $"{sourceUrl}/{session.Congress}/{session.Number}?{ApiPageQuery.FirstPage(ApiPageQuery.PageLimit)}";

    public static HouseVoteListUrl? TryRead(string sourceUrl, string? url)
    {
        if (url is null || ApiPageQuery.TryReadPage(sourceUrl, url, ApiPageQuery.PageLimit) is not var (parts, offset)) return null;
        if (parts.Length != 2) return null;
        return HouseVoteSession.TryRead(parts[0], parts[1]) is { } session ? new HouseVoteListUrl(session, offset) : null;
    }

    public static HouseVoteListUrl Read(string sourceUrl, string? url) =>
        TryRead(sourceUrl, url) ?? throw new InvalidDataException("Job URL is not a House vote list page.");

    public string? ReadNext(string? next, CollectionRequest scope, string sourceUrl) =>
        ApiPageQuery.ReadNext(next, scope, Session, Offset,
            url => TryRead(sourceUrl, url) is { } page ? (page.Session, page.Offset) : null,
            "Next page link is for another session.");
}
