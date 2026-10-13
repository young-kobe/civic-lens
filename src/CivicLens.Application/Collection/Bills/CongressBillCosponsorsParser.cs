using System.Text.Json;
using System.Text.Json.Serialization;
using CivicLens.Collection.Contracts;

namespace CivicLens.Application.Collection.Bills;

public static class CongressBillCosponsorsParser
{
    public const string Version = "congress-bill-cosponsors-v1";
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public static CongressBillCosponsorsPage Parse(ReadOnlySpan<byte> json, CollectionRequest scope, string sourceUrl,
        CongressBillListItem expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var wire = Deserialize(json);
        if (wire.Cosponsors is null || wire.Pagination is null)
            throw new InvalidDataException("Cosponsor page must contain cosponsors and pagination.");
        if (wire.Cosponsors.Length > BillPageQuery.PageLimit)
            throw new InvalidDataException("Cosponsor page holds more cosponsors than the request limit.");
        var cosponsors = wire.Cosponsors.Select(ToCosponsor).ToArray();
        var next = BillPageUrl.ReadNext(wire.Pagination.Next, scope, sourceUrl, ApiResource.BillCosponsors, expected);
        return new CongressBillCosponsorsPage(cosponsors, next);
    }

    private static WirePage Deserialize(ReadOnlySpan<byte> json)
    {
        try
        {
            return JsonSerializer.Deserialize<WirePage>(json, Options)
                ?? throw new InvalidDataException("Cosponsor page is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Cosponsor page is not valid JSON of the expected shape.", exception);
        }
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
