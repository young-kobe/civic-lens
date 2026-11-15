using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

/// <summary>The one owner of the plural names of a party and its chamber members, such as "House Democrats".</summary>
public static class PublishedPartyNames
{
    public static string PartyOf(PublishedChamber chamber, string party) => $"{chamber} {Members(party)}";

    public static string Members(string party) => $"{party}s";

    /// <summary>The members of a party after a count: "1 Republican", "3 Republicans".</summary>
    public static string Members(string party, int count) => count == 1 ? party : Members(party);
}
