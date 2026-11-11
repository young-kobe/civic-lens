using CivicLens.Application.People;

namespace CivicLens.Tests.Application.People;

public sealed class PersonIdRuleTests
{
    [Fact]
    public void TheIdIsTheNameInLowercaseWordsWithoutAccentsAndTheSetRemembersIt()
    {
        var taken = new HashSet<string>(StringComparer.Ordinal);

        var id = PersonIdRule.Mint("Nydia M. Velázquez", "V000080", taken);

        Assert.Equal("nydia-m-velazquez", id);
        Assert.Contains(id, taken);
    }

    [Fact]
    public void ATakenIdGetsTheLowercaseBioguideIdAsASuffix()
    {
        var taken = new HashSet<string>(StringComparer.Ordinal) { "john-smith" };

        Assert.Equal("john-smith-s000001", PersonIdRule.Mint("John Smith", "S000001", taken));
        Assert.Equal("john-smith-s000002", PersonIdRule.Mint("John Smith", "S000002", taken));
    }

    [Fact]
    public void ANameWithNoLettersFallsBackToTheBioguideId()
    {
        Assert.Equal("q000001", PersonIdRule.Mint("...", "Q000001", new HashSet<string>(StringComparer.Ordinal)));
    }

    [Fact]
    public void AnIdLongerThanTheFacetLimitFallsBackToTheBioguideIdSoTheRowFitsItsColumn()
    {
        var name = string.Join(' ', Enumerable.Repeat("Longname", 8));

        Assert.Equal("l000001", PersonIdRule.Mint(name, "L000001", new HashSet<string>(StringComparer.Ordinal)));
    }
}
