using CivicLens.Application.Analysis;
using CivicLens.Core.Analysis;
using CivicLens.Core.Review;

namespace CivicLens.Tests.Application.Analysis;

public sealed class RequeueAnalysesTests
{
    private const string ComparisonId = "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";
    private static readonly ReviewActor Owner = new("auth0|owner", ReviewRole.Owner);
    private readonly FakeAnalysisStore store = new(0);

    [Theory]
    [InlineData("auth0|friend", ReviewRole.Reviewer)]
    [InlineData("civic-lens:analysis", ReviewRole.Owner)]
    public async Task OnlyAHumanOwnerRequeues(string subject, ReviewRole role)
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Requeue().ExecuteAsync(new ReviewActor(subject, role), null));

        Assert.Empty(store.Requeues);
    }

    [Theory]
    [InlineData("not-a-hash")]
    [InlineData("CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC")]
    public async Task ASubjectIdMustBeALowercaseSha256(string subjectId)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Requeue().ExecuteAsync(Owner, subjectId));

        Assert.Empty(store.Requeues);
    }

    [Fact]
    public async Task TheOwnerRequeuesEverythingOrOneSubject()
    {
        await Requeue().ExecuteAsync(Owner, null);
        await Requeue().ExecuteAsync(Owner, ComparisonId);

        Assert.Equal([null, ComparisonId], store.Requeues);
    }

    private RequeueAnalyses<AnalysisRun> Requeue() => new(store);
}
