using CivicLens.Application.Statements;
using CivicLens.Core.Review;
using CivicLens.Core.Statements;

namespace CivicLens.Tests.Application.Statements;

public sealed class GateLabelOwnerOnlyTests
{
    private static readonly ReviewActor Reviewer = new("auth0|friend", ReviewRole.Reviewer);
    private static readonly IGateLabelStore Store = new UnusedStore();

    public static TheoryData<string, Func<ReviewActor, Task>> Reads => new()
    {
        { nameof(ListGateLabelQueue), actor => new ListGateLabelQueue(Store).ExecuteAsync(actor) },
        { nameof(GetGateLabelTask), actor => new GetGateLabelTask(Store).ExecuteAsync(actor, new string('a', 64)) },
        { nameof(ExportGateLabels), actor => new ExportGateLabels(Store).ExecuteAsync(actor) }
    };

    [Theory]
    [MemberData(nameof(Reads))]
    public async Task AReviewerCannotReadTheLabelQueueATaskOrTheExportBecauseOnlyTheOwnerRunsTheBenchmark(string useCase, Func<ReviewActor, Task> execute)
    {
        Assert.NotEmpty(useCase);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => execute(Reviewer));
    }

    private sealed class UnusedStore : IGateLabelStore
    {
        public Task AddAsync(GateLabelEntry entry, CancellationToken cancellationToken) => throw new InvalidOperationException();

        public Task<GateLabelQueue> ListUnlabeledAsync(int limit, CancellationToken cancellationToken) => throw new InvalidOperationException();

        public Task<GateLabelTask?> GetAsync(string candidateId, CancellationToken cancellationToken) => throw new InvalidOperationException();

        public Task<IReadOnlyList<GateLabelExportRow>> ListLabelsOfRecordAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException();
    }
}
