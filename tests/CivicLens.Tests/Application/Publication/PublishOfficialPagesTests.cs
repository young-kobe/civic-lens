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
        Assert.Equal($"<html>official mayor tracked current  congresses [] records [{statementOfMayor}] votes []</html>", Page(files, MayorPage));
        Assert.Equal($"<html>official chair tracked current 119 congresses [119] records [{statementOfChair}] votes [{vote}]</html>", Page(files, ChairPage));
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
        Assert.Equal($"<html>official mayor tracked current 118 congresses [118] records [] votes [{older}]</html>", Page(files, MayorPage));
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
    public async Task ARewriteOfAllOfficialsWritesTheCurrentAndPastPagesOfEveryOfficialAndLinksNone()
    {
        var scenario = await TwoOfficialsWithPastCongressAsync();
        var mayorPast = PublicationProtocol.OfficialCongressPagePath("mayor", 118);

        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, new PublishReleaseRequest([], "k2") { RewriteOfficials = true });

        Assert.Contains(MayorPage, scenario.Releases.WriteOrder);
        Assert.Contains(ChairPage, scenario.Releases.WriteOrder);
        Assert.Contains(mayorPast, scenario.Releases.WriteOrder);
        Assert.DoesNotContain(scenario.Releases.Links, link => link.Item2.StartsWith("officials/", StringComparison.Ordinal) && link.Item2.EndsWith(".html", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WithoutTheRewriteOptionAnUntouchedOfficialKeepsItsCurrentAndPastPagesByLink()
    {
        var scenario = await TwoOfficialsWithPastCongressAsync();

        var second = await PublishAsync(scenario, "k2", PublishedRecordKind.Statement, scenario.AddStatement('d', speakerIds: ["chair"]));

        Assert.DoesNotContain(MayorPage, scenario.Releases.WriteOrder);
        Assert.DoesNotContain(PublicationProtocol.OfficialCongressPagePath("mayor", 118), scenario.Releases.WriteOrder);
        Assert.Contains(scenario.Releases.Links, link => link.Item2 == MayorPage);
        Assert.Contains(scenario.Releases.Links, link => link.Item2 == PublicationProtocol.OfficialCongressPagePath("mayor", 118));
        Assert.Equal(2, second.ReleaseNumber);
    }

    [Fact]
    public async Task ARewriteOfAllOfficialsKeepsEveryRecordFirstPublicationDateAndEveryWithdrawal()
    {
        var scenario = new PublicationScenario();
        var wrong = scenario.AddStatement('e');
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1",
            new PublishedRecordReference(PublishedRecordKind.Statement, scenario.AddStatement('a')),
            new PublishedRecordReference(PublishedRecordKind.Statement, wrong)));
        scenario.Publications.ReviewStatuses[new(PublishedRecordKind.Statement, wrong)] = ReviewStatus.ApprovalWithdrawn;
        var second = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            new PublishReleaseRequest([], "k2") { Withdrawals = [new(PublishedRecordKind.Statement, wrong, "Wrong.")] });

        var third = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, new PublishReleaseRequest([], "k3") { RewriteOfficials = true });

        var before = Manifest(scenario.Releases.Directories[second.DirectoryName]);
        var after = Manifest(scenario.Releases.Directories[third.DirectoryName]);
        Assert.Equal(3, after.ReleaseNumber);
        Assert.Equal(before.Records.Select(record => (record.RecordId, record.FirstPublishedAtUtc)), after.Records.Select(record => (record.RecordId, record.FirstPublishedAtUtc)));
        Assert.Equal(wrong, Assert.Single(after.Withdrawals).RecordId);
        Assert.Contains(scenario.Releases.Links, link => link.Item2 == PublicationProtocol.RecordPagePath(before.Records[0].RecordId));
    }

    [Fact]
    public async Task ARewrittenPastPageHoldsTheVoteRowsOfItsCongress()
    {
        var scenario = new PublicationScenario();
        var older = scenario.AddVote('a', congress: 118);
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1",
            new PublishedRecordReference(PublishedRecordKind.Vote, older),
            new PublishedRecordReference(PublishedRecordKind.Vote, scenario.AddVote('b'))));

        var second = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, new PublishReleaseRequest([], "k2") { RewriteOfficials = true });

        Assert.Contains($"votes [{older}]", Page(scenario.Releases.Directories[second.DirectoryName], PublicationProtocol.OfficialCongressPagePath("mayor", 118)));
    }

    [Fact]
    public async Task ARewriteLeavesAWithdrawnVoteOffTheRewrittenPages()
    {
        var scenario = new PublicationScenario();
        var wrong = scenario.AddVote('a', congress: 118);
        var kept = scenario.AddVote('b', congress: 118);
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1",
            new PublishedRecordReference(PublishedRecordKind.Vote, wrong),
            new PublishedRecordReference(PublishedRecordKind.Vote, kept),
            new PublishedRecordReference(PublishedRecordKind.Vote, scenario.AddVote('c'))));
        scenario.Publications.ReviewStatuses[new(PublishedRecordKind.Vote, wrong)] = ReviewStatus.ApprovalWithdrawn;
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            new PublishReleaseRequest([], "k2") { Withdrawals = [new(PublishedRecordKind.Vote, wrong, "Wrong.")] });

        var third = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, new PublishReleaseRequest([], "k3") { RewriteOfficials = true });

        var page = Page(scenario.Releases.Directories[third.DirectoryName], PublicationProtocol.OfficialCongressPagePath("mayor", 118));
        Assert.Contains($"votes [{kept}]", page);
        Assert.DoesNotContain(wrong, page);
    }

    [Fact]
    public async Task TheRewriteOptionIsInThePayloadHashSoTheSameKeyReplaysWithItAndIsAMismatchWithoutIt()
    {
        var scenario = await TwoOfficialsWithPastCongressAsync();
        var request = new PublishReleaseRequest([], "k2") { RewriteOfficials = true };
        var first = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, request);
        var committed = scenario.Publications.Committed.Count;

        var replay = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, request);

        Assert.Equal(first, replay);
        Assert.Equal(committed, scenario.Publications.Committed.Count);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, new PublishReleaseRequest([], "k2")));
    }

    private static async Task<PublicationScenario> TwoOfficialsWithPastCongressAsync()
    {
        var scenario = new PublicationScenario();
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1",
            new PublishedRecordReference(PublishedRecordKind.Vote, scenario.AddVote('a', congress: 118)),
            new PublishedRecordReference(PublishedRecordKind.Vote, scenario.AddVote('b')),
            new PublishedRecordReference(PublishedRecordKind.Statement, scenario.AddStatement('c', speakerIds: ["chair"]))));
        scenario.Releases.WriteOrder.Clear();
        scenario.Releases.Links.Clear();
        return scenario;
    }

    private static PublicationRelease Manifest(Dictionary<string, byte[]> files) =>
        JsonSerializer.Deserialize<PublicationRelease>(files[PublicationProtocol.ManifestPath], PublicationProtocol.JsonOptions)!;

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
    public async Task AWebsiteChangeRewritesTheOfficialPageBecauseTheProfileChanged()
    {
        var scenario = new PublicationScenario();
        var first = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1",
            new PublishedRecordReference(PublishedRecordKind.Vote, scenario.AddVote('a'))));
        scenario.Websites["mayor"] = "https://mayor.example.gov";
        scenario.Releases.WriteOrder.Clear();
        scenario.Releases.Links.Clear();

        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k2", scenario.AddDraft('c')));

        Assert.Contains(MayorPage, scenario.Releases.WriteOrder);
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
        Assert.Equal($"<html>official mayor tracked current 119 congresses [119,118] records [] votes [{newer}]</html>", Page(files, MayorPage));
        Assert.Equal($"<html>official mayor tracked past 118 congresses [119,118] records [] votes [{older}]</html>",
            Page(files, PublicationProtocol.OfficialCongressPagePath("mayor", 118)));
    }

    [Fact]
    public async Task ANewVoteOfTheCurrentCongressLeavesTheEarlierCongressFilesLinkedAndANewerCongressRewritesEveryEarlierPageBecauseTheirTabsListEveryCongress()
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
        Assert.Contains(PublicationProtocol.OfficialCongressPagePath("mayor", 118), scenario.Releases.WriteOrder);
        Assert.Contains((second.DirectoryName, PublicationProtocol.OfficialVotesPath("mayor", 119)), scenario.Releases.Links);
        Assert.Contains("current 120 congresses [120,119,118]", Page(scenario.Releases.Directories[third.DirectoryName], MayorPage));
    }

    [Fact]
    public async Task WithdrawingTheOnlyVoteOfAMiddleCongressRewritesTheOtherEarlierPageAndDropsThePageOfTheEmptiedCongress()
    {
        var scenario = new PublicationScenario();
        var middle = scenario.AddVote('b', congress: 118);
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1",
            new PublishedRecordReference(PublishedRecordKind.Vote, scenario.AddVote('a')),
            new PublishedRecordReference(PublishedRecordKind.Vote, middle),
            new PublishedRecordReference(PublishedRecordKind.Vote, scenario.AddVote('c', congress: 117))));
        scenario.Publications.ReviewStatuses[new(PublishedRecordKind.Vote, middle)] = ReviewStatus.ApprovalWithdrawn;
        scenario.Releases.WriteOrder.Clear();

        var second = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            new PublishReleaseRequest([], "k2") { Withdrawals = [new(PublishedRecordKind.Vote, middle, "Wrong.")] });

        var oldest = PublicationProtocol.OfficialCongressPagePath("mayor", 117);
        Assert.Contains(oldest, scenario.Releases.WriteOrder);
        Assert.Contains("congresses [119,117]", Page(scenario.Releases.Directories[second.DirectoryName], oldest));
        Assert.DoesNotContain(PublicationProtocol.OfficialCongressPagePath("mayor", 118), scenario.Releases.Directories[second.DirectoryName].Keys);
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

    [Fact]
    public async Task TheCurrentPageGetsTheProfileOfItsCongressWithTheStatementTheSponsoredBillAndTheVoteAndACosponsorGetsNoPage()
    {
        var scenario = new PublicationScenario();
        var statement = scenario.AddStatement('a', statementDate: new DateOnly(2026, 1, 1));
        var action = scenario.AddAction('b');
        var vote = scenario.AddVote('c');

        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k",
            new PublishedRecordReference(PublishedRecordKind.Statement, statement), new PublishedRecordReference(PublishedRecordKind.Action, action),
            new PublishedRecordReference(PublishedRecordKind.Vote, vote)));

        var page = scenario.OfficialPages.Single(page => page.Profile.Id == "mayor" && page.IsCurrent);
        var profile = page.TalkAndAction!;
        Assert.Equal(119, page.Congress);
        Assert.Equal([1, 1, 1], [profile.Statements.CountOf("housing"), profile.SponsoredOn("housing").Length, profile.Votes.CountOf("housing")]);
        Assert.DoesNotContain(scenario.OfficialPages, page => page.Profile.Id == "chair");
    }

    [Fact]
    public async Task TheProfileOfThePageCountsTheMissedVotesOfItsOwnVoteRows()
    {
        var scenario = new PublicationScenario();
        var missed = scenario.AddVote('a', cast: "Not Voting", totalLines: ["Party total D: Yea 0, Nay 0, Present 0, Not voting 1"]);
        var cast = scenario.AddVote('b');

        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k",
            new PublishedRecordReference(PublishedRecordKind.Vote, missed), new PublishedRecordReference(PublishedRecordKind.Vote, cast)));

        var page = scenario.OfficialPages.Single(page => page.Profile.Id == "mayor" && page.IsCurrent);
        var chamber = Assert.Single(page.TalkAndAction!.MissedByChamber);
        Assert.Equal((PublishedChamber.House, 1, 2), (chamber.Chamber, chamber.Missed, chamber.Held));
    }

    [Fact]
    public async Task OnlyTheCurrentPageOfAnOfficialWithAVoteCarriesAProfile()
    {
        var scenario = new PublicationScenario();
        var older = scenario.AddVote('a', congress: 118);
        var newer = scenario.AddVote('b');
        var statementOnly = scenario.AddStatement('c', speakerIds: ["chair"]);

        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k",
            new PublishedRecordReference(PublishedRecordKind.Vote, older), new PublishedRecordReference(PublishedRecordKind.Vote, newer),
            new PublishedRecordReference(PublishedRecordKind.Statement, statementOnly)));

        Assert.Equal([119], scenario.OfficialPages.Where(page => page.Profile.Id == "mayor" && page.TalkAndAction is not null)
            .Select(page => page.Congress!.Value));
        Assert.All(scenario.OfficialPages.Where(page => page.Profile.Id == "chair" || !page.IsCurrent), page => Assert.Null(page.TalkAndAction));
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
