using System.Globalization;

namespace CivicLens.Application.Collection.Bills;

public static class CongressBillParser
{
    public const string ExtractionVersion = "congress-bill-detail-v1";
    private static readonly string[] UpdateDateFormats = ["yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:sszzz"];
    public static CongressBill Parse(ReadOnlySpan<byte> json, CongressBillListItem expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var bill = CongressApiJson.Deserialize<WireDetail>(json, "Bill detail").Bill
            ?? throw new InvalidDataException("Bill detail must contain a bill.");
        if (bill is not
            {
                Congress: >= 1, Number: >= 1, Type: not null, Title: not null, IntroducedDate: not null,
                UpdateDate: not null, Sponsors: not null
            })
            throw new InvalidDataException("Bill detail is incomplete.");
        var type = CongressBillListItem.NormalizeType(bill.Type)
            ?? throw new InvalidDataException("Bill detail has an unknown bill type.");
        if (bill.Congress.Value != expected.Congress || type != expected.Type || bill.Number.Value != expected.Number)
            throw new InvalidDataException("Bill detail is for another bill than requested.");
        return new CongressBill(bill.Congress.Value, type, bill.Number.Value, bill.Title, bill.IntroducedDate.Value,
            ReadUpdateDate(bill.UpdateDate), ReadPolicyArea(bill.PolicyArea), ReadSponsors(bill.Sponsors),
            ReadCosponsorCount(bill.Cosponsors), ReadCount(bill.Actions, "action"), ReadCount(bill.Summaries, "summary"));
    }

    internal static DateTimeOffset ReadUpdateDate(string updateDate) =>
        DateTimeOffset.TryParseExact(updateDate, UpdateDateFormats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : throw new InvalidDataException("Update date must be an exact time with an offset.");

    private static int ReadCosponsorCount(WireCosponsors? cosponsors) => cosponsors switch
    {
        null => 0,
        { CountIncludingWithdrawnCosponsors: >= 0 and var count } => count,
        _ => throw new InvalidDataException("Bill detail cosponsor count is invalid.")
    };

    private static int ReadCount(WireCount? value, string name) => value switch
    {
        null => 0,
        { Count: >= 0 and var count } => count,
        _ => throw new InvalidDataException($"Bill detail {name} count is invalid.")
    };

    private static string? ReadPolicyArea(WirePolicyArea? policyArea) =>
        string.IsNullOrWhiteSpace(policyArea?.Name) ? null : policyArea.Name.Trim();

    private static string[] ReadSponsors(WireSponsor?[] sponsors) =>
        sponsors.Select(sponsor => sponsor?.BioguideId is null || !BioguideId.IsValid(sponsor.BioguideId)
            ? throw new InvalidDataException("Bill sponsor has no valid bioguide ID.")
            : sponsor.BioguideId).Distinct(StringComparer.Ordinal).ToArray();

    private sealed record WireDetail(WireBill? Bill);
    private sealed record WireBill(int? Congress, string? Type, int? Number, string? Title, DateOnly? IntroducedDate,
        string? UpdateDate, WirePolicyArea? PolicyArea, WireSponsor?[]? Sponsors, WireCosponsors? Cosponsors,
        WireCount? Actions, WireCount? Summaries);
    private sealed record WirePolicyArea(string? Name);
    private sealed record WireSponsor(string? BioguideId);
    private sealed record WireCount(int? Count);
    private sealed record WireCosponsors(int? CountIncludingWithdrawnCosponsors);
}
