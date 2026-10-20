using CivicLens.Application.Relationships;
using CivicLens.Core.Analysis;
using CivicLens.Core.Relationships;
using CivicLens.Core.Review;
using CivicLens.Tests.Application.Analysis;
using CivicLens.Tests.Fixtures;
using static CivicLens.Tests.Fixtures.RelationshipFixtures;
using State = CivicLens.Application.Analysis.AnalysisState;

namespace CivicLens.Tests.Application.Relationships;

public sealed class RelationshipDraftStepTests
{
    [Fact]
    public async Task TheFirstDraftCallsTheModelOnceAndWritesRevisionOneThatCitesBothApprovedRevisions()
    {
        var (store, model) = await RunAsync(Evidence(ActionPair()), new FakeRelationshipDraftModel().Draft(Output()));

        var revision = Assert.Single(store.Revisions);
        Assert.Equal((1, ReviewAuthor.AnalysisSubject, RelationshipType.DescribesOwnBill), (revision.RevisionNumber, revision.AuthorSubject, revision.Type));
        Assert.Equal((1, 1), (revision.Statement.RevisionNumber, revision.Target.RevisionNumber));
        Assert.Single(model.Requests);
        Assert.Equal((State.Succeeded, null), (store.Entries[0].Status, store.Entries[0].ErrorCode));
    }

    [Fact]
    public async Task ThePromptCarriesTheMatchedWordsTheRoleAndTheFactsOfTheTargetAndNoOtherConfiguredOfficial()
    {
        var (_, model) = await RunAsync(Evidence(ActionPair()), new FakeRelationshipDraftModel().Draft(Output()));

        var prompt = Assert.Single(model.Requests).UserPrompt;
        Assert.Contains("\"words\": \"H.R. 1234\"", prompt, StringComparison.Ordinal);
        Assert.Contains("\"role\": \"sponsor\"", prompt, StringComparison.Ordinal);
        Assert.Contains("Title: Lower Rent Act of 2025", prompt, StringComparison.Ordinal);
        Assert.Contains("Elizabeth Warren", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Ed Markey", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ANoneAnswerSettlesTheRowWithNoRevisionAndKeepsTheRunWithItsReason()
    {
        var (store, _) = await RunAsync(Evidence(ActionPair()), new FakeRelationshipDraftModel().Draft(None()));

        Assert.Empty(store.Revisions);
        Assert.Equal((State.Succeeded, RelationshipDraftErrorCodes.NoRelationship), (store.Entries[0].Status, store.Entries[0].ErrorCode));
        var run = Assert.Single(store.Runs);
        Assert.Equal((AnalysisRunOutcome.Drafted, RelationshipDraftErrorCodes.NoRelationship), (run.Run.Outcome, run.Reason));
    }

    [Fact]
    public async Task AnUnchangedFingerprintSettlesWithABypassedRunAndNoModelCall()
    {
        var pair = ActionPair();
        var evidence = Evidence(pair, settledFingerprint: RelationshipFingerprint.Compute(pair));

        var (store, model) = await RunAsync(evidence, new FakeRelationshipDraftModel());

        Assert.Empty(model.Requests);
        Assert.Empty(store.Revisions);
        Assert.Equal((State.Succeeded, RelationshipDraftErrorCodes.Unchanged), (store.Entries[0].Status, store.Entries[0].ErrorCode));
        Assert.Equal((AnalysisRunOutcome.Bypassed, 0L), (Assert.Single(store.Runs).Run.Outcome, store.Runs[0].Run.ChargedTokens));
    }

    [Fact]
    public async Task ANewTargetRevisionWithTheSameFingerprintCopiesTheRevisionToTheNewCitationWithoutAModelCall()
    {
        var pair = ActionPair();
        var current = RelationshipDraftResolver.Resolve(Output(), Context(pair), Created).Accepted!;
        var evidence = Evidence(pair, current, RelationshipFingerprint.Compute(pair)) with { Action = Action(revision: 2) };

        var (store, model) = await RunAsync(evidence, new FakeRelationshipDraftModel());

        Assert.Empty(model.Requests);
        var revision = Assert.Single(store.Revisions);
        Assert.Equal((2, 2, 1, ReviewAuthor.AnalysisSubject), (revision.RevisionNumber, revision.Target.RevisionNumber, revision.Statement.RevisionNumber, revision.AuthorSubject));
        Assert.Equal((current.Type, current.Reason, current.StatementQuote, current.TargetFactId), (revision.Type, revision.Reason, revision.StatementQuote, revision.TargetFactId));
        Assert.Equal((State.Succeeded, null), (store.Entries[0].Status, store.Entries[0].ErrorCode));
        Assert.Equal((AnalysisRunOutcome.Bypassed, RelationshipDraftErrorCodes.TargetRecited), (Assert.Single(store.Runs).Run.Outcome, store.Runs[0].Reason));
    }

    [Fact]
    public async Task ACurrentRevisionThatCitesTheCurrentTargetIsNotCopied()
    {
        var pair = ActionPair();
        var current = RelationshipDraftResolver.Resolve(Output(), Context(pair), Created).Accepted!;

        var (store, model) = await RunAsync(Evidence(pair, current, RelationshipFingerprint.Compute(pair)), new FakeRelationshipDraftModel());

        Assert.Empty(model.Requests);
        Assert.Empty(store.Revisions);
    }

    [Fact]
    public async Task ARevisionBehindANoneAnswerIsNeverCopiedToTheNewTargetCitation()
    {
        var pair = ActionPair();
        var current = RelationshipDraftResolver.Resolve(Output(), Context(pair), Created).Accepted!;
        var evidence = Evidence(pair, current, RelationshipFingerprint.Compute(pair)) with { Action = Action(revision: 2), SettledAsNone = true };

        var (store, model) = await RunAsync(evidence, new FakeRelationshipDraftModel());

        Assert.Empty(model.Requests);
        Assert.Empty(store.Revisions);
        Assert.Equal(RelationshipDraftErrorCodes.Unchanged, store.Entries[0].ErrorCode);
    }

    [Fact]
    public async Task ARevisionThatCitesAnOlderStatementRevisionDraftsAgainInsteadOfCopyingTheOldStatementCitation()
    {
        var pair = ActionPair();
        var older = Revision(pair);
        var newer = ActionPair(Statement(revision: 2));
        var evidence = Evidence(newer, older, RelationshipFingerprint.Compute(newer)) with { Action = Action(revision: 2) };

        var (store, model) = await RunAsync(evidence, new FakeRelationshipDraftModel().Draft(Output()));

        Assert.Single(model.Requests);
        var revision = Assert.Single(store.Revisions);
        Assert.Equal((2, 2), (revision.Statement.RevisionNumber, revision.Target.RevisionNumber));
    }

    [Fact]
    public async Task ARevisionThatCitesAnotherMatchDraftsAgainInsteadOfCopyingTheOldMatch()
    {
        var pair = ActionPair();
        var otherMatch = Revision(pair) with { Match = pair.Match with { Span = pair.Match.Span with { Start = pair.Match.Span.Start + 1 } } };
        var evidence = Evidence(pair, otherMatch, RelationshipFingerprint.Compute(pair)) with { Action = Action(revision: 2) };

        var (store, model) = await RunAsync(evidence, new FakeRelationshipDraftModel().Draft(Output()));

        Assert.Single(model.Requests);
        Assert.Equal(pair.Match, Assert.Single(store.Revisions).Match);
    }

    [Fact]
    public async Task APersonRevisionThatCitesAnOlderTargetRevisionDraftsAgainSoItsCitationDoesNotStayStale()
    {
        var pair = ActionPair();
        var person = Revision(pair) with { RevisionNumber = 2, AuthorSubject = "auth0|reviewer" };
        var evidence = Evidence(pair, person, RelationshipFingerprint.Compute(pair)) with { Action = Action(revision: 2) };

        var (store, model) = await RunAsync(evidence, new FakeRelationshipDraftModel().Draft(Output()));

        Assert.Single(model.Requests);
        Assert.Equal(2, Assert.Single(store.Revisions).Target.RevisionNumber);
    }

    [Fact]
    public async Task ADifferentFingerprintDraftsAgainAndAppendsTheNextRevision()
    {
        var pair = ActionPair();
        var current = RelationshipDraftResolver.Resolve(Output(), Context(pair), Created).Accepted!;
        var newer = ActionPair(Statement(revision: 2));

        var (store, model) = await RunAsync(Evidence(newer, current, RelationshipFingerprint.Compute(pair)), new FakeRelationshipDraftModel().Draft(Output()));

        Assert.Single(model.Requests);
        var revision = Assert.Single(store.Revisions);
        Assert.Equal((2, 2), (revision.RevisionNumber, revision.Statement.RevisionNumber));
    }

    [Fact]
    public async Task APairThatNoLongerMatchesSettlesWithoutACallSoAnEditedStatementIsNotAskedAboutAgain()
    {
        var pair = ActionPair();
        var evidence = Evidence(pair) with { Statement = Statement(passages: [FillerSentence]), Action = Action(billNumber: 9, title: "Unrelated Housing Plan") };

        var (store, model) = await RunAsync(evidence, new FakeRelationshipDraftModel());

        Assert.Empty(model.Requests);
        Assert.Equal((State.Succeeded, RelationshipDraftErrorCodes.NoLongerMatches), (store.Entries[0].Status, store.Entries[0].ErrorCode));
    }

    [Fact]
    public async Task ASideThatIsNoLongerApprovedBlocksTheRowWithoutACall()
    {
        var evidence = Evidence(ActionPair()) with { Action = null };

        var (store, model) = await RunAsync(evidence, new FakeRelationshipDraftModel());

        Assert.Empty(model.Requests);
        Assert.Equal((State.Blocked, RelationshipDraftErrorCodes.SideNotApproved), (store.Entries[0].Status, store.Entries[0].ErrorCode));
    }

    [Fact]
    public async Task AMissingQueueRowBlocksWithEvidenceUnavailable()
    {
        var (store, _) = await RunAsync(null, new FakeRelationshipDraftModel());

        Assert.Equal((State.Blocked, RelationshipDraftErrorCodes.EvidenceUnavailable), (store.Entries[0].Status, store.Entries[0].ErrorCode));
    }

    [Fact]
    public async Task APromptOverTheByteCapBlocksWithoutACall()
    {
        var settings = new RelationshipDraftSettings(RelationshipDraftPrompt.MinimumReservation);

        var (store, model) = await RunAsync(Evidence(ActionPair()), new FakeRelationshipDraftModel(), settings);

        Assert.Empty(model.Requests);
        Assert.Equal((State.Blocked, RelationshipDraftErrorCodes.OverByteCap), (store.Entries[0].Status, store.Entries[0].ErrorCode));
    }

    [Fact]
    public async Task ARejectedQuoteGetsOneRetryThatCarriesTheReasonAndAWrongQuoteTwiceFailsTheRow()
    {
        var recovers = new FakeRelationshipDraftModel().Draft(Output(quote: "to cap rent.")).Draft(Output());
        var (accepted, _) = await RunAsync(Evidence(ActionPair()), recovers);
        var fails = new FakeRelationshipDraftModel().Draft(Output(quote: "to cap rent.")).Draft(Output(quote: "to cap rent."));
        var (failed, _) = await RunAsync(Evidence(ActionPair()), fails);

        Assert.Contains("must include the matched words", recovers.Requests[1].UserPrompt, StringComparison.Ordinal);
        Assert.Single(accepted.Revisions);
        Assert.Empty(failed.Revisions);
        Assert.Equal(State.Failed, failed.Entries[0].Status);
    }

    private static RelationshipDraftEvidence Evidence(RelationshipPair pair, RelationshipRevision? current = null, string? settledFingerprint = null) =>
        RelationshipFixtures.Evidence(pair, current, settledFingerprint);

    private static async Task<(FakeRelationshipDraftStore Store, FakeRelationshipDraftModel Model)> RunAsync(RelationshipDraftEvidence? evidence,
        FakeRelationshipDraftModel model, RelationshipDraftSettings? settings = null)
    {
        var store = new FakeRelationshipDraftStore(evidence);
        await AnalysisWorkers.RelationshipDrafting(store, new FakeAnalysisStatusStore(), model,
            settings ?? new RelationshipDraftSettings(10_000_000, concurrency: 1), Officials, TimeProvider.System).ExecuteAsync(once: true);
        return (store, model);
    }
}
