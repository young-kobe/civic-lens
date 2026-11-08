using CivicLens.Publication.Contracts;
using CivicLens.PublicApi.Http;
using Microsoft.AspNetCore.Http;

namespace CivicLens.PublicApi.PartyAgreement;

public sealed record PartyAgreementRequest(PublishedChamber Chamber, int Congress)
{
    public static PartyAgreementRequest Parse(IQueryCollection query)
    {
        var chamber = QueryValues.Value(query, "chamber")?.ToLowerInvariant() switch
        {
            "house" => PublishedChamber.House,
            "senate" => PublishedChamber.Senate,
            null or "" => throw new ArgumentException("chamber is required.", "chamber"),
            _ => throw new ArgumentException("chamber must be house or senate.", "chamber")
        };
        var congress = QueryValues.Number(query, "congress", 1, int.MaxValue)
            ?? throw new ArgumentException("congress is required.", "congress");
        return new(chamber, congress);
    }
}
