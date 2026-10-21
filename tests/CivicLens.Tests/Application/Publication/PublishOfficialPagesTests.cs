using System.Text;
using System.Text.Json;
using CivicLens.Application.Collection;
using CivicLens.Application.Publication;
using CivicLens.Core.Review;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Application.Publication;

public sealed class PublishOfficialPagesTests
{
    private static readonly string MayorPage = PublicationProtocol.OfficialPagePath("mayor");
    private static readonly string ChairPage = PublicationProtocol.OfficialPagePath("chair");

    [Fact]
    public async Task AnOfficialWithNoPublishedRecordGetsNoPageSoNoEmptyProfileExists()
    {
        var scenario = new PublicationScenario();

        var summary = await PublishAsync(scenario, "k", PublishedRecordKind.Statement, scenario.AddStatement('a'));

        var files = scenario.Releases.Directories[summary.DirectoryName];
        Assert.Contains(MayorPage, files.Keys);
        Assert.DoesNotContain(ChairPage, files.Keys);
        Assert.Contains("officials mayor:1</html>", Page(files, PublicationProtocol.OfficialsPagePath));
    }

    [Fact]
    public async Task ACurrentPageHoldsTheRecordsAndTheVotesOfThatOfficialOnly()
    {
        var scenario = new PublicationScenario();
        var statementOfMayor = scenario.AddStatement('a');
        var statementOfChair = scenario.AddStatement('b', speakerIds: ["chair"]);
        var vote = scenario.AddVote('c', memberIds: ["chair"]);

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k",
            new PublishedRecordReference(PublishedRecordKind.Statement, statementOfMayor),
            new PublishedRecordReference(PublishedRecordKind.Statement, statementOfChair),
            new PublishedRecordReference(PublishedRecordKind.Vote, vote)));

        var files = scenario.Releases.Directories[summary.DirectoryName];
        Assert.Equal($"<html>official mayor tracked current  past [] records [{statementOfMayor}] votes []</html>", Page(files, MayorPage));
        Assert.Equal($"<html>official chair tracked current 119 past [] records [{statementOfChair}] votes [{vote}]</html>", Page(files, ChairPage));
    }

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
        Assert.Contains("official chair untracked current 119", Page(files, ChairPage));
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
    public async Task WhenTheNewestCongressEmptiesTheNextOlderCongressBecomesCurrentAndItsPastPageIsDropped()
    {
        var scenario = new PublicationScenario();
        var older = scenario.AddVote('a', congress: 118);
        var newer = scenario.AddVote('b');
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1",
            new PublishedRecordReference(PublishedRecordKind.Vote, older), new PublishedRecordReference(PublishedRecordKind.Vote, newer)));
        var review = scenario.Votes.Publishable[newer].Review;
        var revised = review.CurrentRevision with
        {
            RevisionNumber = 2,
            Members = [review.CurrentRevision.Members[0] with { PersonId = "chair" }]
        };
        scenario.Votes.Publishable[newer] = scenario.Votes.Publishable[newer] with
        {
            Review = review with
            {
                CurrentRevision = revised,
                Revisions = review.Revisions.Add(revised),
                Decisions = review.Decisions.Add(PublicationScenario.Decision(newer, 2, ReviewDecisionKind.Approve, 2)),
                ReviewStateVersion = 2
            }
        };

        var second = await ReplaceAsync(scenario, "k2", PublishedRecordKind.Vote, newer);

        var files = scenario.Releases.Directories[second.DirectoryName];
        Assert.Equal($"<html>official mayor tracked current 118 past [] records [] votes [{older}]</html>", Page(files, MayorPage));
        Assert.DoesNotContain(PublicationProtocol.OfficialCongressPagePath("mayor", 118), files.Keys);
        Assert.DoesNotContain(PublicationProtocol.OfficialVotesPath("mayor", 119), files.Keys);
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
        scenario.SeedRelease(withFiles: true, [.. ids.Chunk(PublishedRecordEntry.MaximumFilterValues).Select((chunk, index) => new PublishedRecordEntry
        {
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
    public async Task APublishRewritesOnlyThePagesOfTheOfficialsItTouchesAndLinksEveryOtherPage()
    {
        var scenario = new PublicationScenario();
        var first = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1",
            new PublishedRecordReference(PublishedRecordKind.Statement, scenario.AddStatement('a')),
            new PublishedRecordReference(PublishedRecordKind.Statement, scenario.AddStatement('b', speakerIds: ["chair"]))));
        scenario.Releases.WriteOrder.Clear();
        scenario.Releases.Links.Clear();

        await PublishAsync(scenario, "k2", PublishedRecordKind.Statement, scenario.AddStatement('c', speakerIds: ["chair"]));

        Assert.Contains(ChairPage, scenario.Releases.WriteOrder);
        Assert.DoesNotContain(MayorPage, scenario.Releases.WriteOrder);
        Assert.Contains((first.DirectoryName, MayorPage), scenario.Releases.Links);
    }

    [Fact]
    public async Task APublishThatNamesNoOfficialRewritesNoOfficialFile()
    {
        var scenario = new PublicationScenario();
        await PublishAsync(scenario, "k1", PublishedRecordKind.Vote, scenario.AddVote('a'));
        scenario.Releases.WriteOrder.Clear();

        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k2", scenario.AddDraft('b')));

        Assert.DoesNotContain(scenario.Releases.WriteOrder, path => path.StartsWith("officials/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AReplacedRecordTouchesTheOfficialsOfItsOldRevisionToo()
    {
        var scenario = new PublicationScenario();
        var statement = scenario.AddStatement('a');
        var staying = scenario.AddStatement('b');
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1",
            new PublishedRecordReference(PublishedRecordKind.Statement, statement), new PublishedRecordReference(PublishedRecordKind.Statement, staying)));
        scenario.Releases.WriteOrder.Clear();
        var review = scenario.Statements.Publishable[statement].Review;
        var revised = review.CurrentRevision! with { RevisionNumber = 2, SpeakerOfficialIds = ["chair"] };
        scenario.Statements.Publishable[statement] = scenario.Statements.Publishable[statement] with
        {
            Review = review with
            {
                CurrentRevision = revised,
                Revisions = review.Revisions.Add(revised),
                Decisions = review.Decisions.Add(PublicationScenario.Decision(statement, 2, ReviewDecisionKind.Approve, 2)),
                ReviewStateVersion = 2
            }
        };

        await ReplaceAsync(scenario, "k2", PublishedRecordKind.Statement, statement);

        Assert.Contains(MayorPage, scenario.Releases.WriteOrder);
        Assert.Contains(ChairPage, scenario.Releases.WriteOrder);
    }

    [Fact]
    public async Task ARosterChangeRewritesTheCurrentPageButLinksTheVoteFileAndTheEarlierCongressPage()
    {
        var scenario = new PublicationScenario();
        scenario.ExternalIds["mayor"] = ("C000127", null);
        var first = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1",
            new PublishedRecordReference(PublishedRecordKind.Vote, scenario.AddVote('a', congress: 118)),
            new PublishedRecordReference(PublishedRecordKind.Vote, scenario.AddVote('b'))));
        scenario.RosterMembers = RosterFixtures.Members();
        scenario.Releases.WriteOrder.Clear();
        scenario.Releases.Links.Clear();

        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k2", scenario.AddDraft('c')));

        Assert.Contains(MayorPage, scenario.Releases.WriteOrder);
        Assert.DoesNotContain(PublicationProtocol.OfficialCongressPagePath("mayor", 118), scenario.Releases.WriteOrder);
        Assert.Contains((first.DirectoryName, PublicationProtocol.OfficialCongressPagePath("mayor", 118)), scenario.Releases.Links);
        Assert.Contains((first.DirectoryName, PublicationProtocol.OfficialVotesPath("mayor", 119)), scenario.Releases.Links);
    }

    [Fact]
    public async Task TheCurrentCongressIsTheNewestWithAVoteAndEarlierCongressesGetOnePageEach()
    {
        var scenario = new PublicationScenario();
        var older = scenario.AddVote('a', congress: 118);
        var newer = scenario.AddVote('b');

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k",
            new PublishedRecordReference(PublishedRecordKind.Vote, older), new PublishedRecordReference(PublishedRecordKind.Vote, newer)));

        var files = scenario.Releases.Directories[summary.DirectoryName];
        Assert.Equal($"<html>official mayor tracked current 119 past [118] records [] votes [{newer}]</html>", Page(files, MayorPage));
        Assert.Equal($"<html>official mayor tracked past 118 past [] records [] votes [{older}]</html>",
            Page(files, PublicationProtocol.OfficialCongressPagePath("mayor", 118)));
    }

    [Fact]
    public async Task ANewVoteOfTheCurrentCongressLeavesTheEarlierCongressFilesLinkedAndANewerCongressWritesThePageOfTheOldCurrent()
    {
        var scenario = new PublicationScenario();
        var first = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1",
            new PublishedRecordReference(PublishedRecordKind.Vote, scenario.AddVote('a', congress: 118)),
            new PublishedRecordReference(PublishedRecordKind.Vote, scenario.AddVote('b'))));
        scenario.Releases.WriteOrder.Clear();
        scenario.Releases.Links.Clear();

        var second = await PublishAsync(scenario, "k2", PublishedRecordKind.Vote, scenario.AddVote('c'));

        Assert.Contains((first.DirectoryName, PublicationProtocol.OfficialCongressPagePath("mayor", 118)), scenario.Releases.Links);
        Assert.Contains((first.DirectoryName, PublicationProtocol.OfficialVotesPath("mayor", 118)), scenario.Releases.Links);
        Assert.DoesNotContain(PublicationProtocol.OfficialCongressPagePath("mayor", 118), scenario.Releases.WriteOrder);
        Assert.Contains(PublicationProtocol.OfficialVotesPath("mayor", 119), scenario.Releases.WriteOrder);
        scenario.Releases.WriteOrder.Clear();
        scenario.Releases.Links.Clear();

        var third = await PublishAsync(scenario, "k3", PublishedRecordKind.Vote, scenario.AddVote('d', congress: 120));

        Assert.Contains(PublicationProtocol.OfficialCongressPagePath("mayor", 119), scenario.Releases.WriteOrder);
        Assert.Contains((second.DirectoryName, PublicationProtocol.OfficialVotesPath("mayor", 119)), scenario.Releases.Links);
        Assert.Contains("current 120 past [119,118]", Page(scenario.Releases.Directories[third.DirectoryName], MayorPage));
    }

    [Fact]
    public async Task ARenamedOfficialRewritesTheEarlierCongressPagesBecauseTheyShowTheName()
    {
        var scenario = new PublicationScenario();
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1",
            new PublishedRecordReference(PublishedRecordKind.Vote, scenario.AddVote('a', congress: 118)),
            new PublishedRecordReference(PublishedRecordKind.Vote, scenario.AddVote('b'))));
        scenario.OfficialNames["mayor"] = "Mayor Renamed";
        scenario.Releases.WriteOrder.Clear();

        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k2", scenario.AddDraft('c')));

        Assert.Contains(PublicationProtocol.OfficialCongressPagePath("mayor", 118), scenario.Releases.WriteOrder);
        Assert.DoesNotContain(PublicationProtocol.OfficialVotesPath("mayor", 118), scenario.Releases.WriteOrder);
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

    [Fact]
    public async Task RosterTermsWinOverConfiguredOfficesWhenBothExistForAPerson()
    {
        var scenario = new PublicationScenario();
        scenario.ExternalIds["mayor"] = (null, "Q22686");
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

    private static Task<PublicationReleaseSummary> ReplaceAsync(PublicationScenario scenario, string key, PublishedRecordKind kind, string recordId) =>
        scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.WithNotesFor(
            PublicationScenario.Request(key, new PublishedRecordReference(kind, recordId)), new PublishedRecordReference(kind, recordId)));

    private static string Page(Dictionary<string, byte[]> files, string path) => Encoding.UTF8.GetString(files[path]);

    private static PublishedOfficials Officials(Dictionary<string, byte[]> files) =>
        JsonSerializer.Deserialize<PublishedOfficials>(files[PublicationProtocol.OfficialsPath], PublicationProtocol.JsonOptions)!;
}
