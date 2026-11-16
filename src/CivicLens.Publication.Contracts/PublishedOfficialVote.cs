namespace CivicLens.Publication.Contracts;

/// <summary>The cast words of one official on a roll call vote, and the side that each cast takes.</summary>
public static class PublishedOfficialVote
{
    public const string Aye = "Aye";
    public const string No = "No";
    public const string Present = "Present";
    public const string NotVoting = "Not Voting";

    public static string? SideOf(string cast) => cast switch
    {
        PublishedPartyMajority.Yea or Aye => PublishedPartyMajority.Yea,
        PublishedPartyMajority.Nay or No => PublishedPartyMajority.Nay,
        _ => null
    };
}
