using CivicLens.Application.Collection.Bills;
using CivicLens.Application.Collection.Votes;
using CivicLens.Collection.Contracts;

namespace CivicLens.Application.Collection.HouseVotes;

public static class HouseVoteListParser
{
    public const string Version = "congress-house-vote-list-v1";

    public static HouseVoteListPage Parse(ReadOnlySpan<byte> json, CollectionRequest scope, string sourceUrl)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var current = HouseVoteListUrl.Read(sourceUrl, scope.Url);
        var wire = CongressApiJson.Deserialize<WirePage>(json, "House vote list page");
        if (wire.HouseRollCallVotes is null || wire.Pagination is null)
            throw new InvalidDataException("House vote list page must contain houseRollCallVotes and pagination.");
        if (wire.HouseRollCallVotes.Length > ApiPageQuery.PageLimit)
            throw new InvalidDataException("House vote list page holds more votes than the request limit.");
        var votes = wire.HouseRollCallVotes.Select(item => ToItem(item, current.Session)).ToArray();
        return new HouseVoteListPage(current.Session, votes, current.ReadNext(wire.Pagination.Next, scope, sourceUrl));
    }

    private static HouseVoteListItem ToItem(HouseVoteWire? item, VoteSession session)
    {
        if (item?.UpdateDate is null || item.ReadRoll() is not { } roll)
            throw new InvalidDataException("House vote list item is incomplete.");
        if (roll.Session != session)
            throw new InvalidDataException("House vote list item belongs to another session than the page.");
        return new HouseVoteListItem(roll, item.UpdateDate.Value);
    }

    private sealed record WirePage(HouseVoteWire?[]? HouseRollCallVotes, WirePagination? Pagination);
    private sealed record WirePagination(string? Next);
}
