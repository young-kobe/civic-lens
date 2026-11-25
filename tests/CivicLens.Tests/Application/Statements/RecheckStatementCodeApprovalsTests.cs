using CivicLens.Application.Collection;
using CivicLens.Application.Review;
using CivicLens.Application.Statements;
using CivicLens.Core.Review;
using CivicLens.Tests.Fixtures;
using static CivicLens.Tests.Fixtures.StatementDraftFixtures;

namespace CivicLens.Tests.Application.Statements;

public sealed class RecheckStatementCodeApprovalsTests
{
    private static readonly ReviewActor Owner = new("auth0|owner", ReviewRole.Owner);

    [Fact]
    public async Task APreOfficeOfSingleSpeakerDraftIsApprovedWhenRecheckedWithCurrentOfficeOf()
    {
        var evidence = DraftEvidence(configuration: SpeakerRevision());
        var revision = StatementDraftResolver.Resolve(Output(), evidence, Officials(Warren), DateTimeOffset.UnixEpoch).Revision!;
        var store = new FakeRecheckStore([new(evidence.Article.CandidateId, evidence, revision)]);

        var result = await new RecheckStatementCodeApprovals(store).ExecuteAsync(
            Owner, CollectionConfigurationRevision.Create(OwnOffice(Warren)), StatementGateFixtures.Configuration(), 10);

        Assert.Equal(new StatementCodeApprovalRecheckResult(1, 1, 0, 0), result);
        Assert.True(Assert.Single(store.Applied).Check.Passed);
    }

    [Fact]
    public async Task AMultiSpeakerDraftWithOtherSpeakersRemainsHeldOnRecheck()
    {
        var evidence = DraftEvidence(configuration: SpeakerRevision());
        var revision = StatementDraftResolver.Resolve(Output(otherSpeakers: ["Jo Rivera"]), evidence, Officials(Warren), DateTimeOffset.UnixEpoch).Revision!;
        var store = new FakeRecheckStore([new(evidence.Article.CandidateId, evidence, revision)]);

        var result = await new RecheckStatementCodeApprovals(store).ExecuteAsync(
            Owner, CollectionConfigurationRevision.Create(OwnOffice(Warren)), StatementGateFixtures.Configuration(), 10);

        Assert.Equal(new StatementCodeApprovalRecheckResult(1, 0, 1, 0), result);
        Assert.Equal(StatementCodeApproval.SpeakerIsNotOfficeOwner, Assert.Single(store.Applied).Check.Failure);
    }

    [Fact]
    public async Task ADateOutsideTheSevenDayWindowRemainsHeldOnRecheckBecauseNoRuleIsRelaxed()
    {
        var text = Article.Replace("October 3, 2026", "September 25, 2026", StringComparison.Ordinal);
        var evidence = DraftEvidence(text, observed: new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero), configuration: SpeakerRevision());
        var revision = StatementDraftResolver.Resolve(Output(date: ("2026-09-25", "September 25, 2026")), evidence, Officials(Warren), DateTimeOffset.UnixEpoch).Revision!;
        var store = new FakeRecheckStore([new(evidence.Article.CandidateId, evidence, revision)]);

        var result = await new RecheckStatementCodeApprovals(store).ExecuteAsync(
            Owner, CollectionConfigurationRevision.Create(OwnOffice(Warren)), StatementGateFixtures.Configuration(), 10);

        Assert.Equal(new StatementCodeApprovalRecheckResult(1, 0, 1, 0), result);
        Assert.False(Assert.Single(store.Applied).Check.Passed);
    }

    [Fact]
    public async Task AConcurrentlyChangedDraftCountsAsSkipped()
    {
        var evidence = DraftEvidence(configuration: SpeakerRevision());
        var revision = StatementDraftResolver.Resolve(Output(), evidence, Officials(Warren), DateTimeOffset.UnixEpoch).Revision!;
        var store = new FakeRecheckStore([new(evidence.Article.CandidateId, evidence, revision)], forceSkip: true);

        var result = await new RecheckStatementCodeApprovals(store).ExecuteAsync(
            Owner, CollectionConfigurationRevision.Create(OwnOffice(Warren)), StatementGateFixtures.Configuration(), 10);

        Assert.Equal(new StatementCodeApprovalRecheckResult(1, 0, 0, 1), result);
    }

    [Theory]
    [InlineData("auth0|reviewer", ReviewRole.Reviewer)]
    [InlineData("civic-lens:analysis", ReviewRole.Reviewer)]
    public async Task NonOwnersCannotRunStatementCodeApprovalRecheck(string subject, ReviewRole role)
    {
        var store = new FakeRecheckStore([]);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            new RecheckStatementCodeApprovals(store).ExecuteAsync(
                new(subject, role), CollectionConfigurationRevision.Create(OwnOffice(Warren)), StatementGateFixtures.Configuration(), 10));
    }

    private sealed class FakeRecheckStore(IReadOnlyList<StatementCodeApprovalCandidate> candidates, bool forceSkip = false)
        : IStatementCodeApprovalRecheckStore
    {
        public List<(string CandidateId, int RevisionNumber, ApprovalCheck Check)> Applied { get; } = [];

        public Task<IReadOnlyList<StatementCodeApprovalCandidate>> ListAwaitingAsync(int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<StatementCodeApprovalCandidate>>([.. candidates.Take(limit)]);

        public Task<StatementCodeApprovalOutcome> ApplyAsync(string candidateId, int expectedRevisionNumber, ApprovalCheck check,
            CancellationToken cancellationToken)
        {
            Applied.Add((candidateId, expectedRevisionNumber, check));
            if (forceSkip) return Task.FromResult(StatementCodeApprovalOutcome.Skipped);
            return Task.FromResult(check.Passed ? StatementCodeApprovalOutcome.Approved : StatementCodeApprovalOutcome.StillHeld);
        }
    }
}
