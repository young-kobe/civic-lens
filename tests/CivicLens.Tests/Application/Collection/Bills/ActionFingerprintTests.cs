using System.Security.Cryptography;
using System.Text;
using CivicLens.Application.Collection.Bills;
using CivicLens.Core.Actions;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Application.Collection.Bills;

public sealed class ActionFingerprintTests
{
    private static ActionSponsorship Entry(string bioguide, ActionSponsorshipRole role, bool original, string sponsored, string? withdrawn = null) =>
        new("person", bioguide, role, original, DateOnly.Parse(sponsored), withdrawn is null ? null : DateOnly.Parse(withdrawn), []);

    [Fact]
    public void TheFingerprintIsTheSha256OfTheVersionedCanonicalTextOfTheSortedEntries()
    {
        var fingerprint = ActionFingerprint.Compute(
        [
            Entry("W000817", ActionSponsorshipRole.Sponsor, false, "2025-01-03"),
            Entry("M000133", ActionSponsorshipRole.Cosponsor, true, "2025-01-04", "2025-02-10")
        ]);

        var canonical = "action-fingerprint-v1\nM000133|cosponsor|original|2025-01-04|2025-02-10\nW000817|sponsor|added|2025-01-03|-";
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))), fingerprint);
    }

    [Fact]
    public void TheOrderOfTheEntriesAndTheirCitationsDoNotChangeTheFingerprint()
    {
        var first = Entry("W000817", ActionSponsorshipRole.Sponsor, false, "2025-01-03");
        var second = Entry("M000133", ActionSponsorshipRole.Cosponsor, true, "2025-01-04");

        Assert.Equal(ActionFingerprint.Compute([first, second]),
            ActionFingerprint.Compute([second, first with { Citations = ActionRevisionFixtures.Spans((0, 4)) }]));
    }

    [Theory]
    [InlineData("M000133", ActionSponsorshipRole.Sponsor, true, "2025-01-04", "2025-02-10")]
    [InlineData("M000133", ActionSponsorshipRole.Cosponsor, false, "2025-01-04", "2025-02-10")]
    [InlineData("M000133", ActionSponsorshipRole.Cosponsor, true, "2025-01-05", "2025-02-10")]
    [InlineData("M000133", ActionSponsorshipRole.Cosponsor, true, "2025-01-04", "2025-02-11")]
    [InlineData("M000133", ActionSponsorshipRole.Cosponsor, true, "2025-01-04", null)]
    [InlineData("S000033", ActionSponsorshipRole.Cosponsor, true, "2025-01-04", "2025-02-10")]
    public void EveryFieldOfAnEntryChangesTheFingerprint(string bioguide, ActionSponsorshipRole role, bool original, string sponsored, string? withdrawn)
    {
        var baseline = ActionFingerprint.Compute([Entry("M000133", ActionSponsorshipRole.Cosponsor, true, "2025-01-04", "2025-02-10")]);

        Assert.NotEqual(baseline, ActionFingerprint.Compute([Entry(bioguide, role, original, sponsored, withdrawn)]));
    }
}
