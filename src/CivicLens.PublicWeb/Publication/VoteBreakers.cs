namespace CivicLens.PublicWeb.Publication;

/// <summary>The one owner of the words about members who voted against most of their party. The vote page, the live record page, and the official page use them.</summary>
public static class VoteBreakers
{
    /// <summary>The phrase after "voted": against most of a group, such as "against most Democrats".</summary>
    public static string AgainstMost(string group) => $"against most {group}";

    public static string Headline(int count) => $"{count} tracked {(count == 1 ? "member" : "members")} voted {AgainstMost("of the party")}";

    public const string None = "no tracked member voted against most of the party.";
}
