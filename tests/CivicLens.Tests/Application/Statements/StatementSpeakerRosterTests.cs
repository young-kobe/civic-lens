using CivicLens.Application.Collection;
using CivicLens.Application.Statements;
using static CivicLens.Tests.Fixtures.StatementDraftFixtures;

namespace CivicLens.Tests.Application.Statements;

public sealed class StatementSpeakerRosterTests
{
    [Theory]
    [InlineData("2026-07-31", new[] { Warren })]
    [InlineData("2026-08-01", new[] { Markey, Warren })]
    public void AnOfficialIsListedOnlyOnDatesThatTheSourceCoversThem(string date, string[] expected) =>
        Assert.Equal(expected, OfficialIds(SpeakerRevision(), "source", date));

    [Fact]
    public void CoverageThatEndedDropsTheOfficialAndNamesComeFromThePeopleList()
    {
        var revision = SpeakerRevision(warrenEndsBefore: new DateOnly(2026, 9, 1));

        Assert.Equal(["Ed Markey"],
            new StatementSpeakerRoster().OfficialsOn(revision, "source", new DateOnly(2026, 9, 1)).Select(official => official.Name).ToArray());
    }

    [Fact]
    public void TheSameLookupReturnsTheOfficeOwnerOfTheSourceSoCodeApprovalNeedsNoSecondSearch()
    {
        var revision = CollectionConfigurationRevision.Create(OwnOffice(Warren));

        var speakers = new StatementSpeakerRoster().SpeakersOn(revision, "source", new DateOnly(2026, 9, 1));

        Assert.Equal(Warren, speakers.OfficeOf);
        Assert.Equal([Warren], speakers.Officials.Select(official => official.Id).ToArray());
    }

    [Fact]
    public void AnUnknownSourceCoversNobody() => Assert.Empty(OfficialIds(SpeakerRevision(), "elsewhere", "2026-09-01"));

    [Fact]
    public void NoRevisionCoversNobody() => Assert.Empty(OfficialIds(null, "source", "2026-09-01"));

    private static string[] OfficialIds(CollectionConfigurationRevision? revision, string sourceId, string date) =>
        [.. new StatementSpeakerRoster().OfficialsOn(revision, sourceId, DateOnly.Parse(date)).Select(official => official.Id)];
}
