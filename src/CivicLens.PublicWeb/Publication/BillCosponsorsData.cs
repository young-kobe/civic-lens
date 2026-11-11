namespace CivicLens.PublicWeb.Publication;

/// <summary>The tracked cosponsors of a bill who had not withdrawn on the release date. Original cosponsors joined on the day of introduction.</summary>
public sealed record BillCosponsorsData(string Chamber, BillSponsorData[] Original, BillSponsorData[] Later)
{
    public const string NoParty = "No party listed";

    public int Total => Original.Length + Later.Length;

    /// <summary>The plural name and count of each party, largest first. A cosponsor with no party in the release counts under "No party listed".</summary>
    public IReadOnlyList<(string Name, int Count)> ByParty() => [.. Original.Concat(Later)
        .GroupBy(sponsor => sponsor.Party)
        .Select(group => (Name: group.Key is { } party ? PublishedPartyNames.Members(party) : NoParty, Count: group.Count()))
        .OrderByDescending(item => item.Count).ThenBy(item => item.Name, StringComparer.Ordinal)];
}
