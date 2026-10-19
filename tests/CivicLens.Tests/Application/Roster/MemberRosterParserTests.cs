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

    [Fact]
    public void AValidMinimalMemberParsesSoEveryRejectionBelowBreaksOneRule()
    {
        Assert.Single(Parse(Roster(Member())));
    }

    [Fact]
    public void AnEmptyListIsRejectedBecauseItWouldReadAsEveryoneLeaving()
    {
        Assert.Throws<InvalidDataException>(() => Parse("[]"));
    }

    [Fact]
    public void ADocumentThatIsNotAListIsRejected()
    {
        Assert.Throws<InvalidDataException>(() => Parse("""{"not":"a list"}"""));
    }

    [Fact]
    public void AMalformedBioguideIdIsRejected()
    {
        Assert.Throws<InvalidDataException>(() => Parse(Roster(Member(bioguide: "bad"))));
    }

    [Fact]
    public void AMemberWithoutTermsIsRejected()
    {
        Assert.Throws<InvalidDataException>(() => Parse(Roster(Member(terms: ""))));
    }

    [Fact]
    public void AMemberWithoutAnyNameIsRejected()
    {
        Assert.Throws<InvalidDataException>(() => Parse(Roster(Member(name: "{}"))));
    }

    [Fact]
    public void ATermWithAnUnknownChamberIsRejected()
    {
        Assert.Throws<InvalidDataException>(() => Parse(Roster(Member(term: Term(type: "gov")))));
    }

    [Fact]
    public void ATermThatEndsBeforeItStartsIsRejected()
    {
        Assert.Throws<InvalidDataException>(() => Parse(Roster(Member(term: Term(start: "2027-01-03", end: "2025-01-03")))));
    }

    [Fact]
    public void ATermWithoutAPartyIsRejected()
    {
        Assert.Throws<InvalidDataException>(() => Parse(Roster(Member(term: Term(party: "")))));
    }

    [Fact]
    public void ABioguideIdListedTwiceIsRejectedBecauseItNamesOneMember()
    {
        Assert.Throws<InvalidDataException>(() => Parse(Roster(Member(), Member())));
    }

    [Theory]
    [MemberData(nameof(ValuesOneCharacterOverTheirColumn))]
    public void AValueLongerThanItsColumnIsRejectedSoThePreparationBlocksAsInvalidData(string member)
    {
        Assert.Throws<InvalidDataException>(() => Parse(Roster(member)));
    }

    [Theory]
    [MemberData(nameof(ValuesAtTheirColumnLimit))]
    public void AValueExactlyAtItsColumnLimitIsAccepted(string member)
    {
        Assert.Single(Parse(Roster(member)));
    }

    public static TheoryData<string> ValuesOneCharacterOverTheirColumn => Limited(1);

    public static TheoryData<string> ValuesAtTheirColumnLimit => Limited(0);

    private static TheoryData<string> Limited(int excess) => new()
    {
        Member(lis: Text(RosterLimits.LisId + excess)),
        Member(name: $$"""{"official_full":"{{Text(RosterLimits.FullName + excess)}}"}"""),
        Member(term: Term(office: Text(RosterLimits.Office + excess))),
        Member(term: Term(phone: Text(RosterLimits.Phone + excess))),
        Member(term: Term(url: Text(RosterLimits.Url + excess))),
        Member(term: Term(state: Text(RosterLimits.State + excess))),
        Member(term: Term(party: Text(RosterLimits.Party + excess)))
    };

    private static string Text(int length) => new('x', length);

    private static string Roster(params string[] members) => $"[{string.Join(',', members)}]";

    private static string Term(string type = "sen", string start = "2025-01-03", string end = "2027-01-03", string state = "AL",
        string party = "Republican", string office = "", string phone = "", string url = "") =>
        $$"""{"type":"{{type}}","start":"{{start}}","end":"{{end}}","state":"{{state}}","party":"{{party}}","office":"{{office}}","phone":"{{phone}}","url":"{{url}}"}""";

    private static string Member(string bioguide = "A000001", string lis = "", string name = """{"official_full":"A"}""",
        string? terms = null, string? term = null) =>
        $$"""{"id":{"bioguide":"{{bioguide}}","lis":"{{lis}}"},"name":{{name}},"terms":[{{terms ?? term ?? Term()}}]}""";
}
