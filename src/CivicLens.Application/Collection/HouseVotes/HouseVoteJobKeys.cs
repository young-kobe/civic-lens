using CivicLens.Application.Collection.Bills;

namespace CivicLens.Application.Collection.HouseVotes;

public static class HouseVoteJobKeys
{
    public const string Prefix = "house-vote/";

    public static string Detail(string sourceId, HouseVoteListItem vote) => Versioned(sourceId, "vote", vote.Roll, vote.UpdateDate);

    public static string Members(string sourceId, HouseVote vote) => Versioned(sourceId, "members", vote.Roll, vote.UpdateDate);

    public static string NextPage(string sourceId, string listJobId) => $"{Prefix}{sourceId}/page/{listJobId}";

    private static string Versioned(string sourceId, string kind, HouseVoteRoll roll, DateTimeOffset updateDate) =>
        $"{Prefix}{sourceId}/{kind}/{roll.Session.Congress}/{roll.Session.Number}/{roll.Number}/" +
        ApiJobKeys.ExactTime(updateDate);
}
