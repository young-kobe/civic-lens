using System.Text;
using CivicLens.Application.Roster;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Application.Roster;

public sealed class MemberRosterParserTests
{
    private static RosterMember[] Parse(string json) => MemberRosterParser.Parse(Encoding.UTF8.GetBytes(json));

    [Fact]
    public void AMemberKeepsEveryTermInOrderSoOfficialPagesCanShowDatedTerms()
    {
        var cantwell = Parse(RosterFixtures.Json).Single(member => member.BioguideId == "C000127");

        Assert.Equal(["rep", "sen"], cantwell.Terms.Select(term => term.Chamber));
        Assert.Equal([0, 1], cantwell.Terms.Select(term => term.Index));
        Assert.Equal((new DateOnly(2025, 1, 3), new DateOnly(2031, 1, 3), "WA", null, 1, "Democrat"),
            (cantwell.Terms[1].Start, cantwell.Terms[1].End, cantwell.Terms[1].State, cantwell.Terms[1].District,
                cantwell.Terms[1].SenateClass, cantwell.Terms[1].Party));
    }

    [Fact]
    public void TheOfficeAndContactComeFromTheLastTermAndTheSenateIdFromTheMember()
    {
        var cantwell = Parse(RosterFixtures.Json).Single(member => member.BioguideId == "C000127");

        Assert.Equal(("S275", "511 Hart Senate Office Building", "202-224-3441", "https://www.cantwell.senate.gov"),
            (cantwell.LisId, cantwell.Office, cantwell.Phone, cantwell.Url));
    }

    [Fact]
    public void AHouseMemberHasNoSenateId()
    {
        var norton = Parse(RosterFixtures.Json).Single(member => member.BioguideId == "N000147");

        Assert.Null(norton.LisId);
        Assert.Equal(0, norton.Terms[1].District);
    }

    [Fact]
    public void AMemberWithoutAnOfficialFullNameGetsHerFirstAndLastName()
    {
        var wahab = Parse(RosterFixtures.Json).Single(member => member.BioguideId == "W000832");

        Assert.Equal("Aisha Wahab", wahab.FullName);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("""[{"id":{"bioguide":"bad"},"name":{"official_full":"A"},"terms":[]}]""")]
    [InlineData("""[{"id":{"bioguide":"A000001"},"name":{"official_full":"A"},"terms":[]}]""")]
    [InlineData("""[{"id":{"bioguide":"A000001"},"name":{},"terms":[{"type":"sen","start":"2025-01-03","end":"2027-01-03","state":"AL","party":"Republican"}]}]""")]
    [InlineData("""[{"id":{"bioguide":"A000001"},"name":{"official_full":"A"},"terms":[{"type":"gov","start":"2025-01-03","end":"2027-01-03","state":"AL","party":"Republican"}]}]""")]
    [InlineData("""[{"id":{"bioguide":"A000001"},"name":{"official_full":"A"},"terms":[{"type":"sen","start":"2027-01-03","end":"2025-01-03","state":"AL","party":"Republican"}]}]""")]
    [InlineData("""[{"id":{"bioguide":"A000001"},"name":{"official_full":"A"},"terms":[{"type":"sen","start":"2025-01-03","end":"2027-01-03","state":"AL"}]}]""")]
    [InlineData("""{"not":"a list"}""")]
    public void AnIncompleteRosterIsRejectedSoNoPartialSnapshotIsStored(string json)
    {
        Assert.Throws<InvalidDataException>(() => Parse(json));
    }

    [Fact]
    public void ABioguideIdListedTwiceIsRejectedBecauseItNamesOneMember()
    {
        const string member = """{"id":{"bioguide":"A000001"},"name":{"official_full":"A"},"terms":[{"type":"sen","start":"2025-01-03","end":"2027-01-03","state":"AL","party":"Republican"}]}""";

        Assert.Throws<InvalidDataException>(() => Parse($"[{member},{member}]"));
    }
}
