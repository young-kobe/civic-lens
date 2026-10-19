using CivicLens.Collection.Contracts;

namespace CivicLens.Application.Collection.Bills;

public static class CongressBillCosponsorsParser
{
    public const string Version = "congress-bill-cosponsors-v1";
    public static CongressBillCosponsorsPage Parse(ReadOnlySpan<byte> json, CollectionRequest scope, string sourceUrl,
        CongressBillListItem expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var wire = CongressApiJson.Deserialize<WirePage>(json, "Cosponsor page");
        if (wire.Cosponsors is null || wire.Pagination is null)
            throw new InvalidDataException("Cosponsor page must contain cosponsors and pagination.");
        if (wire.Cosponsors.Length > ApiPageQuery.PageLimit)
            throw new InvalidDataException("Cosponsor page holds more cosponsors than the request limit.");
        var cosponsors = wire.Cosponsors.Select(ToCosponsor).ToArray();
        var next = BillPageUrl.ReadNext(wire.Pagination.Next, scope, sourceUrl, ApiResource.BillCosponsors, expected);
        return new CongressBillCosponsorsPage(cosponsors, next);
    }

    private static CongressBillCosponsor ToCosponsor(WireCosponsor? cosponsor)
    {
        if (cosponsor is not { BioguideId: not null, SponsorshipDate: not null, IsOriginalCosponsor: not null } ||
            !BioguideId.IsValid(cosponsor.BioguideId))
            throw new InvalidDataException("Cosponsor is incomplete or has an invalid bioguide ID.");
        return new CongressBillCosponsor(cosponsor.BioguideId, cosponsor.SponsorshipDate.Value,
            cosponsor.IsOriginalCosponsor.Value, cosponsor.SponsorshipWithdrawnDate);
    }

    private sealed record WirePage(WireCosponsor?[]? Cosponsors, WirePagination? Pagination);
    private sealed record WireCosponsor(string? BioguideId, DateOnly? SponsorshipDate, bool? IsOriginalCosponsor,
        DateOnly? SponsorshipWithdrawnDate);
    private sealed record WirePagination(string? Next);
}
