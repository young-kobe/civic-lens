namespace CivicLens.PublicWeb.Publication;

/// <summary>The one owner of the words about members who voted against most of their party. The vote page and the live record page both use them.</summary>
public static class VoteBreakers
{
    public static string Headline(int count) => $"{count} tracked {(count == 1 ? "member" : "members")} voted against most of the party";

    public const string None = "no tracked member voted against most of the party.";
}
