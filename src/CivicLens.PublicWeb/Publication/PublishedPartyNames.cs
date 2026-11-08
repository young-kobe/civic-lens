using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

public static class PublishedPartyNames
{
    public static string PartyOf(PublishedChamber chamber, string party) => $"{chamber} {Members(party)}";

    public static string Members(string party) => $"{party}s";
}
