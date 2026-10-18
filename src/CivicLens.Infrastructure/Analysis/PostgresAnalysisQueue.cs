using System.Data;
using CivicLens.Application.Analysis;
using CivicLens.Core.Analysis;
using CivicLens.Infrastructure.Analysis.Persistence;
using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using State = CivicLens.Application.Analysis.AnalysisState;

namespace CivicLens.Infrastructure.Analysis;

internal sealed class PostgresAnalysisQueue<TRow, TBudget, TRun>(IDbContextFactory<CollectionAttemptDbContext> contextFactory,
    IAnalysisQueueMapping<TRow, TRun> mapping) : IAnalysisQueue<TRun>
    where TRow : class, IAnalysisRow
    where TBudget : class, IAnalysisBudgetRow
    where TRun : class
{
    private readonly string lockRowSql =
        $"SELECT * FROM {mapping.Table} WHERE {mapping.KeyColumn} = {{0}} AND task = {{1}} FOR UPDATE";

    private readonly string lockRowsSql =
        $"SELECT * FROM {mapping.Table} WHERE task = {{0}} AND status = {{1}} AND (CAST({{2}} AS text) IS NULL OR error_code = {{2}}) " +
        $"AND (CAST({{3}} AS text) IS NULL OR {mapping.KeyColumn} = {{3}}) ORDER BY {mapping.KeyColumn} FOR UPDATE";

    private readonly string lockTaskSql =
        $"SELECT 1 FROM {mapping.Table} WHERE task = {{0}} ORDER BY {mapping.KeyColumn} FOR UPDATE";

    private readonly string lockSubjectsSql =
        $"SELECT * FROM {mapping.Table} WHERE task = {{0}} AND status = ANY({{1}}) AND {mapping.KeyColumn} = ANY({{2}}) " +
        $"ORDER BY {mapping.KeyColumn} FOR UPDATE";

    private readonly string ensureDaySql = mapping.BudgetTask is null
        ? $"INSERT INTO {mapping.BudgetTable} (day_utc, charged_tokens) VALUES ({{0}}, 0) ON CONFLICT DO NOTHING"
        : $"INSERT INTO {mapping.BudgetTable} (day_utc, task, charged_tokens) VALUES ({{0}}, {{1}}, 0) ON CONFLICT DO NOTHING";

    private readonly string releaseWaitsSql =
        $"SELECT * FROM {mapping.Table} WHERE task = {{0}} AND status = 'WaitingForBudget' ORDER BY {mapping.KeyColumn} FOR UPDATE";

    private readonly string readDaySql = ReadDaySql(mapping);

    private readonly string lockDaySql = ReadDaySql(mapping) + " FOR UPDATE";

    public async Task<IReadOnlyList<AnalysisRecord>> GetEligibleAsync(int limit, CancellationToken cancellationToken)
    {
        if (limit is < 1 or > AnalysisPolicy.MaximumBatch) throw new ArgumentOutOfRangeException(nameof(limit));
        var task = mapping.Identity.Task;
        var key = mapping.KeyProperty;
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var now = (await DatabaseClock.ReadAsync(db, cancellationToken)).Ticks;
        var rows = await db.Set<TRow>().AsNoTracking()
            .Where(row => row.Task == task && (row.Status == State.Pending ||
                (row.Status == State.RetryWaiting || row.Status == State.WaitingForBudget) && row.RetryAt <= now ||
                row.Status == State.Running && row.LeaseExpiresAt <= now))
            .OrderBy(row => row.Priority).ThenBy(row => row.CreatedAtUtcTicks).ThenBy(row => EF.Property<string>(row, key)).Take(limit)
            .ToArrayAsync(cancellationToken);
        return [.. rows.Select(ToRecord)];
    }

    public async Task<long> GetChargedTodayAsync(CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var day = (await DatabaseClock.ReadAsync(db, cancellationToken)).Day;
        return await db.Set<TBudget>().FromSqlRaw(readDaySql, BudgetArguments(day)).AsNoTracking()
            .Select(row => row.ChargedTokens).SingleOrDefaultAsync(cancellationToken);
    }

    public Task<int> RequeueFailedAsync(string? subjectId, CancellationToken cancellationToken) =>
        RequeueAsync(State.Failed, null, subjectId, null, cancellationToken);

    public async Task<int> RequeueAsync(State status, string? errorCode, string? subjectId,
        Func<CollectionAttemptDbContext, TRow[], CancellationToken, Task<IEnumerable<TRow>>>? narrow, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var locked = await db.Set<TRow>().FromSqlRaw(lockRowsSql, mapping.Identity.Task, status.ToString(), errorCode!, subjectId!)
            .ToArrayAsync(cancellationToken);
        var selected = narrow is null ? locked : await narrow(db, locked, cancellationToken);
        var clock = await DatabaseClock.ReadAsync(db, cancellationToken);
        var requeued = selected.Count(row => TryRequeue(db, row, clock));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return requeued;
    }

    public Task LockRowsAsync(CollectionAttemptDbContext db, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlRawAsync(lockTaskSql, [mapping.Identity.Task], cancellationToken);

    public async Task<int> RequeueInAsync(CollectionAttemptDbContext db, State[] statuses, string[] subjectIds,
        CancellationToken cancellationToken)
    {
        var rows = await db.Set<TRow>().FromSqlRaw(lockSubjectsSql, mapping.Identity.Task,
            statuses.Select(status => status.ToString()).ToArray(), subjectIds).ToArrayAsync(cancellationToken);
        var clock = await DatabaseClock.ReadAsync(db, cancellationToken);
        return rows.Count(row => TryRequeue(db, row, clock));
    }

    public async Task<AnalysisClaim?> TryClaimAsync(string subjectId, TimeSpan leaseDuration, CancellationToken cancellationToken)
    {
        AnalysisPolicy.ValidateLeaseDuration(leaseDuration);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var row = await LockAsync(db, subjectId, cancellationToken);
        var clock = await DatabaseClock.ReadAsync(db, cancellationToken);
        if (row is null || !IsDue(row, clock.Ticks)) return null;
        if (row.Status == State.Running) Interrupt(db, row, clock.Ticks);
        if (!Transition(row, Unclaimed(row, State.Running), clock, leaseDuration))
            throw new InvalidOperationException("Analysis claim is not a lifecycle-valid move.");
        var claim = new AnalysisClaim(ToRecord(row), row.LeaseToken!, row.Fence);
        if (row.Attempts >= AnalysisPolicy.MaximumAttempts)
        {
            _ = Transition(row, AnalysisCheckpoint.From(claim, State.Failed, AnalysisErrorCodes.AttemptsExhausted), clock);
            claim = null;
        }
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return claim;
    }

    public async Task<bool> RenewAsync(AnalysisClaim claim, TimeSpan leaseDuration, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(claim);
        AnalysisPolicy.ValidateLeaseDuration(leaseDuration);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var row = await LockAsync(db, claim.Record.SubjectId, cancellationToken);
        if (row is null || !mapping.CanTransition(ToRecord(row), AnalysisCheckpoint.From(claim, State.Running))) return false;
        row.LeaseExpiresAt = (await DatabaseClock.ReadAsync(db, cancellationToken)).Ticks + leaseDuration.Ticks;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<AnalysisReservation> ReserveAsync(AnalysisClaim claim, string runId, string inputHash, long tokens,
        long dailyTokenLimit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(claim);
        if (tokens < 1 || dailyTokenLimit < tokens) throw new ArgumentOutOfRangeException(nameof(tokens));
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var row = await LockAsync(db, claim.Record.SubjectId, cancellationToken);
        var wait = AnalysisCheckpoint.From(claim, State.WaitingForBudget, AnalysisErrorCodes.DailyTokenLimit);
        if (row is null || row.ReservedTokens != 0 || !mapping.CanTransition(ToRecord(row), wait)) return AnalysisReservation.LeaseLost;
        var clock = await DatabaseClock.ReadAsync(db, cancellationToken);
        var day = await LockDayAsync(db, clock.Day, cancellationToken);
        var result = AnalysisReservation.WaitingForBudget;
        if (day.ChargedTokens + tokens > dailyTokenLimit) _ = Transition(row, wait, clock);
        else
        {
            day.ChargedTokens += tokens;
            row.ReservedTokens = tokens;
            row.BudgetDay = clock.Day;
            row.ReservedAtUtcTicks = clock.Ticks;
            row.PendingRunId = runId;
            row.PendingInputHash = inputHash;
            result = AnalysisReservation.Reserved;
        }
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<bool> CheckpointAsync(AnalysisCheckpoint checkpoint, TRun? run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        if (run is not null) mapping.RunOf(run).Validate();
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var row = await LockAsync(db, checkpoint.SubjectId, cancellationToken);
        if (row is null || !mapping.CanTransition(ToRecord(row), checkpoint)) return false;
        var clock = await DatabaseClock.ReadAsync(db, cancellationToken);
        (checkpoint, run) = await mapping.PrepareSettlementAsync(db, row, new(checkpoint, run), cancellationToken);
        if (run is not null) await SettleRunAsync(db, row, run, cancellationToken);
        else if (row.ReservedTokens != 0)
            throw new InvalidOperationException("An analysis run must settle the reservation that it was made under.");
        if (checkpoint.Status == State.Running && run is not null) row.RetryOfRunId = mapping.RunOf(run).RunId;
        if (checkpoint.Status is State.Succeeded or State.Blocked or State.Failed) row.RetryOfRunId = null;
        if (!Transition(row, checkpoint, clock)) throw new InvalidOperationException("Analysis checkpoint is not valid.");
        await mapping.AfterTransitionAsync(db, row, checkpoint, clock.Ticks, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<bool> ReleaseAsync(AnalysisClaim claim, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(claim);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var row = await LockAsync(db, claim.Record.SubjectId, cancellationToken);
        var release = AnalysisCheckpoint.From(claim, State.Pending);
        if (row is null || !mapping.CanTransition(ToRecord(row), release)) return false;
        var clock = await DatabaseClock.ReadAsync(db, cancellationToken);
        if (row.ReservedTokens > 0) Interrupt(db, row, clock.Ticks);
        _ = Transition(row, release, clock);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<int> ReleaseBudgetWaitsAsync(CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var rows = await db.Set<TRow>().FromSqlRaw(releaseWaitsSql, mapping.Identity.Task).ToArrayAsync(cancellationToken);
        var clock = await DatabaseClock.ReadAsync(db, cancellationToken);
        var released = rows.Count(row => Transition(row, Unclaimed(row, State.Pending), clock));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return released;
    }

    private bool TryRequeue(CollectionAttemptDbContext db, TRow row, DatabaseTime clock)
    {
        var pending = Unclaimed(row, State.Pending);
        if (!mapping.CanTransition(ToRecord(row), pending)) return false;
        if (row.ReservedTokens > 0) Interrupt(db, row, clock.Ticks);
        _ = Transition(row, pending, clock);
        row.Attempts = 0;
        row.RetryOfRunId = null;
        return true;
    }

    private static AnalysisRecord ToRecord(TRow row) => new(row.SubjectId, row.Task, row.Status, row.LeaseToken, row.Fence, row.Attempts,
        row.RetryAt is { } retry ? new DateTimeOffset(retry, TimeSpan.Zero) : null, row.ErrorCode, row.RetryOfRunId);

    private async Task SettleRunAsync(CollectionAttemptDbContext db, TRow row, TRun wrapped, CancellationToken cancellationToken)
    {
        var run = mapping.RunOf(wrapped);
        if (run.ComparisonId != row.SubjectId) throw new InvalidOperationException("An analysis run must belong to its subject.");
        if (run.Outcome == AnalysisRunOutcome.Bypassed)
        {
            if (row.ReservedTokens != 0 || run.ChargedTokens != 0)
                throw new InvalidOperationException("A run without a model call cannot hold or charge a reservation.");
        }
        else
        {
            if (run.RunId != row.PendingRunId)
                throw new InvalidOperationException("An analysis run must settle the reservation that it was made under.");
            var day = await LockDayAsync(db, row.BudgetDay!.Value, cancellationToken);
            day.ChargedTokens += run.ChargedTokens - row.ReservedTokens;
            ClearReservation(row);
        }
        mapping.AddRun(db, row, wrapped);
        if (run.Outcome is AnalysisRunOutcome.CitationRejected or AnalysisRunOutcome.OutputRejected) row.Attempts++;
    }

    private bool Transition(TRow row, AnalysisCheckpoint update, DatabaseTime clock, TimeSpan? newLease = null)
    {
        if (!mapping.CanTransition(ToRecord(row), update)) return false;
        row.Status = update.Status;
        row.ErrorCode = update.ErrorCode;
        row.RetryAt = update.Status switch
        {
            State.RetryWaiting => clock.Ticks + update.RetryDelay!.Value.Ticks,
            State.WaitingForBudget => new DateTime(clock.Day.AddDays(1), TimeOnly.MinValue, DateTimeKind.Utc).Ticks,
            _ => null
        };
        if (update.Status == State.RetryWaiting) row.Attempts++;
        if (newLease is { } lease)
        {
            row.LeaseToken = Guid.NewGuid().ToString("N");
            row.LeaseExpiresAt = clock.Ticks + lease.Ticks;
            row.Fence++;
        }
        else if (update.Status != State.Running)
        {
            row.LeaseToken = null;
            row.LeaseExpiresAt = null;
        }
        return true;
    }

    private static AnalysisCheckpoint Unclaimed(TRow row, State status) =>
        new(row.SubjectId, row.Task, row.Status, row.LeaseToken, row.Fence, status);

    private static bool IsDue(TRow row, long now) => row.Status switch
    {
        State.Pending => true,
        State.RetryWaiting or State.WaitingForBudget => row.RetryAt <= now,
        State.Running => row.LeaseExpiresAt <= now,
        _ => false
    };

    private static void ClearReservation(TRow row)
    {
        row.ReservedTokens = 0;
        row.BudgetDay = null;
        row.ReservedAtUtcTicks = null;
        row.PendingRunId = null;
        row.PendingInputHash = null;
    }

    private void Interrupt(CollectionAttemptDbContext db, TRow row, long now)
    {
        row.Attempts++;
        if (row.ReservedTokens == 0) return;
        var identity = mapping.Identity;
        var run = new AnalysisRun(row.PendingRunId!, row.SubjectId, row.Task, identity.TaskVersion, identity.Model,
            identity.PromptVersion, identity.SchemaVersion, row.PendingInputHash!, row.RetryOfRunId, AnalysisRunOutcome.Interrupted,
            AnalysisTokenUsage.None, row.ReservedTokens, null, null, null, [], null,
            new DateTimeOffset(row.ReservedAtUtcTicks!.Value, TimeSpan.Zero), new DateTimeOffset(now, TimeSpan.Zero));
        run.Validate();
        mapping.AddRun(db, row, mapping.WrapInterrupted(run));
        ClearReservation(row);
    }

    private Task<TRow?> LockAsync(CollectionAttemptDbContext db, string subjectId, CancellationToken cancellationToken) =>
        db.Set<TRow>().FromSqlRaw(lockRowSql, subjectId, mapping.Identity.Task).SingleOrDefaultAsync(cancellationToken);

    private async Task<TBudget> LockDayAsync(CollectionAttemptDbContext db, DateOnly day, CancellationToken cancellationToken)
    {
        _ = await db.Database.ExecuteSqlRawAsync(ensureDaySql, BudgetArguments(day), cancellationToken);
        return await db.Set<TBudget>().FromSqlRaw(lockDaySql, BudgetArguments(day)).SingleAsync(cancellationToken);
    }

    private static string ReadDaySql(IAnalysisQueueMapping<TRow, TRun> mapping) => mapping.BudgetTask is null
        ? $"SELECT * FROM {mapping.BudgetTable} WHERE day_utc = {{0}}"
        : $"SELECT * FROM {mapping.BudgetTable} WHERE day_utc = {{0}} AND task = {{1}}";

    private object[] BudgetArguments(DateOnly day) => mapping.BudgetTask is { } task ? [day, task] : [day];
}
