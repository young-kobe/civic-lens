using System.Text.Json;
using CivicLens.Application.Publication;
using CivicLens.Core.Review;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Application.Publication;

public sealed class PublishStatementTests
{
    [Fact]
    public async Task OneReleaseHoldsAStatementAndADocumentChangeEachUnderItsOwnKind()
    {
        var scenario = new PublicationScenario();
        var change = scenario.AddDraft('a');
        var statement = scenario.AddStatement('b', speakerIds: ["mayor"]);

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k",
            new PublishedRecordReference(PublishedRecordKind.Statement, statement),
            new PublishedRecordReference(PublishedRecordKind.DocumentChange, change)));

        var manifest = ReadManifest(scenario, summary.DirectoryName);
        Assert.Equal(PublicationProtocol.SchemaVersion, manifest.SchemaVersion);
        Assert.Equal([PublishedRecordKind.DocumentChange, PublishedRecordKind.Statement],
            manifest.Records.OrderBy(entry => entry.RecordId.Length).Select(entry => entry.Kind));
        var files = scenario.Releases.Directories[summary.DirectoryName];
        Assert.Contains(PublicationProtocol.RecordDataPath(change), files.Keys);
        Assert.Contains(PublicationProtocol.RecordPagePath(change), files.Keys);
        Assert.Contains(PublicationProtocol.RecordDataPath(statement), files.Keys);
        Assert.Contains(PublicationProtocol.RecordPagePath(statement), files.Keys);
        var record = JsonSerializer.Deserialize<PublishedStatement>(files[PublicationProtocol.RecordDataPath(statement)],
            PublicationProtocol.JsonOptions)!;
        Assert.Equal("housing matters", Assert.Single(record.Quotations).Quote);
        Assert.Equal(PublishedQuotationKind.DirectQuotation, record.Quotations[0].Kind);
        Assert.Equal("Mayor Example", Assert.Single(record.Speakers).Name);
        Assert.Equal(PublicationScenario.ApprovedAt, record.ApprovedAtUtc);
        Assert.Equal(scenario.Clock.Now, record.FirstPublishedAtUtc);
    }

    [Fact]
    public async Task ARequestWithOnlyDocumentChangesNeverAsksTheStatementStoreBecauseItHasNothingToLoad()
    {
        var scenario = new PublicationScenario();

        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k", scenario.AddDraft('a')));

        Assert.Equal(0, scenario.Statements.PublicationReadCount);
    }

    [Fact]
    public async Task ARequestWithOnlyStatementsNeverAsksTheDocumentChangeStore()
    {
        var scenario = new PublicationScenario();

        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k",
            new PublishedRecordReference(PublishedRecordKind.Statement, scenario.AddStatement('b'))));

        Assert.Equal(0, scenario.Reviews.ReadCount);
    }

    [Fact]
    public async Task CommitBindsEachRecordToItsKindAndReviewStateSoTheStoreChecksTheRightDraftTable()
    {
        var scenario = new PublicationScenario();
        var statement = scenario.AddStatement('b');

        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k",
            new PublishedRecordReference(PublishedRecordKind.Statement, statement)));

        var commit = scenario.Publications.LastCommit!;
        Assert.Equal(new PublishedRevisionBinding(PublishedRecordKind.Statement, statement, 1), Assert.Single(commit.Records));
        Assert.Equal(new ReviewStateExpectation(PublishedRecordKind.Statement, statement, 1, 1), Assert.Single(commit.AddedRecords));
    }

    [Fact]
    public async Task StatementAlreadyPublishedAtItsCurrentRevisionIsRejectedSoReleasesDoNotRepeatIt()
    {
        var scenario = new PublicationScenario();
        var statement = scenario.AddStatement('b');
        var reference = new PublishedRecordReference(PublishedRecordKind.Statement, statement);
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1", reference));

        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k2", reference)));

        Assert.Contains("already published at its current revision", error.Message, StringComparison.Ordinal);
        Assert.Single(scenario.Publications.Committed);
    }

    [Fact]
    public async Task ADocumentChangeWithTheSameIdDoesNotCountAsThePublishedStatement()
    {
        var scenario = new PublicationScenario();
        var statement = scenario.AddStatement('b');
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1",
            new PublishedRecordReference(PublishedRecordKind.Statement, statement)));

        var change = scenario.AddDraft('a');
        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k2", change));

        Assert.Equal(2, ReadManifest(scenario, summary.DirectoryName).Records.Length);
    }

    [Fact]
    public async Task CarriedStatementIsHardLinkedFromThePreviousReleaseWithoutRebuildingIt()
    {
        var scenario = new PublicationScenario();
        var statement = scenario.AddStatement('b');
        var first = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1",
            new PublishedRecordReference(PublishedRecordKind.Statement, statement)));
        scenario.Releases.Links.Clear();
        scenario.Statements.Publishable.Clear();

        var second = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            PublicationScenario.Request("k2", scenario.AddDraft('a')));

        Assert.Equal([(first.DirectoryName, PublicationProtocol.RecordDataPath(statement)),
            (first.DirectoryName, PublicationProtocol.RecordPagePath(statement))], scenario.Releases.Links);
        Assert.Contains(ReadManifest(scenario, second.DirectoryName).Records, entry => entry.Kind == PublishedRecordKind.Statement);
    }

    [Fact]
    public async Task ARepublishedStatementReplacesItsCarriedEntryInsteadOfDuplicatingIt()
    {
        var scenario = new PublicationScenario();
        var statement = scenario.AddStatement('b');
        var reference = new PublishedRecordReference(PublishedRecordKind.Statement, statement);
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1", reference));
        var review = scenario.Statements.Publishable[statement].Review;
        var revised = review.CurrentRevision! with { RevisionNumber = 2 };
        var decisions = review.Decisions.Add(PublicationScenario.Decision(statement, 2, ReviewDecisionKind.Approve) with { RevisionNumber = 2 });
        scenario.Statements.Publishable[statement] = scenario.Statements.Publishable[statement] with
        {
            Review = review with
            {
                CurrentRevision = revised,
                Revisions = review.Revisions.Add(revised),
                Decisions = decisions,
                ReviewStateVersion = 2
            }
        };

        var second = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k2", reference));

        var entry = Assert.Single(ReadManifest(scenario, second.DirectoryName).Records);
        Assert.Equal(2, entry.RevisionNumber);
        Assert.Empty(scenario.Releases.Links);
    }

    [Fact]
    public async Task StatementThatIsNotApprovedIsRefusedBecauseNoModelOrDraftMaySpeakForTheSite()
    {
        var scenario = new PublicationScenario();
        var statement = scenario.AddStatement('b', decisions: [PublicationScenario.Decision(new string('b', 64), 1, ReviewDecisionKind.RequestChanges)]);

        await Assert.ThrowsAsync<ArgumentException>(() => scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            PublicationScenario.Request("k", new PublishedRecordReference(PublishedRecordKind.Statement, statement))));

        Assert.Equal(0, scenario.Releases.BeginCount);
    }

    [Fact]
    public async Task StatementSpeakerWithoutAPublicNameFailsBeforeAnythingIsStaged()
    {
        var scenario = new PublicationScenario();
        var statement = scenario.AddStatement('b', speakerIds: ["unnamed"]);

        var error = await Assert.ThrowsAsync<ArgumentException>(() => scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            PublicationScenario.Request("k", new PublishedRecordReference(PublishedRecordKind.Statement, statement))));

        Assert.Contains("has no configured public name", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, scenario.Releases.BeginCount);
    }

    [Theory]
    [InlineData(PublishedRecordKind.Statement, 32)]
    [InlineData(PublishedRecordKind.DocumentChange, 64)]
    public async Task RecordIdMustMatchTheIdShapeOfItsKindSoAKindCannotBeMislabelled(PublishedRecordKind kind, int length)
    {
        var scenario = new PublicationScenario();

        await Assert.ThrowsAsync<ArgumentException>(() => scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            PublicationScenario.Request("k", new PublishedRecordReference(kind, new string('a', length)))));

        Assert.Equal(0, scenario.Statements.PublicationReadCount);
    }

    [Fact]
    public async Task ReleaseCannotNameTheSameRecordTwice()
    {
        var scenario = new PublicationScenario();
        var reference = new PublishedRecordReference(PublishedRecordKind.Statement, scenario.AddStatement('b'));

        await Assert.ThrowsAsync<ArgumentException>(() => scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            PublicationScenario.Request("k", reference, reference)));
    }

    [Fact]
    public async Task ReleaseWrittenAtSchemaVersionOneStillLoadsAsTheBaseOfTheNextRelease()
    {
        var scenario = new PublicationScenario();
        var old = new string('c', 32);
        scenario.SeedVersionOneRelease(old);

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            PublicationScenario.Request("k", scenario.AddDraft('a')));

        var manifest = ReadManifest(scenario, summary.DirectoryName);
        Assert.Equal(2, manifest.SchemaVersion);
        var carried = Assert.Single(manifest.Records, entry => entry.RecordId == old);
        Assert.Equal(PublishedRecordKind.DocumentChange, carried.Kind);
        Assert.Contains(("seeded", PublicationProtocol.RecordDataPath(old)), scenario.Releases.Links);
    }

    private static PublicationRelease ReadManifest(PublicationScenario scenario, string directory) =>
        JsonSerializer.Deserialize<PublicationRelease>(
            scenario.Releases.Directories[directory][PublicationProtocol.ManifestPath], PublicationProtocol.JsonOptions)!;
}
