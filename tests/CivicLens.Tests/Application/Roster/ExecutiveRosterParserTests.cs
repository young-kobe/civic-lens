using System.Text;
using CivicLens.Application.Roster;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Application.Roster;

public sealed class ExecutiveRosterParserTests
{
    private static ExecutiveOfficial[] Parse(string json) => ExecutiveRosterParser.Parse(Encoding.UTF8.GetBytes(json));

    [Fact]
    public void AnOfficialKeepsEveryTermInOrderWithItsOfficeSoOfficialPagesCanShowDatedTerms()
    {
        var biden = Parse(ExecutiveFixtures.Json).Single(official => official.GovtrackId == 300008);

        Assert.Equal(["viceprez", "viceprez", "prez"], biden.Terms.Select(term => term.Office));
        Assert.Equal([0, 1, 2], biden.Terms.Select(term => term.Index));
        Assert.Equal((new DateOnly(2021, 1, 20), new DateOnly(2025, 1, 20), "Democrat"),
            (biden.Terms[2].Start, biden.Terms[2].End, biden.Terms[2].Party));
    }

    [Fact]
    public void AnOfficialWithoutAWikidataIdIsKeptBecauseTheFileGivesOneToRecentOfficialsOnly()
    {
        var washington = Parse(ExecutiveFixtures.Json).Single(official => official.GovtrackId == 411351);

        Assert.Null(washington.WikidataId);
        Assert.Equal("Q22686", Parse(ExecutiveFixtures.Json).Single(official => official.GovtrackId == 412733).WikidataId);
    }

    [Fact]
    public void AnOfficialWithoutAnOfficialFullNameGetsTheFirstAndLastName()
    {
        Assert.Equal("Donald Trump", Parse(ExecutiveFixtures.Json).Single(official => official.GovtrackId == 412733).FullName);
    }

    [Fact]
    public void AValidMinimalOfficialParsesSoEveryRejectionBelowBreaksOneRule()
    {
        Assert.Single(Parse(Roster(Official())));
    }

    [Fact]
    public void AnEmptyListIsRejectedBecauseItWouldReadAsNoOfficeholders()
    {
        Assert.Throws<InvalidDataException>(() => Parse("[]"));
    }

    [Fact]
    public void ADocumentThatIsNotAListIsRejected()
    {
        Assert.Throws<InvalidDataException>(() => Parse("""{"not":"a list"}"""));
    }

    [Theory]
    [InlineData("""{"bioguide":"A000001"}""")]
    [InlineData("""{"govtrack":0}""")]
    [InlineData("""{"govtrack":-5}""")]
    public void AnOfficialWithoutAPositiveGovtrackIdIsRejected(string id)
    {
        Assert.Throws<InvalidDataException>(() => Parse(Roster(Official(id: id))));
    }

    [Fact]
    public void AGovtrackIdListedTwiceIsRejectedBecauseItNamesOnePerson()
    {
        Assert.Throws<InvalidDataException>(() => Parse(Roster(Official(), Official())));
    }

    [Fact]
    public void AWikidataIdListedTwiceIsRejectedBecausePublishJoinsAPersonByIt()
    {
        Assert.Throws<InvalidDataException>(() => Parse(Roster(
            Official("""{"govtrack":1,"wikidata":"Q76"}"""), Official("""{"govtrack":2,"wikidata":"Q76"}"""))));
    }

    [Theory]
    [InlineData("q76")]
    [InlineData("Q0")]
    [InlineData("Q076")]
    [InlineData("76")]
    public void AMalformedWikidataIdIsRejected(string wikidata)
    {
        Assert.Throws<InvalidDataException>(() => Parse(Roster(Official(id: $$"""{"govtrack":1,"wikidata":"{{wikidata}}"}"""))));
    }

    [Fact]
    public void AnOfficialWithoutTermsIsRejected()
    {
        Assert.Throws<InvalidDataException>(() => Parse(Roster(Official(terms: ""))));
    }

    [Fact]
    public void AnOfficialWithoutAnyNameIsRejected()
    {
        Assert.Throws<InvalidDataException>(() => Parse(Roster(Official(name: "{}"))));
    }

    [Fact]
    public void ATermOfAnOfficeOtherThanPresidentOrVicePresidentIsRejected()
    {
        Assert.Throws<InvalidDataException>(() => Parse(Roster(Official(terms: Term(type: "sen")))));
    }

    [Fact]
    public void ATermThatEndsBeforeItStartsIsRejected()
    {
        Assert.Throws<InvalidDataException>(() => Parse(Roster(Official(terms: Term(start: "2029-01-20", end: "2025-01-20")))));
    }

    [Fact]
    public void ATermWithoutAPartyIsRejected()
    {
        Assert.Throws<InvalidDataException>(() => Parse(Roster(Official(terms: Term(party: "")))));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void AValueLongerThanItsColumnIsRejectedAndOneAtTheLimitIsAccepted(int excess)
    {
        var values = new[]
        {
            Official(id: $$"""{"govtrack":1,"wikidata":"Q{{new string('1', RosterLimits.WikidataId - 1 + excess)}}"}"""),
            Official(name: $$"""{"official_full":"{{new string('x', RosterLimits.FullName + excess)}}"}"""),
            Official(terms: Term(party: new string('x', RosterLimits.Party + excess)))
        };

        foreach (var value in values)
        {
            if (excess == 0) Assert.Single(Parse(Roster(value)));
            else Assert.Throws<InvalidDataException>(() => Parse(Roster(value)));
        }
    }

    private static string Roster(params string[] officials) => $"[{string.Join(',', officials)}]";

    private static string Term(string type = "prez", string start = "2025-01-20", string end = "2029-01-20", string party = "Republican") =>
        $$"""{"type":"{{type}}","start":"{{start}}","end":"{{end}}","party":"{{party}}"}""";

    private static string Official(string id = """{"govtrack":1}""", string name = """{"official_full":"A"}""", string? terms = null) =>
        $$"""{"id":{{id}},"name":{{name}},"terms":[{{terms ?? Term()}}]}""";
}
