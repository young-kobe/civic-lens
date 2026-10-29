using CivicLens.Application.Issues;
using CivicLens.Tests.Application.Relationships;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CivicLens.Application.Briefs;
using CivicLens.Application.Publication;
using CivicLens.Application.Review;
using CivicLens.Core.Briefs;
using CivicLens.Core.Review;
using CivicLens.Host.Publication;
using CivicLens.Infrastructure.Briefs;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Publication;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Application.Actions;
using CivicLens.Tests.Application.Publication;
using CivicLens.Tests.Application.Statements;
using CivicLens.Tests.Application.Votes;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using static CivicLens.Tests.Fixtures.BriefFixtures;
using static CivicLens.Tests.Fixtures.BriefWorldSeeder;

namespace CivicLens.Tests.Infrastructure.Briefs;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PostgresBriefPublicationTests(PostgresCollection postgres) : IAsyncLifetime
{
    private static readonly ReviewActor Reviewer = new("auth0|reviewer", ReviewRole.Reviewer);
    private static readonly PublishedRevisionBinding Statement = Binding(PublishedRecordKind.Statement, CitedStatement, 1);
    private static readonly PublishedRevisionBinding Action = Binding(PublishedRecordKind.Action, CitedAction, 1);
    private static readonly PublishedRevisionBinding DocumentChange = Binding(PublishedRecordKind.DocumentChange, CitedDocumentChange, 1);
    private static readonly PublishedRevisionBinding BriefBinding = Binding(PublishedRecordKind.Brief, BriefFixtures.RecordId, 1);

    private readonly string releaseRoot = Path.Combine(Path.GetTempPath(), "civic-brief-publication-" + Guid.NewGuid().ToString("N"));
    private string connectionString = null!;
    private PostgresBriefReviewStore reviews = null!;
    private PostgresBriefDraftStore drafts = null!;
    private PostgresPublicationStore publications = null!;
    private BriefWorldSeeder seeder = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
        var factory = new PooledDbContextFactory<CollectionAttemptDbContext>(
            new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options);
        reviews = new PostgresBriefReviewStore(factory);
        drafts = new PostgresBriefDraftStore(factory);
        publications = new PostgresPublicationStore(factory);
        seeder = new BriefWorldSeeder(connectionString);
        await seeder.ReleaseCitedRecordsAsync();
        await seeder.ReviewableBriefAsync(ReviewableRevision());
    }

    public async Task DisposeAsync()
    {
        await postgres.DropDatabaseAsync(connectionString);
        if (Directory.Exists(releaseRoot)) Directory.Delete(releaseRoot, recursive: true);
    }

    [Fact]
    public async Task TheReadyListHoldsAnApprovedBriefUntilACitedRecordLosesItsApprovalBecausePublishWouldRefuseIt()
    {
        await ApproveAsync();

        var ready = Assert.Single(await reviews.ListUnpublishedApprovedAsync(10, default));
        Assert.Equal((RecordId, null, false), (ready.Brief.RecordId, ready.PublishedRevisionNumber, ready.CitedRecordsChanged));

        await seeder.DecisionAsync(PublishedRecordKind.Statement, CitedStatement, 1, 2, ReviewDecisionKind.WithdrawApproval);

        Assert.True(Assert.Single(await reviews.ListUnpublishedApprovedAsync(10, default)).CitedRecordsChanged);
    }

    [Fact]
    public async Task TheReadyListLeavesOutABriefThatIsNotApprovedAndABriefThatTheActiveReleaseHoldsAtItsCurrentRevision()
    {
        Assert.Empty(await reviews.ListUnpublishedApprovedAsync(10, default));
        await ApproveAsync();
        await seeder.ReplaceActiveReleaseAsync(2, InWeek.AddHours(1), Statement, Action, DocumentChange, BriefBinding);

        Assert.Empty(await reviews.ListUnpublishedApprovedAsync(10, default));
    }

    [Fact]
    public async Task TheCitedRecordsAreCurrentOnlyWhenTheGivenReleaseHoldsTheirExactRevisionsAndTheirLatestDecisionIsAnApproval()
    {
        await ApproveAsync();

        Assert.False(await ChangedAsync(Statement, Action, DocumentChange));
        Assert.True(await ChangedAsync(Action, DocumentChange));
        Assert.True(await ChangedAsync(Binding(PublishedRecordKind.Statement, CitedStatement, 2), Action, DocumentChange));
        await seeder.DecisionAsync(PublishedRecordKind.Action, CitedAction, 1, 2, ReviewDecisionKind.RequestChanges);
        Assert.True(await ChangedAsync(Statement, Action, DocumentChange));
    }

    [Fact]
    public async Task OnlyTheChangedCitationIsFlaggedSoTheReviewerSeesWhichRecordBlocksPublication()
    {
        await ApproveAsync();

        var found = await reviews.GetForPublicationAsync([RecordId], [Statement, DocumentChange], default);

        var cited = found[RecordId].Cited;
        Assert.Equal([false, false, true], new[] { cited["R1"].Changed, cited["R2"].Changed, cited["R3"].Changed });
        Assert.Equal("Rent cap", cited["R1"].Headline);
    }

    [Fact]
    public async Task TheCommitRefusesABriefWhoseCitedRecordIsMissingFromTheNewReleaseSoThePageNeverLinksToNothing()
    {
        await ApproveAsync();

        await Assert.ThrowsAsync<PublicationConflictException>(() => CommitAsync(Statement, DocumentChange, BriefBinding));

        Assert.Single(await publications.ListAsync(10, default));
    }

    [Fact]
    public async Task TheCommitRefusesABriefWhoseCitedRecordTheSamePublishReplacesWithANewerRevision()
    {
        await ApproveAsync();
        await seeder.StatementAsync(CitedStatement, 2, "Rent cap amended", "warren");
        await seeder.DecisionAsync(PublishedRecordKind.Statement, CitedStatement, 2, 2, ReviewDecisionKind.Approve);

        await Assert.ThrowsAsync<PublicationConflictException>(() =>
            CommitAsync(Binding(PublishedRecordKind.Statement, CitedStatement, 2), Action, DocumentChange, BriefBinding));

        Assert.Single(await publications.ListAsync(10, default));
    }

    [Fact]
    public async Task TheCommitRefusesABriefWhenACitedRecordChangedBetweenTheBuildAndTheCommit()
    {
        await ApproveAsync();
        await seeder.DecisionAsync(PublishedRecordKind.Statement, CitedStatement, 1, 2, ReviewDecisionKind.WithdrawApproval);

        await Assert.ThrowsAsync<PublicationConflictException>(() => CommitAsync(Statement, Action, DocumentChange, BriefBinding));

        Assert.Single(await publications.ListAsync(10, default));
    }

    [Fact]
    public async Task TheCommitAcceptsABriefWhoseCitedRecordsAreCurrentInTheNewRelease()
    {
        await ApproveAsync();

        var summary = await CommitAsync(Statement, Action, DocumentChange, BriefBinding);

        Assert.Equal(2, summary.ReleaseNumber);
    }

    [Fact]
    public async Task TheCommitChecksTheBriefTableForABriefAndRefusesAChangedReviewState()
    {
        await ApproveAsync();
        await seeder.ExecuteAsync($"UPDATE brief_drafts SET review_state_version = review_state_version + 1 WHERE record_id = '{RecordId}'");

        await Assert.ThrowsAsync<PublicationConflictException>(() => CommitAsync(Statement, Action, DocumentChange, BriefBinding));
    }

    [Fact]
    public async Task APublishOfAnApprovedBriefWritesItsRecordAndItsPageAndRecordsItInTheRelease()
    {
        await ApproveAsync();
        var publish = await PublisherAsync();

        var summary = await publish.ExecuteAsync(PublicationScenario.Owner,
            PublicationScenario.Request("publish-1", new PublishedRecordReference(PublishedRecordKind.Brief, RecordId)));

        var files = new FileReleaseDirectory(releaseRoot);
        var record = JsonSerializer.Deserialize<PublishedBrief>(
            await files.ReadFileAsync(summary.DirectoryName, PublicationProtocol.RecordDataPath(RecordId), 1_000_000, default),
            PublicationProtocol.JsonOptions)!;
        Assert.Equal(["Rent cap", "A rule changed", "Bill 7"], record.Citations.Select(citation => citation.Headline));
        Assert.Contains("citation-R3", Encoding.UTF8.GetString(
            await files.ReadFileAsync(summary.DirectoryName, PublicationProtocol.RecordPagePath(RecordId), 1_000_000, default)),
            StringComparison.Ordinal);
        Assert.Equal(4, summary.RecordCount);
    }

    [Fact]
    public async Task APublishRefusesABriefWhoseCitedRevisionWasWithdrawnAfterTheBriefWasApproved()
    {
        await ApproveAsync();
        await seeder.DecisionAsync(PublishedRecordKind.DocumentChange, CitedDocumentChange, 1, 2, ReviewDecisionKind.WithdrawApproval);
        var publish = await PublisherAsync();

        var error = await Assert.ThrowsAsync<ArgumentException>(() => publish.ExecuteAsync(PublicationScenario.Owner,
            PublicationScenario.Request("publish-1", new PublishedRecordReference(PublishedRecordKind.Brief, RecordId))));

        Assert.Contains("cites a record", error.Message);
        Assert.Single(await publications.ListAsync(10, default));
    }

    [Fact]
    public async Task APublishRefusesABriefThatIsNotApproved()
    {
        var publish = await PublisherAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => publish.ExecuteAsync(PublicationScenario.Owner,
            PublicationScenario.Request("publish-1", new PublishedRecordReference(PublishedRecordKind.Brief, RecordId))));
    }

    [Fact]
    public async Task ABriefInTheReleaseIsNeverANewRecordOfItsWeekSoPublishingItDoesNotChangeTheBriefOfItsOwnWeek()
    {
        await seeder.ReplaceActiveReleaseAsync(2, InWeek.AddHours(1), Statement, Action, DocumentChange, BriefBinding);

        var evidence = (await drafts.ReadEvidenceAsync(RecordId, default))!;

        Assert.Equal(3, evidence.Records.Length);
        Assert.Equal(0, evidence.LeftOut);
        Assert.DoesNotContain(evidence.Records, record => record.RecordId == RecordId);
    }

    [Fact]
    public async Task ActivatingAReleaseThatAddsABriefRequeuesNoWeekBecauseAPublishedBriefIsNotACitableRecord()
    {
        await seeder.ReleaseAsync(2, InWeek.AddHours(1), active: false, Statement, Action, DocumentChange, BriefBinding);

        _ = await publications.ActivateAsync(2, (_, _) => Task.CompletedTask, default);

        Assert.Equal("Succeeded:3", await seeder.BriefQueueStatusAsync(Week));
    }

    [Fact]
    public async Task ActivatingAReleaseThatChangesACitableRecordStillRequeuesItsWeek()
    {
        await seeder.ReleaseAsync(2, InWeek.AddHours(1), active: false, Action, DocumentChange);

        _ = await publications.ActivateAsync(2, (_, _) => Task.CompletedTask, default);

        Assert.Equal("Pending:0", await seeder.BriefQueueStatusAsync(Week));
    }

    private async Task ApproveAsync() => _ = await new DecideBriefReview(reviews).ExecuteAsync(Reviewer,
        new(RecordId, 1, 0, ReviewDecisionKind.Approve, null, [], "approve-1"));

    private async Task<bool> ChangedAsync(params PublishedRevisionBinding[] release) =>
        (await reviews.GetForPublicationAsync([RecordId], [.. release], default))[RecordId].CitedRecordsChanged;

    private Task<PublicationReleaseSummary> CommitAsync(params PublishedRevisionBinding[] records) =>
        publications.CommitAsync("auth0|owner", new(2, 1, $"000002-{Guid.NewGuid():N}", [.. records],
            [new(PublishedRecordKind.Brief, RecordId, 1, 1)]), "commit-1",
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("payload"))), default);

    private async Task<PublishRelease> PublisherAsync()
    {
        var active = (await publications.GetStateAsync(default)).Active!;
        var folder = Path.Combine(releaseRoot, "releases", active.DirectoryName);
        Directory.CreateDirectory(folder);
        var entries = new[] { Statement, Action, DocumentChange }.Select(binding => new PublishedRecordEntry
        {
            IssueIds = [],
            Officials = [],
            PolicyAreaIds = [],
            Kind = binding.Kind,
            RecordId = binding.RecordId,
            RevisionNumber = binding.RevisionNumber,
            Headline = $"Headline of {binding.RecordId[..4]}",
            BillTitle = binding.Kind == PublishedRecordKind.Action ? $"Title of {binding.RecordId[..4]}" : null,
            FirstPublishedAtUtc = InWeek
        }).ToArray();
        var manifest = new PublicationRelease
        {
            SchemaVersion = PublicationProtocol.SchemaVersion,
            ReleaseNumber = 1,
            PublishedAtUtc = InWeek,
            Withdrawals = [],
            Records = entries
        };
        File.WriteAllBytes(Path.Combine(folder, PublicationProtocol.ManifestPath), JsonSerializer.SerializeToUtf8Bytes(manifest, PublicationProtocol.JsonOptions));
        File.WriteAllBytes(Path.Combine(folder, PublicationProtocol.OfficialsPath),
            JsonSerializer.SerializeToUtf8Bytes(new PublishedOfficials { Officials = [] }, PublicationProtocol.JsonOptions));
        Directory.CreateDirectory(Path.Combine(folder, "records"));
        foreach (var entry in entries)
        {
            File.WriteAllBytes(Path.Combine(folder, PublicationProtocol.RecordDataPath(entry.RecordId)), []);
            File.WriteAllBytes(Path.Combine(folder, PublicationProtocol.RecordPagePath(entry.RecordId)), []);
        }
        return new PublishRelease(publications, new FileReleaseDirectory(releaseRoot), new ReleaseRenderer(null, null),
            new PublishedRecordLoaders(new FakeReviewStore(), new FakeStatementReviewStore(), new FakeActionReviewStore(), new FakeVoteReviewStore(),
                new FakeRelationshipReviewStore(), reviews),
            new PublicationCatalog(new Dictionary<string, string>(), IssueConfiguration.None), FixedRosters.NoOfficials(), PhotoCaptures.None(), new FakeClock(InWeek.AddDays(10)));
    }
}
