using CivicLens.Application.Collection.Jobs;
using CivicLens.Collection.Contracts;
using static CivicLens.Tests.Fixtures.CollectionFixtures;

namespace CivicLens.Tests.Application.Collection.Jobs;

public sealed class CollectionJobLifecycleTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(CollectionJobAttemptOutcome.Succeeded, false, CollectionJobState.Succeeded)]
    [InlineData(CollectionJobAttemptOutcome.PermanentFailure, false, CollectionJobState.Failed)]
    [InlineData(CollectionJobAttemptOutcome.RetryableFailure, false, CollectionJobState.WaitingToRetry)]
    [InlineData(CollectionJobAttemptOutcome.RetryableFailure, true, CollectionJobState.Failed)]
    [InlineData(CollectionJobAttemptOutcome.Interrupted, false, CollectionJobState.WaitingToRetry)]
    public void SettlementTransitionsRespectOutcomeAndRemainingAttempts(CollectionJobAttemptOutcome outcome,
        bool exhausted, CollectionJobState expected)
    {
        Assert.Equal(expected, CollectionJobLifecycle.Transition(CollectionJobState.Running, outcome,
            cancellationRequested: false, attemptsExhausted: exhausted));
    }

    [Theory]
    [InlineData(404, CollectionJobAttemptOutcome.PermanentFailure, CollectionJobState.Failed)]
    [InlineData(503, CollectionJobAttemptOutcome.RetryableFailure, CollectionJobState.WaitingToRetry)]
    public void APhotoThatIsMissingEndsTheJobFailedWithNoRetryWhileAServerErrorRetries(int status,
        CollectionJobAttemptOutcome outcome, CollectionJobState state)
    {
        var receipt = Receipt(Request()) with
        {
            Outcome = CollectionOutcome.Failed,
            Capture = null,
            FailureCode = CollectionFailureCode.HttpError,
            Response = new HttpResponseMetadata { StatusCode = status, ContentEncodings = [] }
        };

        var resolution = CollectionJobLifecycle.Resolve(receipt);

        Assert.Equal(outcome, resolution.Outcome);
        Assert.Equal(state, CollectionJobLifecycle.Transition(CollectionJobState.Running, resolution.Outcome,
            cancellationRequested: false, attemptsExhausted: false));
    }

    [Fact]
    public void CancellationWinsSettlementAndTerminalStateCannotTransitionAgain()
    {
        Assert.Equal(CollectionJobState.Cancelled, CollectionJobLifecycle.Transition(CollectionJobState.Running,
            CollectionJobAttemptOutcome.Succeeded, cancellationRequested: true, attemptsExhausted: false));
        Assert.Throws<InvalidOperationException>(() => CollectionJobLifecycle.Transition(CollectionJobState.Succeeded,
            CollectionJobAttemptOutcome.Succeeded, false, false));
    }

    [Fact]
    public void RetryDelayUsesRetryAfterAndSaturatesAtDateTimeMaximum()
    {
        var policy = new CollectionJobPolicy { InitialRetryDelaySeconds = 30, MaximumRetryDelaySeconds = 3600 };
        Assert.Equal(Now.AddSeconds(90), CollectionJobLifecycle.RetryAt(Now, policy, 2, TimeSpan.FromSeconds(90)));
        Assert.Equal(DateTimeOffset.MaxValue, CollectionJobLifecycle.RetryAt(DateTimeOffset.MaxValue.AddSeconds(-1),
            policy, 3, TimeSpan.MaxValue));
    }
}
