using static CivicLens.Tests.Fixtures.StatementDraftFixtures;

namespace CivicLens.Tests.Application.Statements;

public sealed class StatementSpeakerRosterTests
{
    [Theory]
    [InlineData("2026-07-31", new[] { Warren })]
    [InlineData("2026-08-01", new[] { Markey, Warren })]
    public void AnOfficialIsListedOnlyOnDatesThatTheSourceCoversThem(string date, string[] expected) =>
        Assert.Equal(expected, Roster().OfficialsOn("source", DateOnly.Parse(date)).Select(official => official.Id).ToArray());

    [Fact]
    public void CoverageThatEndedDropsTheOfficialAndNamesComeFromThePeopleList()
    {
        var roster = Roster(warrenEndsBefore: new DateOnly(2026, 9, 1));

        Assert.Equal(["Ed Markey"], roster.OfficialsOn("source", new DateOnly(2026, 9, 1)).Select(official => official.Name).ToArray());
    }

    [Fact]
    public void AnUnknownSourceCoversNobody() => Assert.Empty(Roster().OfficialsOn("elsewhere", new DateOnly(2026, 9, 1)));
}
