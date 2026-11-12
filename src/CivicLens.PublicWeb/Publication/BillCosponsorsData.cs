using CivicLens.Publication.Contracts;
using CivicLens.PublicWeb.Components.Ui;

namespace CivicLens.PublicWeb.Publication;

/// <summary>
/// The tracked cosponsors of a bill who had not withdrawn on the release date. Original cosponsors joined on the day of introduction.
/// The party of each one is the party that the term of the official held on the day that the official joined.
/// </summary>
public sealed record BillCosponsorsData(PublishedChamber Chamber, BillSponsorData[] Original, BillSponsorData[] Later)
{
    public const string NoParty = "No party listed";

    private static readonly PartyBarFill[] PartyFills = [PartyBarFill.Solid, PartyBarFill.Stripes, PartyBarFill.Dots, PartyBarFill.Grid];

    public int Total => Original.Length + Later.Length;

    /// <summary>The bar of the tracked cosponsors by party. Democrats come first, then Republicans, then every other party. A cosponsor with no party counts under "No party listed", drawn open and gray.</summary>
    public PartyBarData PartyBar()
    {
        var parties = Original.Concat(Later).Where(sponsor => sponsor.Party is not null).GroupBy(sponsor => sponsor.Party!)
            .OrderBy(group => PartyTones.Rank(group.Key)).ThenBy(group => group.Key, StringComparer.Ordinal).ToArray();
        List<PartyBarSegment> segments = [.. parties.Select((group, index) =>
            new PartyBarSegment(PublishedPartyNames.Members(group.Key), group.Count(), PartyTones.Of(group.Key), PartyFills[index % PartyFills.Length]))];
        if (Original.Concat(Later).Count(sponsor => sponsor.Party is null) is > 0 and var unknown)
            segments.Add(new(NoParty, unknown, PartyTone.Quiet, PartyBarFill.Open));
        return new($"Tracked {Chamber} cosponsors by party on the day each one joined", segments);
    }
}
