using System.Text.Json;
using CivicLens.Application.Collection;
using CivicLens.Application.Publication;
using CivicLens.Core.Review;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Application.Publication;

public sealed class PublishOfficialFilesTests
{
    [Fact]
    public async Task AnOfficialWhoLeftTheConfigurationKeepsAPageWithTheNameOfTheVoteRecordAndNoTerms()
    {
        var scenario = new PublicationScenario();
        scenario.ExternalIds["chair"] = ("C000127", null);
        scenario.RosterMembers = RosterFixtures.Members();
        await PublishAsync(scenario, "k1", PublishedRecordKind.Vote, scenario.AddVote('a', memberIds: ["chair"]));
        scenario.OfficialNames.Remove("chair");

        var second = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k2", scenario.AddDraft('b')));

        var files = scenario.Releases.Directories[second.DirectoryName];
        var chair = Assert.Single(Officials(files).Officials);
        Assert.Equal(("Council Chair Example", 0), (chair.Name, chair.Terms.Length));
    }

    [Fact]
    public async Task AnOfficialWhoLeftTheConfigurationAndIsNamedByAnEntryTakesTheNameOfTheEntryWithoutReadingAVoteRecord()
    {
        var scenario = new PublicationScenario();
        await PublishAsync(scenario, "k1", PublishedRecordKind.Statement, scenario.AddStatement('a', speakerIds: ["chair"]));
        scenario.OfficialNames.Remove("chair");
        scenario.Releases.ReadPaths.Clear();

        var second = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k2", scenario.AddDraft('b')));

        Assert.Equal(["Council Chair Example"], Officials(scenario.Releases.Directories[second.DirectoryName]).Officials.Select(official => official.Name));
        Assert.DoesNotContain(scenario.Releases.ReadPaths, path => path.StartsWith("records/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task APublishWithNoEarlierProfileStillNamesAnOfficialWhoLeftTheConfigurationFromTheRecords()
    {
        var scenario = new PublicationScenario();
        var first = await PublishAsync(scenario, "k1", PublishedRecordKind.Vote, scenario.AddVote('a', memberIds: ["chair"]));
        scenario.OfficialNames.Remove("chair");
        scenario.Releases.Directories[first.DirectoryName][PublicationProtocol.OfficialsPath] =
            JsonSerializer.SerializeToUtf8Bytes(new PublishedOfficials { Officials = [] }, PublicationProtocol.JsonOptions);

        var second = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k2", scenario.AddDraft('b')));

        Assert.Equal(["Council Chair Example"], Officials(scenario.Releases.Directories[second.DirectoryName]).Officials.Select(official => official.Name));
    }

    [Fact]
    public async Task APublishFailsLoudlyWhenNoRecordHoldsTheNameOfAnOfficialWhoLeftTheConfiguration()
    {
        var scenario = new PublicationScenario();
        var vote = scenario.AddVote('a', memberIds: ["chair"]);
        var first = await PublishAsync(scenario, "k1", PublishedRecordKind.Vote, vote);
        scenario.OfficialNames.Remove("chair");
        var path = PublicationProtocol.RecordDataPath(vote);
        var record = JsonSerializer.Deserialize<PublishedVote>(scenario.Releases.Directories[first.DirectoryName][path], PublicationProtocol.JsonOptions)!;
        scenario.Releases.Directories[first.DirectoryName][path] = JsonSerializer.SerializeToUtf8Bytes(
            record with { Members = [record.Members[0] with { Official = new() { Id = "other", Name = "Other" } }] }, PublicationProtocol.JsonOptions);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k2", scenario.AddDraft('b'))));
    }

    [Fact]
    public async Task AnOfficialsFileOverTheByteCapIsRefusedBeforeItIsWritten()
    {
        var scenario = new PublicationScenario();
        var title = new string('t', 256);
        var offices = Enumerable.Range(0, PublishedOfficials.MaximumTerms)
            .Select(index => new OfficeConfiguration { Title = title, StartsOn = new DateOnly(2000, 1, 1).AddDays(index) }).ToArray();
        var ids = Enumerable.Range(0, PublishedOfficials.MaximumOfficials).Select(index => $"p{index}").ToArray();
        foreach (var id in ids)
        {
            scenario.OfficialNames[id] = id;
            scenario.Offices[id] = offices;
        }
        await scenario.SeedReleaseWithEmptyCorrectionHistoriesAsync(withFiles: true, [.. ids.Chunk(PublishedRecordEntry.MaximumFilterValues).Select((chunk, index) => new PublishedRecordEntry
        {
            IssueIds = [],
            Kind = PublishedRecordKind.DocumentChange,
            PolicyAreaIds = [],
            RecordId = index.ToString("x32"),
            RevisionNumber = 1,
            Headline = "Seeded",
            FirstPublishedAtUtc = PublicationScenario.ApprovedAt,
            Officials = [.. chunk.Select(id => new PublishedOfficial { Id = id, Name = id })]
        })]);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k", scenario.AddDraft('a'))));
    }

    [Fact]
    public async Task AConfiguredOfficeIsTheTermOfAPersonWithNoRosterTerms()
    {
        var scenario = new PublicationScenario();
        scenario.Offices["mayor"] = [new() { Title = "Secretary of State", StartsOn = new(2025, 1, 21), Party = "Republican" }];

        var summary = await PublishAsync(scenario, "k", PublishedRecordKind.Statement, scenario.AddStatement('a'));

        var term = Assert.Single(Assert.Single(Officials(scenario.Releases.Directories[summary.DirectoryName]).Officials).Terms);
        Assert.Equal(("Secretary of State", "Republican", new DateOnly(2025, 1, 21), null), (term.Office, term.Party, term.Start, term.End));
    }

    [Theory]
    [InlineData("C000127", "Representative,Senator")]
    [InlineData("N000147", "Representative,Representative")]
    public async Task AMemberTermIsKindSenatorOrRepresentativeByItsChamberSoTheListGroupsASenatorWhoWasARepresentative(string bioguideId, string expected)
    {
        var scenario = new PublicationScenario();
        scenario.ExternalIds["mayor"] = (bioguideId, null);
        scenario.RosterMembers = RosterFixtures.Members();

        var summary = await PublishAsync(scenario, "k", PublishedRecordKind.Statement, scenario.AddStatement('a'));

        var kinds = Assert.Single(Officials(scenario.Releases.Directories[summary.DirectoryName]).Officials).Terms.Select(term => term.Kind.ToString());
        Assert.Equal(expected.Split(','), kinds);
    }

    [Fact]
    public async Task AnExecutiveRosterTermIsKindExecutiveSoThePresidentIsNotInOtherOfficials()
    {
        var scenario = new PublicationScenario();
        scenario.ExternalIds["mayor"] = ("V000137", "Q22686");
        scenario.ExecutiveOfficials = ExecutiveFixtures.Officials();

        var summary = await PublishAsync(scenario, "k", PublishedRecordKind.Statement, scenario.AddStatement('a'));

        var terms = Assert.Single(Officials(scenario.Releases.Directories[summary.DirectoryName]).Officials).Terms;
        Assert.All(terms, term => Assert.Equal(PublishedOfficeKind.Executive, term.Kind));
    }

    [Theory]
    [InlineData(ConfiguredOfficeKind.Executive, PublishedOfficeKind.Executive)]
    [InlineData(ConfiguredOfficeKind.Judicial, PublishedOfficeKind.Judicial)]
    [InlineData(null, PublishedOfficeKind.Other)]
    public async Task AConfiguredOfficeTakesItsConfiguredKindElseOtherSoOnlyAnOfficeWithAKindLeavesTheOtherOfficialsTab(ConfiguredOfficeKind? kind, PublishedOfficeKind expected)
    {
        var scenario = new PublicationScenario();
        scenario.Offices["mayor"] = [new() { Title = "Chief Justice", StartsOn = new(2005, 9, 29), Kind = kind }];

        var summary = await PublishAsync(scenario, "k", PublishedRecordKind.Statement, scenario.AddStatement('a'));

        Assert.Equal(expected, Assert.Single(Assert.Single(Officials(scenario.Releases.Directories[summary.DirectoryName]).Officials).Terms).Kind);
    }

    [Fact]
    public async Task APersonWithABioguideIdAndNoMemberTermShowsTheConfiguredOffice()
    {
        var scenario = new PublicationScenario();
        scenario.ExternalIds["mayor"] = ("M000001", null);
        scenario.Offices["mayor"] = [new() { Title = "Vice President", StartsOn = new(2025, 1, 20) }];

        var summary = await PublishAsync(scenario, "k", PublishedRecordKind.Statement, scenario.AddStatement('a'));

        Assert.Equal(["Vice President"], Assert.Single(Officials(scenario.Releases.Directories[summary.DirectoryName]).Officials).Terms.Select(term => term.Office));
    }

    [Fact]
    public async Task ExecutiveTermsWinOverConfiguredOfficesForAFormerMemberAbsentFromTheMemberSnapshot()
    {
        var scenario = new PublicationScenario();
        scenario.ExternalIds["mayor"] = ("V000137", "Q22686");
        scenario.RosterMembers = RosterFixtures.Members();
        scenario.ExecutiveOfficials = ExecutiveFixtures.Officials();
        scenario.Offices["mayor"] = [new() { Title = "Secretary of State", StartsOn = new(2025, 1, 21) }];

        var summary = await PublishAsync(scenario, "k", PublishedRecordKind.Statement, scenario.AddStatement('a'));

        Assert.Equal(["prez", "prez"], Assert.Single(Officials(scenario.Releases.Directories[summary.DirectoryName]).Officials).Terms.Select(term => term.Office));
    }

    [Fact]
    public async Task RosterTermsWinOverConfiguredOfficesWhenBothExistForAPerson()
    {
        var scenario = new PublicationScenario();
        scenario.ExternalIds["mayor"] = (null, "Q22686");
        scenario.RosterMembers = RosterFixtures.Members();
        scenario.ExecutiveOfficials = ExecutiveFixtures.Officials();
        scenario.Offices["mayor"] = [new() { Title = "Secretary of State", StartsOn = new(2025, 1, 21) }];

        var summary = await PublishAsync(scenario, "k", PublishedRecordKind.Statement, scenario.AddStatement('a'));

        Assert.DoesNotContain("Secretary of State", Assert.Single(Officials(scenario.Releases.Directories[summary.DirectoryName]).Officials).Terms.Select(term => term.Office));
    }

    [Fact]
    public async Task APreviousOfficialsFileOverTheByteCapIsRefusedOnRead()
    {
        var scenario = new PublicationScenario();
        var first = await PublishAsync(scenario, "k1", PublishedRecordKind.Statement, scenario.AddStatement('a'));
        scenario.Releases.Directories[first.DirectoryName][PublicationProtocol.OfficialsPath] = new byte[PublishedOfficials.MaximumBytes + 1];

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k2", scenario.AddDraft('b'))));
    }

    private static Task<PublicationReleaseSummary> PublishAsync(PublicationScenario scenario, string key, PublishedRecordKind kind, string recordId) =>
        scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request(key, new PublishedRecordReference(kind, recordId)));

    private static PublishedOfficials Officials(Dictionary<string, byte[]> files) =>
        JsonSerializer.Deserialize<PublishedOfficials>(files[PublicationProtocol.OfficialsPath], PublicationProtocol.JsonOptions)!;
}
