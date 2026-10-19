using CivicLens.Collection.Contracts;
using CivicLens.Core.Collection;

namespace CivicLens.Application.Collection.Jobs;

/// <summary>Pure job retry, outcome, and reserved-budget decisions shared with persistence adapters.</summary>
public static class CollectionJobLifecycle
{
    public static CollectionAttemptResolution Resolve(CollectionResult receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        var outcome = receipt.Outcome switch
        {
            CollectionOutcome.Captured or CollectionOutcome.NotModified => CollectionJobAttemptOutcome.Succeeded,
            CollectionOutcome.Deferred or CollectionOutcome.Failed when IsRetryable(
                receipt.Outcome == CollectionOutcome.Deferred, receipt.FailureCode?.ToString(), receipt.Response?.StatusCode) => CollectionJobAttemptOutcome.RetryableFailure,
            _ => CollectionJobAttemptOutcome.PermanentFailure
        };
        return new CollectionAttemptResolution(outcome, receipt, receipt.FailureCode?.ToString(),
            ToRetryDelay(receipt.RetryAfterSeconds), receipt.RobotsCrawlDelayMilliseconds);
    }

    private static bool IsRetryable(bool deferred, string? failureCode, int? statusCode) => deferred ||
        failureCode is nameof(CollectionFailureCode.TransportError) or nameof(CollectionFailureCode.Timeout) or
            nameof(CollectionFailureCode.IncompleteResponse) || statusCode is >= 500 and <= 599;

    public static CollectionAttemptResolution Resolve(StoredCollectionAttempt retained)
    {
        ArgumentNullException.ThrowIfNull(retained);
        var attempt = retained.AttemptResult;
        if (attempt is CapturedAttemptResult or NotModifiedAttemptResult)
            return new CollectionAttemptResolution(CollectionJobAttemptOutcome.Succeeded, null, "EvidenceAlreadyImported",
                RobotsCrawlDelayMilliseconds: retained.RobotsCrawlDelayMilliseconds);
        var failureCode = attempt switch
        {
            FailedAttemptResult failed => failed.FailureCode,
            DeferredAttemptResult deferred => deferred.FailureCode,
            _ => "UnknownImportedOutcome"
        };
        var retryable = IsRetryable(attempt is DeferredAttemptResult, failureCode, attempt.Response?.StatusCode);
        TimeSpan? delay = attempt is DeferredAttemptResult deferredAttempt ? deferredAttempt.RetryDelay : null;
        return new CollectionAttemptResolution(retryable ? CollectionJobAttemptOutcome.RetryableFailure :
            CollectionJobAttemptOutcome.PermanentFailure, null, failureCode, delay,
            retained.RobotsCrawlDelayMilliseconds);
    }

    public static CollectionJobState Transition(CollectionJobState current, CollectionJobAttemptOutcome outcome,
        bool cancellationRequested, bool attemptsExhausted)
    {
        if (current is not (CollectionJobState.Running or CollectionJobState.Cancelled))
            throw new InvalidOperationException($"Cannot settle an attempt while job is {current}.");
        if (current == CollectionJobState.Cancelled || cancellationRequested)
            return CollectionJobState.Cancelled;
        return outcome switch
        {
            CollectionJobAttemptOutcome.Succeeded => CollectionJobState.Succeeded,
            CollectionJobAttemptOutcome.PermanentFailure => CollectionJobState.Failed,
            CollectionJobAttemptOutcome.RetryableFailure when !attemptsExhausted => CollectionJobState.WaitingToRetry,
            CollectionJobAttemptOutcome.RetryableFailure => CollectionJobState.Failed,
            CollectionJobAttemptOutcome.Interrupted when !attemptsExhausted => CollectionJobState.WaitingToRetry,
            CollectionJobAttemptOutcome.Interrupted => CollectionJobState.Failed,
            _ => throw new ArgumentOutOfRangeException(nameof(outcome))
        };
    }

    public static bool CanReserve(CollectionJobDefinition definition, CollectionRequest request,
        CollectionJobState state, int attemptsStarted, CollectionJobCharges charged)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(charged);
        if (state is not (CollectionJobState.Pending or CollectionJobState.WaitingToRetry)) return false;
        if (attemptsStarted >= definition.Policy.MaxAttempts) return false;
        return (long)charged.Requests + request.MaxRequests <= definition.Policy.ResolveMaxTotalRequests(request) &&
            charged.Bytes + request.MaxBytes <= definition.Policy.ResolveMaxTotalBytes(request) &&
            charged.TimeoutSeconds + request.TimeoutSeconds <= definition.Policy.ResolveMaxTotalTimeoutSeconds(request);
    }

    public static DateTimeOffset RetryAt(DateTimeOffset now, CollectionJobPolicy policy, int completedAttemptCount,
        TimeSpan? retryAfter)
    {
        ArgumentNullException.ThrowIfNull(policy);
        var exponent = Math.Clamp(completedAttemptCount - 1, 0, 62);
        var seconds = policy.InitialRetryDelaySeconds;
        for (var index = 0; index < exponent && seconds < policy.MaximumRetryDelaySeconds; index++)
            seconds = Math.Min(policy.MaximumRetryDelaySeconds, seconds > int.MaxValue / 2 ? int.MaxValue : seconds * 2);
        var delay = TimeSpan.FromSeconds(seconds);
        if (retryAfter is { } serverDelay && serverDelay > delay) delay = serverDelay;
        return SaturatingAdd(now, delay);
    }

    private static TimeSpan? ToRetryDelay(long? seconds)
    {
        if (seconds is null) return null;
        return seconds > TimeSpan.MaxValue.Ticks / TimeSpan.TicksPerSecond
            ? TimeSpan.MaxValue
            : TimeSpan.FromSeconds(seconds.Value);
    }

    private static DateTimeOffset SaturatingAdd(DateTimeOffset value, TimeSpan delay)
    {
        try { return value + delay; }
        catch (ArgumentOutOfRangeException) { return DateTimeOffset.MaxValue; }
    }
}
