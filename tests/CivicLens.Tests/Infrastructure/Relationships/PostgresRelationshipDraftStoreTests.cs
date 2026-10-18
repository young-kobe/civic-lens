using CivicLens.Application.Relationships;
using CivicLens.Core.Relationships;
using CivicLens.Core.Review;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Relationships;
using CivicLens.Tests.Application.Relationships;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using static CivicLens.Tests.Fixtures.RelationshipFixtures;
using State = CivicLens.Application.Analysis.AnalysisState;

namespace CivicLens.Tests.Infrastructure.Relationships;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PostgresRelationshipDraftStoreTests(PostgresCollection postgres) : IAsyncLifetime
{
    private string connectionString = null!;
    private PooledDbContextFactory<CollectionAttemptDbContext> factory = null!;
    private PostgresRelationshipDraftStore store = null!;
    private RelationshipWorldSeeder seeder = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
        factory = new PooledDbContextFactory<CollectionAttemptDbContext>(
            new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options);
        store = new PostgresRelationshipDraftStore(factory);
        seeder = new RelationshipWorldSeeder(connectionString);
    }

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

    [Fact]
    public async Task RefreshQueuesOnePendingRowForTheApprovedPairWithTheHashKeyAndTheIdentityOfBothSides()
    {
        await SeedApprovedPairAsync();

        await store.RefreshPairsAsync(default);

        Assert.Equal([[RelationshipPair.IdOf(StatementId, RelationshipRecordKind.Action, ActionId), StatementId, "Action", ActionId, "Pending"]],
            await seeder.ReadAsync("SELECT pair_id, statement_candidate_id, target_kind, target_record_id, status FROM relationship_analysis"));
    }

    [Fact]
    public async Task ASideThatIsNotApprovedMakesNoPairBecauseOnlyApprovedRevisionsMayBeCited()
    {
        await seeder.StatementAsync(Statement(), "AwaitingReview");
        await seeder.ActionAsync(Action());
        await store.RefreshPairsAsync(default);
        Assert.Empty(await seeder.ReadAsync("SELECT 1 FROM relationship_analysis"));

        await seeder.StatementAsync(Statement());
        await seeder.ActionAsync(Action(), "ChangesRequested");
        await store.RefreshPairsAsync(default);
        Assert.Empty(await seeder.ReadAsync("SELECT 1 FROM relationship_analysis"));
    }

    [Fact]
    public async Task APairComesFromTheCosponsorshipOfTheSpeakerAndNotFromAnActionOfSomeoneElse()
    {
        await seeder.StatementAsync(Statement());
        await seeder.ActionAsync(Action(sponsor: "markey"));
        await store.RefreshPairsAsync(default);
        Assert.Empty(await seeder.ReadAsync("SELECT 1 FROM relationship_analysis"));

        await seeder.ActionAsync(Action(revision: 2, role: CivicLens.Core.Actions.ActionSponsorshipRole.Cosponsor, original: false,
            withdrawnOn: new DateOnly(2025, 2, 1)));
        await store.RefreshPairsAsync(default);

        Assert.Single(await seeder.ReadAsync("SELECT 1 FROM relationship_analysis"));
    }

    [Fact]
    public async Task AVoteOnAMentionedBillQueuesAVotePairAndAVoteOnAnotherBillDoesNot()
    {
        await seeder.StatementAsync(Statement());
        await seeder.VoteAsync(Vote());
        await seeder.VoteAsync(Vote(billNumber: 77) with { RecordId = new string('7', 64), CandidateId = new string('8', 64), RollCallNumber = 101 });

        await store.RefreshPairsAsync(default);

        Assert.Equal([["Vote", VoteId]], await seeder.ReadAsync("SELECT target_kind, target_record_id FROM relationship_analysis"));
    }

    [Fact]
    public async Task RefreshTwiceKeepsOneRowAndNeverResetsARowThatAlreadySettledWithTheSameInput()
    {
        var pair = await SeedApprovedPairAsync();
        await store.RefreshPairsAsync(default);
        await seeder.SettledRowAsync(pair, "Succeeded", null, RelationshipFingerprint.Compute(pair));

        await store.RefreshPairsAsync(default);

        Assert.Equal([["Succeeded"]], await seeder.ReadAsync("SELECT status FROM relationship_analysis"));
    }

    [Fact]
    public async Task ANewApprovedRevisionOfASideMovesAnUnapprovedPairBackToPendingThroughTheQueueOwner()
    {
        var pair = await SeedApprovedPairAsync();
        await store.RefreshPairsAsync(default);
        await seeder.SettledRowAsync(pair, "Succeeded", null, RelationshipFingerprint.Compute(pair));

        await seeder.StatementAsync(Statement(revision: 2));
        await store.RefreshPairsAsync(default);

        Assert.Equal([["Pending"]], await seeder.ReadAsync("SELECT status FROM relationship_analysis"));
    }

    [Fact]
    public async Task AnApprovedRelationshipIsNeverRequeuedEvenWhenACitedRevisionChanged()
    {
        var pair = await SeedApprovedPairAsync();
        await store.RefreshPairsAsync(default);
        await seeder.SettledRowAsync(pair, "Succeeded", null, RelationshipFingerprint.Compute(pair), "Approved");

        await seeder.ActionAsync(Action(revision: 2));
        await store.RefreshPairsAsync(default);

        Assert.Equal([["Succeeded"]], await seeder.ReadAsync("SELECT status FROM relationship_analysis"));
    }

    [Fact]
    public async Task ANoneAnswerIsNotAskedAgainWhileTheInputIsTheSameAndIsAskedAgainWhenACitedRevisionChanges()
    {
        var pair = await SeedApprovedPairAsync();
        await store.RefreshPairsAsync(default);
        await seeder.ExecuteAsync("""
            SET session_replication_role = replica;
            UPDATE relationship_analysis SET status = 'Succeeded', error_code = 'noRelationship';
            INSERT INTO relationship_analysis_runs (run_id, pair_id, task, task_version, model, prompt_version, schema_version, input_hash, outcome,
                reason, input_tokens, output_tokens, cache_read_tokens, cache_write_tokens, charged_tokens, context_json, started_at_utc_ticks,
                finished_at_utc_ticks)
            VALUES (substr(@id, 1, 32), @id, 'relationship-draft', 'v', 'm', 'p', 's', @id, 'Drafted', 'noRelationship', 0, 0, 0, 0, 0, @context, 1, 1)
            """, ("id", pair.PairId), ("context", System.Text.Json.JsonSerializer.Serialize(new { fingerprint = RelationshipFingerprint.Compute(pair) })));

        await store.RefreshPairsAsync(default);
        Assert.Equal([["Succeeded"]], await seeder.ReadAsync("SELECT status FROM relationship_analysis"));

        await seeder.ActionAsync(Action(revision: 2));
        await store.RefreshPairsAsync(default);
        Assert.Equal([["Pending"]], await seeder.ReadAsync("SELECT status FROM relationship_analysis"));
    }

    [Fact]
    public async Task APairThatIsNotFoundAnymoreIsFlaggedAndNotDeletedAndTheFlagClearsWhenItIsFoundAgain()
    {
        var pair = await SeedApprovedPairAsync();
        await store.RefreshPairsAsync(default);
        await seeder.SettledRowAsync(pair, "Succeeded", null, RelationshipFingerprint.Compute(pair));
        const string flag = "SELECT no_longer_matches FROM relationship_drafts";

        await seeder.StatementAsync(Statement(passages: [FillerSentence]), "Approved");
        await store.RefreshPairsAsync(default);
        Assert.Equal([["True"]], await seeder.ReadAsync(flag));
        Assert.Single(await seeder.ReadAsync("SELECT 1 FROM relationship_analysis"));

        await seeder.StatementAsync(Statement());
        await store.RefreshPairsAsync(default);
        Assert.Equal([["False"]], await seeder.ReadAsync(flag));
    }

    [Fact]
    public async Task ReadingEvidenceReturnsTheApprovedSidesAndTheFingerprintOfTheLastSettledInput()
    {
        var pair = await SeedApprovedPairAsync();
        await store.RefreshPairsAsync(default);
        await seeder.SettledRowAsync(pair, "Succeeded", null, "abc");

        var evidence = (await store.ReadEvidenceAsync(pair.PairId, default))!;

        Assert.Equal((RelationshipRecordKind.Action, StatementId, ActionId, "abc"),
            (evidence.TargetKind, evidence.Statement!.RecordId, evidence.Action!.RecordId, evidence.SettledFingerprint));
        Assert.True(evidence.SidesAreApproved);
        Assert.Equal(pair.PairId, evidence.FindPair()!.PairId);
    }

    [Fact]
    public async Task EvidenceOfAnUnknownPairIsNullAndASideThatLostItsApprovalIsMissing()
    {
        var pair = await SeedApprovedPairAsync();
        await store.RefreshPairsAsync(default);
        Assert.Null(await store.ReadEvidenceAsync(new string('0', 64), default));

        await seeder.SetStatusAsync("action_drafts", "record_id", ActionId, "ApprovalWithdrawn");

        var evidence = (await store.ReadEvidenceAsync(pair.PairId, default))!;
        Assert.False(evidence.SidesAreApproved);
    }

    [Fact]
    public async Task OneWorkerPassDraftsTheApprovedPairSavesRevisionOneUnderTheReviewLockAndLeavesTheDraftAwaitingReview()
    {
        var pair = await SeedApprovedPairAsync();
        var model = new FakeRelationshipDraftModel().Draft(Output());

        await RunWorkerAsync(model);

        Assert.Single(model.Requests);
        Assert.Equal([[pair.PairId, "1", "AwaitingReview", "False"]],
            await seeder.ReadAsync("SELECT record_id, current_revision_number, review_status, no_longer_matches FROM relationship_drafts"));
        Assert.Equal([["Analysis", "Succeeded"]], await seeder.ReadAsync(
            "SELECT r.author_kind, a.status FROM relationship_revisions r JOIN relationship_analysis a ON a.pair_id = r.record_id"));
    }

    [Fact]
    public async Task AChangedStatementRevisionDraftsRevisionTwoAndALaterPassWithTheSameInputsMakesNoCall()
    {
        await SeedApprovedPairAsync();
        await RunWorkerAsync(new FakeRelationshipDraftModel().Draft(Output()));
        await seeder.StatementAsync(Statement(revision: 2));
        var second = new FakeRelationshipDraftModel().Draft(Output());

        await RunWorkerAsync(second);
        var third = new FakeRelationshipDraftModel();
        await RunWorkerAsync(third);

        Assert.Single(second.Requests);
        Assert.Empty(third.Requests);
        Assert.Equal([["2"]], await seeder.ReadAsync("SELECT current_revision_number FROM relationship_drafts"));
    }

    [Fact]
    public async Task AnApprovalThatLandsWhileTheModelRunsLeavesTheApprovedRevisionAndSettlesTheRunAsUnchanged()
    {
        var pair = await SeedApprovedPairAsync();
        await RunWorkerAsync(new FakeRelationshipDraftModel().Draft(Output()));
        var settled = (await store.ReadEvidenceAsync(pair.PairId, default))!.SettledFingerprint;
        await seeder.StatementAsync(Statement(revision: 2));

        await RunWorkerAsync(new ApprovingModel(seeder, pair.PairId, Output()));

        Assert.Equal([["1"]], await seeder.ReadAsync("SELECT count(*) FROM relationship_revisions"));
        Assert.Equal([["Approved"]], await seeder.ReadAsync("SELECT review_status FROM relationship_drafts"));
        Assert.Equal([["Succeeded", "unchanged"]], await seeder.ReadAsync("SELECT status, error_code FROM relationship_analysis"));
        Assert.Equal([["1"]], await seeder.ReadAsync("SELECT count(*) FROM relationship_analysis_runs WHERE outcome = 'DraftDiscarded' AND reason IS NULL"));
        Assert.Equal(settled, (await store.ReadEvidenceAsync(pair.PairId, default))!.SettledFingerprint);
    }

    [Fact]
    public async Task AModelRevisionThatCitesTheSameRecordsAsAPersonRevisionIsNotAppendedAndTheEntrySettlesAsUnchanged()
    {
        var pair = await SeedPersonRevisionAsync();
        var settled = (await store.ReadEvidenceAsync(pair.PairId, default))!.SettledFingerprint;

        await RunWorkerAsync(new FakeRelationshipDraftModel().Draft(Output()));

        Assert.Equal([["2"]], await seeder.ReadAsync("SELECT count(*) FROM relationship_revisions"));
        Assert.Equal([["Succeeded", "unchanged"]], await seeder.ReadAsync("SELECT status, error_code FROM relationship_analysis"));
        Assert.Equal([["1"]], await seeder.ReadAsync("SELECT count(*) FROM relationship_analysis_runs WHERE outcome = 'DraftDiscarded' AND reason IS NULL"));
        Assert.Equal(settled, (await store.ReadEvidenceAsync(pair.PairId, default))!.SettledFingerprint);
    }

    [Fact]
    public async Task AModelRevisionThatCitesANewerRevisionOfASideLandsOnAPersonRevision()
    {
        await SeedPersonRevisionAsync();
        await seeder.StatementAsync(Statement(revision: 2));

        await RunWorkerAsync(new FakeRelationshipDraftModel().Draft(Output()));

        Assert.Equal([["3"]], await seeder.ReadAsync("SELECT count(*) FROM relationship_revisions"));
        Assert.Equal([["Succeeded", "null"]], await seeder.ReadAsync("SELECT status, error_code FROM relationship_analysis"));
    }

    [Fact]
    public async Task RefreshQueuesAWithdrawnPairAgainWhenACitedRevisionChangedWhileItWasApproved()
    {
        var pair = await SeedApprovedPairAsync();
        await store.RefreshPairsAsync(default);
        await seeder.SettledRowAsync(pair, "Succeeded", null, RelationshipFingerprint.Compute(pair), "Approved");
        await seeder.StatementAsync(Statement(revision: 2));
        await store.RefreshPairsAsync(default);
        Assert.Equal([["Succeeded"]], await seeder.ReadAsync("SELECT status FROM relationship_analysis"));

        await seeder.ExecuteAsync("UPDATE relationship_drafts SET review_status = 'ApprovalWithdrawn'");
        await store.RefreshPairsAsync(default);

        Assert.Equal([["Pending"]], await seeder.ReadAsync("SELECT status FROM relationship_analysis"));
    }

    private async Task<RelationshipPair> SeedPersonRevisionAsync()
    {
        var pair = await SeedApprovedPairAsync();
        await RunWorkerAsync(new FakeRelationshipDraftModel().Draft(Output()));
        await seeder.RevisionAsync(Revision(pair) with { RevisionNumber = 2, AuthorSubject = "auth0|reviewer" });
        await seeder.ExecuteAsync("UPDATE relationship_analysis_runs SET context_json = '{\"fingerprint\":\"old\"}'");
        return pair;
    }

    [Fact]
    public async Task ANoneAnswerSavesNoRelationshipAndTheNextPassDoesNotAskAgain()
    {
        await SeedApprovedPairAsync();
        var first = new FakeRelationshipDraftModel().Draft(None());
        var second = new FakeRelationshipDraftModel();

        await RunWorkerAsync(first);
        await RunWorkerAsync(second);

        Assert.Single(first.Requests);
        Assert.Empty(second.Requests);
        Assert.Empty(await seeder.ReadAsync("SELECT 1 FROM relationship_drafts"));
        Assert.Equal([["Succeeded", "noRelationship"]], await seeder.ReadAsync("SELECT status, error_code FROM relationship_analysis"));
    }

    [Fact]
    public async Task ListAndCountReportTheViewOfEachQueueRow()
    {
        var pair = await SeedApprovedPairAsync();
        await store.RefreshPairsAsync(default);

        var pending = Assert.Single(await store.ListAsync(RelationshipDraftView.Pending, 10, default));
        Assert.Equal((pair.PairId, RelationshipRecordKind.Action, State.Pending), (pending.PairId, pending.TargetKind, pending.Status));
        Assert.Empty(await store.ListAsync(RelationshipDraftView.Drafted, 10, default));
        Assert.Equal(1, (await store.CountAsync(default))[RelationshipDraftView.Pending]);
    }

    [Fact]
    public async Task TheCountOfStatementsWithoutACongressComesFromTheApprovedStatementsThatHaveABareNumberAndNoUsableDate()
    {
        await seeder.StatementAsync(Statement(dated: false));

        Assert.Equal(1, await store.CountStatementsWithoutCongressAsync(default));
    }

    private async Task<RelationshipPair> SeedApprovedPairAsync()
    {
        await seeder.StatementAsync(Statement());
        await seeder.ActionAsync(Action());
        return ActionPair();
    }

    private async Task RunWorkerAsync(IRelationshipDraftModel model)
    {
        _ = await store.RefreshPairsAsync(default);
        await AnalysisWorkers.RelationshipDrafting(store, new PostgresAnalysisStatusStore(factory, RelationshipDraftTask.Task), model,
            new RelationshipDraftSettings(10_000_000, concurrency: 1), Officials, TimeProvider.System).ExecuteAsync(once: true);
    }

    private sealed class ApprovingModel(RelationshipWorldSeeder seeder, string recordId, string reply) : IRelationshipDraftModel
    {
        public async Task<CivicLens.Application.Analysis.AnalysisModelResponse> DraftAsync(CivicLens.Application.Analysis.AnalysisModelRequest request,
            CancellationToken cancellationToken)
        {
            await seeder.ExecuteAsync("UPDATE relationship_drafts SET review_status = 'Approved' WHERE record_id = @id", ("id", recordId));
            return new("end_turn", reply, new(900, 100, 0, 0), "req_fixture");
        }
    }
}
