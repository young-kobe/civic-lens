using System.Text;
using System.Text.Json;
using CivicLens.Application.Publication;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Application.Publication;

public sealed class PublishPageRewriteTests
{
    private static readonly DateOnly Week = new(2026, 1, 5);

    [Fact]
    public async Task ALayoutChangeReachesEveryKindOfCarriedPageAndEachRecordFileStaysTheSameFile()
    {
        var scenario = new PublicationScenario();
        var records = await PublishEveryKindAsync(scenario);
        var first = scenario.Publications.Committed.Single().DirectoryName;
        scenario.PageLayout = " layout-2";
        scenario.Releases.Links.Clear();

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Rewrite("k2"));

        var second = scenario.Releases.Directories[summary.DirectoryName];
        foreach (var record in records.All)
        {
            Assert.EndsWith(" layout-2</html>", PageOf(scenario, summary.DirectoryName, record.RecordId));
            Assert.Same(scenario.Releases.Directories[first][PublicationProtocol.RecordDataPath(record.RecordId)], second[PublicationProtocol.RecordDataPath(record.RecordId)]);
            Assert.Contains((first, PublicationProtocol.RecordDataPath(record.RecordId)), scenario.Releases.Links);
            Assert.DoesNotContain((first, PublicationProtocol.RecordPagePath(record.RecordId)), scenario.Releases.Links);
        }
    }

    [Fact]
    public async Task ARewrittenPageKeepsItsBillLinkItsRelatedRecordsAndItsCorrectionsBecauseTheLayoutChangesNoFact()
    {
        var scenario = new PublicationScenario();
        var records = await PublishEveryKindAsync(scenario);
        scenario.PageLayout = " layout-2";

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Rewrite("k2"));

        Assert.Equal($"<html>vote {records.Vote.RecordId} bill {records.Action.RecordId} layout-2</html>", PageOf(scenario, summary.DirectoryName, records.Vote.RecordId));
        Assert.Equal($"<html>statement {records.Statement.RecordId} related e layout-2</html>", PageOf(scenario, summary.DirectoryName, records.Statement.RecordId));
        Assert.Equal($"<html>action {records.Action.RecordId} related e layout-2</html>", PageOf(scenario, summary.DirectoryName, records.Action.RecordId));
    }

    [Fact]
    public async Task TheRewriteAsksTheDatabaseForTheBillActionsOfAllCarriedVotesOnceBecauseOneQueryForEachVoteWouldCostTooMuch()
    {
        var scenario = new PublicationScenario();
        var records = await PublishEveryKindAsync(scenario);
        scenario.AddVote('7', billActionRecordIds: [records.Action.RecordId]);
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k2", new PublishedRecordReference(PublishedRecordKind.Vote, new string('7', 64))));
        var before = scenario.Votes.BillLookups;

        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Rewrite("k3"));

        Assert.Equal(before + 1, scenario.Votes.BillLookups);
    }

    [Fact]
    public async Task TheRewriteReadsAndRendersOneRecordBeforeItReadsTheNextSoPeakMemoryStaysAtAboutOnePage()
    {
        var scenario = new PublicationScenario();
        var statement = scenario.AddStatement('c');
        var action = scenario.AddAction('d');
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1",
            new PublishedRecordReference(PublishedRecordKind.Statement, statement), new PublishedRecordReference(PublishedRecordKind.Action, action)));
        scenario.Events.Clear();

        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Rewrite("k2"));

        Assert.Equal(["read c", "render c", "read d", "render d"], scenario.Events.Where(entry => entry.Contains(" c") || entry.Contains(" d")));
    }

    [Fact]
    public async Task AWithdrawnRecordStaysWithdrawnAfterTheRewriteBecauseOnlyCarriedRecordsAreRendered()
    {
        var scenario = new PublicationScenario();
        var records = await PublishEveryKindAsync(scenario);
        var other = scenario.AddStatement('b');
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k2", new PublishedRecordReference(PublishedRecordKind.Statement, other)));
        var withdrawn = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            new PublishReleaseRequest([], "k3") { Withdrawals = [new(PublishedRecordKind.Statement, other, "Wrong.")] });
        var page = PageOf(scenario, withdrawn.DirectoryName, other);
        scenario.PageLayout = " layout-2";

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Rewrite("k4"));

        Assert.Equal(page, PageOf(scenario, summary.DirectoryName, other));
        Assert.DoesNotContain("layout-2", page);
        var manifest = JsonSerializer.Deserialize<PublicationRelease>(
            scenario.Releases.Directories[summary.DirectoryName][PublicationProtocol.ManifestPath], PublicationProtocol.JsonOptions)!;
        Assert.Equal(other, Assert.Single(manifest.Withdrawals).RecordId);
        Assert.DoesNotContain(manifest.Records, entry => entry.RecordId == other);
        Assert.EndsWith(" layout-2</html>", PageOf(scenario, summary.DirectoryName, records.Statement.RecordId));
    }

    [Fact]
    public async Task APlainPublishStillHardLinksEveryCarriedPageBecauseOnlyTheOptionRendersThemAgain()
    {
        var scenario = new PublicationScenario();
        var records = await PublishEveryKindAsync(scenario);
        var first = scenario.Publications.Committed.Single().DirectoryName;
        scenario.PageLayout = " layout-2";
        scenario.Releases.Links.Clear();

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            PublicationScenario.Request("k2", new PublishedRecordReference(PublishedRecordKind.Statement, scenario.AddStatement('b'))));

        foreach (var record in new[] { records.Action, records.Vote, records.Document, records.Executive })
        {
            Assert.Contains((first, PublicationProtocol.RecordPagePath(record.RecordId)), scenario.Releases.Links);
            Assert.DoesNotContain("layout-2", PageOf(scenario, summary.DirectoryName, record.RecordId));
        }
    }

    [Fact]
    public async Task ARenderFailureWhileStagingLeavesNoReleaseAndDiscardsTheStagedDirectory()
    {
        var scenario = new PublicationScenario();
        var records = await PublishEveryKindAsync(scenario);
        var committed = scenario.Publications.Committed.Count;
        var discarded = scenario.Releases.DiscardedStagings;
        scenario.RenderFailureOfStatement = records.Statement.RecordId;

        await Assert.ThrowsAsync<InvalidOperationException>(() => scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Rewrite("k2")));

        Assert.Equal(committed, scenario.Publications.Committed.Count);
        Assert.Equal(discarded + 1, scenario.Releases.DiscardedStagings);
    }

    [Fact]
    public async Task TheRewritePagesOptionIsInThePayloadHashSoTheSameKeyIsAMismatchWithoutIt()
    {
        var scenario = new PublicationScenario();
        await PublishEveryKindAsync(scenario);
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Rewrite("k2"));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, new PublishReleaseRequest([], "k2")));
    }

    private static PublishReleaseRequest Rewrite(string key) => new([], key) { RewritePages = true };

    private static string PageOf(PublicationScenario scenario, string directory, string recordId) =>
        Encoding.UTF8.GetString(scenario.Releases.Directories[directory][PublicationProtocol.RecordPagePath(recordId)]);

    private static async Task<Published> PublishEveryKindAsync(PublicationScenario scenario)
    {
        var statement = scenario.AddStatement('c', statementDate: new DateOnly(2026, 1, 2));
        var action = scenario.AddAction('d');
        var vote = scenario.AddVote('f', billActionRecordIds: [action]);
        var document = scenario.AddDraft('a');
        var executive = scenario.AddExecutiveAction('9');
        var relationship = scenario.AddRelationship('e', statement, action);
        var brief = scenario.AddBrief(Week, [new(PublishedRecordKind.Statement, statement, 1)]);
        var all = new Published(
            new(PublishedRecordKind.Statement, statement), new(PublishedRecordKind.Action, action), new(PublishedRecordKind.Vote, vote),
            new(PublishedRecordKind.DocumentChange, document), new(PublishedRecordKind.ExecutiveAction, executive),
            new(PublishedRecordKind.Relationship, relationship), new(PublishedRecordKind.Brief, brief));
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1", [.. all.All]));
        return all;
    }

    private sealed record Published(PublishedRecordReference Statement, PublishedRecordReference Action, PublishedRecordReference Vote,
        PublishedRecordReference Document, PublishedRecordReference Executive, PublishedRecordReference Relationship, PublishedRecordReference Brief)
    {
        public IEnumerable<PublishedRecordReference> All => [Statement, Action, Vote, Document, Executive, Relationship, Brief];
    }
}
