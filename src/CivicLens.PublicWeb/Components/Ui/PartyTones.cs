using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Components.Ui;

/// <summary>The one owner of the party color rule and the party order of every party bar.</summary>
public static class PartyTones
{
    public static PartyTone Of(string party) => party switch
    {
        PublishedPartyMajority.Democrat => PartyTone.Democrat,
        PublishedPartyMajority.Republican => PartyTone.Republican,
        _ => PartyTone.Neutral
    };

    /// <summary>Democrats come first, then Republicans, then every other group.</summary>
    public static int Rank(string party) => party switch
    {
        PublishedPartyMajority.Democrat => 0,
        PublishedPartyMajority.Republican => 1,
        _ => 2
    };
}
