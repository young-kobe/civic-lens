using CivicLens.Publication.Contracts;

namespace CivicLens.PublicApi.Officials;

/// <summary>The one owner of the words that say who and what each public number of an official counts.</summary>
internal static class OfficialScopes
{
    public const string RecordCount = "Records of the active release that name the official and have an entry row. Votes are not counted.";

    public static string MissedVotes(PublishedChamber chamber, int congress) =>
        $"Roll calls of the {chamber} in Congress {congress} that list the official. Missed means the cast is Not Voting.";

    public static string PartyLine(PublishedChamber chamber, int congress, string party) =>
        $"Yea or Nay casts by the official as {party} on substantive split roll calls of the {chamber} in Congress {congress}. With party and against party compare the cast with that party's majority. Present, Not Voting, ties, and mixed votes are not counted.";

    public static string Bills(int congress) =>
        $"Bills of Congress {congress} in the active release. A bill counts in the Congress of its bill number. Sponsored counts the bills that the official sponsors. Cosponsored counts the bills that the official cosponsors and has not withdrawn. A sponsor who also cosponsors a bill counts once, as sponsored.";

    public static string Issues(int congress) =>
        $"Tracked issues, in Congress {congress}. Sponsored bills counts the bills that the official sponsors. Votes cast counts the roll calls of a substantive kind where the cast is not Not Voting. Statements are not counted.";

    public const string ExecutiveActions = "Executive actions of the active release that name the official and have an entry row.";
}
