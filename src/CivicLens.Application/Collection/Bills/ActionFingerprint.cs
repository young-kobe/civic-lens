using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CivicLens.Core.Actions;

namespace CivicLens.Application.Collection.Bills;

public static class ActionFingerprint
{
    public const string Version = "action-fingerprint-v1";

    public static string Compute(IEnumerable<ActionSponsorship> sponsorships)
    {
        ArgumentNullException.ThrowIfNull(sponsorships);
        var lines = sponsorships.Select(Line).Order(StringComparer.Ordinal);
        var text = string.Join('\n', lines.Prepend(Version));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    private static string Line(ActionSponsorship item) => string.Join('|', item.BioguideId,
        item.Role == ActionSponsorshipRole.Sponsor ? "sponsor" : "cosponsor", item.IsOriginal ? "original" : "added",
        Date(item.SponsoredOn), item.WithdrawnOn is { } withdrawn ? Date(withdrawn) : "-");

    private static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
