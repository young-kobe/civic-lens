namespace CivicLens.Application.Collection.Jobs;

public enum CollectionJobStartStatus { Started, Blocked, Exhausted, Cancelled, LostOwnership }
public enum CollectionJobBlockReason { CollectorBusy, OriginBackoff, AttemptUnresolved, RetryNotDue, BudgetReserved }

public sealed record CollectionJobStartResult(CollectionJobStartStatus Status,
    CollectionJobAttempt? Attempt = null, CollectionCollectorLease? CollectorLease = null,
    CollectionJobBlockReason? BlockReason = null, DateTimeOffset? RetryAt = null);
