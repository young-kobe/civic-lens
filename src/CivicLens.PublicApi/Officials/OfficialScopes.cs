using CivicLens.Publication.Contracts;

namespace CivicLens.PublicApi.Officials;

/// <summary>The one owner of the words that say who and what each public number of an official counts.</summary>
internal static class OfficialScopes
{
    public const string RecordCount = "Records of the active release that name the official. Votes are not counted.";

    public static string MissedVotes(PublishedChamber chamber, int congress) =>
        $"Roll calls of the {chamber} in Congress {congress} that list the official. Missed means the cast is Not Voting.";

    public static string Bills(int congress) =>
        $"Bills of Congress {congress} in the active release. Sponsored counts the bills that the official sponsors. Cosponsored counts the bills that the official cosponsors and has not withdrawn.";

    public static string Issues(int congress) =>
        $"Tracked issues, in Congress {congress}. Sponsored bills counts the bills that the official sponsors. Votes cast counts the roll calls of a substantive kind where the cast is not Not Voting.";

    public const string ExecutiveActions = "Executive actions of the active release that name the official.";
}
