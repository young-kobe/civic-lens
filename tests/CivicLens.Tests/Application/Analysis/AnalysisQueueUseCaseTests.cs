using CivicLens.Application.Analysis;
using CivicLens.Application.Statements;
using CivicLens.Core.Review;
using CivicLens.Tests.Application.Statements;

namespace CivicLens.Tests.Application.Analysis;

public sealed class AnalysisQueueUseCaseTests
{
    private static readonly ReviewActor Owner = new("auth0|owner", ReviewRole.Owner);
    private static readonly ReviewActor Reviewer = new("auth0|friend", ReviewRole.Reviewer);
    private static readonly string KnownRunId = new('a', 32);
    private readonly FakeStatementGateStore store = new("The senator announced a rent cap for tenants.");

    [Fact]
    public async Task OnlyTheOwnerReadsTheQueue()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => List().ExecuteAsync(Reviewer, null, 1));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Run().ExecuteAsync(Reviewer, KnownRunId));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Status().ExecuteAsync(Reviewer));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(501)]
    public async Task TheListRefusesALimitOutsideTheBound(int limit) =>
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => List().ExecuteAsync(Owner, null, limit));

    [Theory]
    [InlineData(1)]
    [InlineData(500)]
    public async Task TheListAcceptsALimitInsideTheBound(int limit) =>
        Assert.Empty(await List().ExecuteAsync(Owner, StatementGateView.Passed, limit));

    [Theory]
    [InlineData("")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("gggggggggggggggggggggggggggggggg")]
    public async Task ARunIdMustBe32LowercaseHexCharacters(string runId) =>
        await Assert.ThrowsAsync<ArgumentException>(() => Run().ExecuteAsync(Owner, runId));

    [Fact]
    public async Task AValidRunIdReachesTheStore()
    {
        store.ResumeAfterRejection("{}");

        Assert.Equal(KnownRunId, (await Run().ExecuteAsync(Owner, KnownRunId))!.Run.RunId);
        Assert.Null(await Run().ExecuteAsync(Owner, new string('b', 32)));
    }

    [Fact]
    public async Task TheStatusReportCarriesTheChargeAndTheCounts()
    {
        store.Charged = 42;

        var report = await Status().ExecuteAsync(Owner);

        Assert.Equal((null, 42L), (report.Status, report.ChargedToday));
        Assert.Empty(report.Counts);
    }

    private ListAnalyses<StatementGateRun, StatementGateView, StatementGateListItem> List() => new(store);

    private GetAnalysisRun<StatementGateRun, StatementGateView, StatementGateListItem> Run() => new(store);

    private GetAnalysisStatus<StatementGateRun, StatementGateView, StatementGateListItem> Status() =>
        new(store, new FakeAnalysisStatusStore());
}
