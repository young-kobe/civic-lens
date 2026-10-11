using System.Data;
using System.Text.Json;
using CivicLens.Application.Analysis;
using CivicLens.Core.Analysis;
using CivicLens.Infrastructure.Analysis.Persistence;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Review;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using State = CivicLens.Application.Analysis.DocumentChangeAnalysisState;

namespace CivicLens.Infrastructure.Analysis;

public sealed class PostgresDocumentChangeAnalysisStore(IDbContextFactory<CollectionAttemptDbContext> contextFactory)
    : IDocumentChangeAnalysisStore
{
    public static PostgresDocumentChangeAnalysisStore FromConnectionString(string connectionString)
    {
        _ = PostgresCollectionAttemptStore.FromConnectionString(connectionString);
        var options = new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options;
        return new(new PooledDbContextFactory<CollectionAttemptDbContext>(options));
    }

    public async Task<IReadOnlyList<DocumentChangeAnalysisRecord>> GetEligibleAsync(int limit, CancellationToken cancellationToken)
    {
        if (limit is < 1 or > DocumentChangeAnalysisPolicy.MaximumBatch) throw new ArgumentOutOfRangeException(nameof(limit));
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var now = (await ReadClockAsync(db, cancellationToken)).Ticks;
        var rows = await db.Set<DocumentChangeAnalysisRow>().AsNoTracking()
            .Where(row => row.Task == DocumentChangeDraftingTask.Task && (row.Status == State.Pending ||
                (row.Status == State.RetryWaiting || row.Status == State.WaitingForBudget) && row.RetryAt <= now ||
                row.Status == State.Running && row.LeaseExpiresAt <= now))
            .OrderBy(row => row.CreatedAtUtcTicks).ThenBy(row => row.ComparisonId).Take(limit)
            .ToArrayAsync(cancellationToken);
        return [.. rows.Select(ToRecord)];
    }

    public async Task<IReadOnlyList<DocumentChangeAnalysisRecord>> GetByComparisonIdsAsync(
        IReadOnlyCollection<string> comparisonIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(comparisonIds);
        if (comparisonIds.Count > DocumentChangeAnalysisPolicy.MaximumBatch) throw new ArgumentOutOfRangeException(nameof(comparisonIds));
        if (comparisonIds.Count == 0) return [];
        var ids = comparisonIds.ToArray();
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.Set<DocumentChangeAnalysisRow>().AsNoTracking()
            .Where(row => row.Task == DocumentChangeDraftingTask.Task && ids.Contains(row.ComparisonId)).ToArrayAsync(cancellationToken);
        return [.. rows.Select(ToRecord)];
    }

    public async Task<AnalysisRun?> GetDraftRunAsync(string draftId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(draftId);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.Set<DocumentChangeAnalysisRunRow>().AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.DraftId == draftId, cancellationToken);
        return row is null ? null : ToRun(row);
    }

    public async Task<AnalysisRun?> GetRunAsync(string runId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.Set<DocumentChangeAnalysisRunRow>().AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.RunId == runId, cancellationToken);
        return row is null ? null : ToRun(row);
    }

    public async Task<long> GetChargedTodayAsync(CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var day = (await ReadClockAsync(db, cancellationToken)).Day;
        return await db.Set<DocumentChangeAnalysisBudgetRow>().AsNoTracking().Where(row => row.DayUtc == day)
            .Select(row => row.ChargedTokens).SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<int> RequeueFailedAsync(string? comparisonId, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var rows = await db.Set<DocumentChangeAnalysisRow>().FromSqlInterpolated($"""
            SELECT * FROM document_change_analysis
            WHERE task = {DocumentChangeDraftingTask.Task} AND status = 'Failed' AND (CAST({comparisonId} AS text) IS NULL OR comparison_id = {comparisonId})
            FOR UPDATE
            """).ToArrayAsync(cancellationToken);
        var clock = await ReadClockAsync(db, cancellationToken);
        var requeued = 0;
        foreach (var row in rows.Where(row => Transition(row, Unclaimed(row, State.Pending), clock)))
        {
            row.Attempts = 0;
            row.RetryOfRunId = null;
            requeued++;
        }
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return requeued;
    }

    public async Task<DocumentChangeAnalysisEvidence?> ReadEvidenceAsync(string comparisonId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(comparisonId);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var verified = await PostgresDocumentChangeReviewStore.ReadVerifiedComparisonAsync(db, comparisonId, cancellationToken);
        var draftId = await PostgresDocumentChangeReviewStore.ReadDraftIdForComparisonAsync(db, comparisonId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return verified is null ? null : new(verified.Summary, verified.Before, verified.After, draftId);
    }

    public async Task<DocumentChangeAnalysisClaim?> TryClaimAsync(string comparisonId, TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        DocumentChangeAnalysisPolicy.ValidateLeaseDuration(leaseDuration);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var row = await LockAsync(db, comparisonId, cancellationToken);
        var clock = await ReadClockAsync(db, cancellationToken);
        if (row is null || !IsDue(row, clock.Ticks)) return null;
        if (row.Status == State.Running) Interrupt(db, row, clock.Ticks);
        if (!Transition(row, Unclaimed(row, State.Running), clock, leaseDuration))
            throw new InvalidOperationException("Analysis claim is not a lifecycle-valid move.");
        var claim = new DocumentChangeAnalysisClaim(ToRecord(row), row.LeaseToken!, row.Fence);
        if (row.Attempts >= DocumentChangeAnalysisPolicy.MaximumAttempts)
        {
            _ = Transition(row, DocumentChangeAnalysisCheckpoint.From(claim, State.Failed, DocumentChangeAnalysisErrorCodes.AttemptsExhausted), clock);
            claim = null;
        }
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return claim;
    }

    public async Task<bool> RenewAsync(DocumentChangeAnalysisClaim claim, TimeSpan leaseDuration, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(claim);
        DocumentChangeAnalysisPolicy.ValidateLeaseDuration(leaseDuration);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var row = await LockAsync(db, claim.Record.ComparisonId, cancellationToken);
        if (row is null || !DocumentChangeAnalysisLifecycle.CanTransition(ToRecord(row),
                DocumentChangeAnalysisCheckpoint.From(claim, State.Running))) return false;
        row.LeaseExpiresAt = (await ReadClockAsync(db, cancellationToken)).Ticks + leaseDuration.Ticks;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<DocumentChangeAnalysisReservation> ReserveAsync(DocumentChangeAnalysisClaim claim, string runId,
        string inputHash, long tokens, long dailyTokenLimit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(claim);
        if (tokens < 1 || dailyTokenLimit < tokens) throw new ArgumentOutOfRangeException(nameof(tokens));
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var row = await LockAsync(db, claim.Record.ComparisonId, cancellationToken);
        var wait = DocumentChangeAnalysisCheckpoint.From(claim, State.WaitingForBudget, DocumentChangeAnalysisErrorCodes.DailyTokenLimit);
        if (row is null || row.ReservedTokens != 0 || !DocumentChangeAnalysisLifecycle.CanTransition(ToRecord(row), wait))
            return DocumentChangeAnalysisReservation.LeaseLost;
        var clock = await ReadClockAsync(db, cancellationToken);
        var day = await LockDayAsync(db, clock.Day, cancellationToken);
        var result = DocumentChangeAnalysisReservation.WaitingForBudget;
        if (day.ChargedTokens + tokens > dailyTokenLimit) _ = Transition(row, wait, clock);
        else
        {
            day.ChargedTokens += tokens;
            row.ReservedTokens = tokens;
            row.BudgetDay = clock.Day;
            row.ReservedAtUtcTicks = clock.Ticks;
            row.PendingRunId = runId;
            row.PendingInputHash = inputHash;
            result = DocumentChangeAnalysisReservation.Reserved;
        }
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<bool> CheckpointAsync(DocumentChangeAnalysisCheckpoint checkpoint, AnalysisRun? run,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        run?.Validate();
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var row = await LockAsync(db, checkpoint.ComparisonId, cancellationToken);
        if (row is null || !DocumentChangeAnalysisLifecycle.CanTransition(ToRecord(row), checkpoint)) return false;
        if (run is null ? row.ReservedTokens != 0 : run.RunId != row.PendingRunId || run.ComparisonId != row.ComparisonId)
            throw new InvalidOperationException("An analysis run must settle the reservation that it was made under.");
        var clock = await ReadClockAsync(db, cancellationToken);
        if (checkpoint.Revision is not null)
        {
            if (run?.Outcome != AnalysisRunOutcome.Drafted) throw new ArgumentException("A draft needs its drafted run.", nameof(run));
            if (await PostgresDocumentChangeReviewStore.TryAddAnalysisDraftAsync(db, checkpoint.Revision, cancellationToken))
                row.DraftId = checkpoint.Revision.DraftId;
            else
            {
                run = run with { Outcome = AnalysisRunOutcome.DraftDiscarded };
                checkpoint = checkpoint with { Revision = null, ErrorCode = DocumentChangeAnalysisErrorCodes.DraftDiscarded };
            }
        }
        if (run is not null)
        {
            var day = await LockDayAsync(db, row.BudgetDay!.Value, cancellationToken);
            day.ChargedTokens += run.ChargedTokens - row.ReservedTokens;
            ClearReservation(row);
            db.Add(ToRow(run, row.DraftId));
            if (run.Outcome is AnalysisRunOutcome.CitationRejected or AnalysisRunOutcome.OutputRejected) row.Attempts++;
            if (checkpoint.Status == State.Running) row.RetryOfRunId = run.RunId;
        }
        if (checkpoint.Status is State.Succeeded or State.Blocked or State.Failed) row.RetryOfRunId = null;
        if (!Transition(row, checkpoint, clock)) throw new InvalidOperationException("Analysis checkpoint is not valid.");
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<bool> ReleaseAsync(DocumentChangeAnalysisClaim claim, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(claim);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var row = await LockAsync(db, claim.Record.ComparisonId, cancellationToken);
        var release = DocumentChangeAnalysisCheckpoint.From(claim, State.Pending);
        if (row is null || !DocumentChangeAnalysisLifecycle.CanTransition(ToRecord(row), release)) return false;
        var clock = await ReadClockAsync(db, cancellationToken);
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
        var rows = await db.Set<DocumentChangeAnalysisRow>().FromSqlRaw(
            "SELECT * FROM document_change_analysis WHERE status = 'WaitingForBudget' FOR UPDATE")
            .Where(row => row.Task == DocumentChangeDraftingTask.Task).ToArrayAsync(cancellationToken);
        var clock = await ReadClockAsync(db, cancellationToken);
        var released = rows.Count(row => Transition(row, Unclaimed(row, State.Pending), clock));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return released;
    }

    private static bool Transition(DocumentChangeAnalysisRow row, DocumentChangeAnalysisCheckpoint update, DatabaseClock clock,
        TimeSpan? newLease = null)
    {
        if (!DocumentChangeAnalysisLifecycle.CanTransition(ToRecord(row), update)) return false;
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

    private static DocumentChangeAnalysisCheckpoint Unclaimed(DocumentChangeAnalysisRow row, State status) =>
        new(row.ComparisonId, row.Task, row.Status, row.LeaseToken, row.Fence, status);

    private static bool IsDue(DocumentChangeAnalysisRow row, long now) => row.Status switch
    {
        State.Pending => true,
        State.RetryWaiting or State.WaitingForBudget => row.RetryAt <= now,
        State.Running => row.LeaseExpiresAt <= now,
        _ => false
    };

    private static void ClearReservation(DocumentChangeAnalysisRow row)
    {
        row.ReservedTokens = 0;
        row.BudgetDay = null;
        row.ReservedAtUtcTicks = null;
        row.PendingRunId = null;
        row.PendingInputHash = null;
    }

    private static void Interrupt(CollectionAttemptDbContext db, DocumentChangeAnalysisRow row, long now)
    {
        row.Attempts++;
        if (row.ReservedTokens == 0) return;
        var run = new AnalysisRun(row.PendingRunId!, row.ComparisonId, row.Task, DocumentChangeDraftingTask.TaskVersion,
            DocumentChangeDraftingTask.Model, DocumentChangeDraftingTask.PromptVersion, DocumentChangeDraftingTask.SchemaVersion,
            row.PendingInputHash!, row.RetryOfRunId, AnalysisRunOutcome.Interrupted, AnalysisTokenUsage.None, row.ReservedTokens,
            null, null, null, [], null, new DateTimeOffset(row.ReservedAtUtcTicks!.Value, TimeSpan.Zero), new DateTimeOffset(now, TimeSpan.Zero));
        run.Validate();
        db.Add(ToRow(run, null));
        ClearReservation(row);
    }

    private static Task<DocumentChangeAnalysisRow?> LockAsync(CollectionAttemptDbContext db, string comparisonId,
        CancellationToken cancellationToken) => db.Set<DocumentChangeAnalysisRow>().FromSqlInterpolated($"""
            SELECT * FROM document_change_analysis
            WHERE comparison_id = {comparisonId} AND task = {DocumentChangeDraftingTask.Task} FOR UPDATE
            """).SingleOrDefaultAsync(cancellationToken);

    private static async Task<DocumentChangeAnalysisBudgetRow> LockDayAsync(CollectionAttemptDbContext db, DateOnly day,
        CancellationToken cancellationToken)
    {
        _ = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO document_change_analysis_budget (day_utc, charged_tokens) VALUES ({day}, 0) ON CONFLICT DO NOTHING
            """, cancellationToken);
        return await db.Set<DocumentChangeAnalysisBudgetRow>().FromSqlInterpolated($"""
            SELECT * FROM document_change_analysis_budget WHERE day_utc = {day} FOR UPDATE
            """).SingleAsync(cancellationToken);
    }

    private static async Task<DatabaseClock> ReadClockAsync(CollectionAttemptDbContext db, CancellationToken cancellationToken) =>
        await db.Database.SqlQueryRaw<DatabaseClock>("""
            SELECT (EXTRACT(EPOCH FROM clock_timestamp())::numeric * 10000000 + 621355968000000000)::bigint AS "Ticks",
                   (clock_timestamp() AT TIME ZONE 'UTC')::date AS "Day"
            """).SingleAsync(cancellationToken);

    private sealed class DatabaseClock
    {
        public long Ticks { get; set; }
        public DateOnly Day { get; set; }
    }

    private static DocumentChangeAnalysisRecord ToRecord(DocumentChangeAnalysisRow row) => new(row.ComparisonId, row.Task,
        row.Status, row.LeaseToken, row.Fence, row.Attempts,
        row.RetryAt is { } retry ? new DateTimeOffset(retry, TimeSpan.Zero) : null, row.DraftId, row.ErrorCode, row.RetryOfRunId);

    private static DocumentChangeAnalysisRunRow ToRow(AnalysisRun run, string? draftId) => new()
    {
        RunId = run.RunId,
        ComparisonId = run.ComparisonId,
        Task = run.Task,
        TaskVersion = run.TaskVersion,
        Model = run.Model,
        PromptVersion = run.PromptVersion,
        SchemaVersion = run.SchemaVersion,
        InputHash = run.InputHash,
        PreviousRunId = run.PreviousRunId,
        Outcome = run.Outcome,
        InputTokens = run.Usage.InputTokens,
        OutputTokens = run.Usage.OutputTokens,
        CacheReadTokens = run.Usage.CacheReadTokens,
        CacheWriteTokens = run.Usage.CacheWriteTokens,
        ChargedTokens = run.ChargedTokens,
        StopReason = run.StopReason,
        ProviderRequestId = run.ProviderRequestId,
        OutputJson = run.OutputJson,
        ValidationJson = run.ValidationErrors.IsEmpty ? null : JsonSerializer.Serialize(run.ValidationErrors),
        ContextJson = run.ContextJson,
        DraftId = draftId,
        RevisionNumber = draftId is null ? null : 1,
        StartedAtUtcTicks = run.StartedAtUtc.UtcTicks,
        FinishedAtUtcTicks = run.FinishedAtUtc.UtcTicks
    };

    private static AnalysisRun ToRun(DocumentChangeAnalysisRunRow row)
    {
        var run = new AnalysisRun(row.RunId, row.ComparisonId, row.Task, row.TaskVersion, row.Model, row.PromptVersion,
            row.SchemaVersion, row.InputHash, row.PreviousRunId, row.Outcome,
            new AnalysisTokenUsage(row.InputTokens, row.OutputTokens, row.CacheReadTokens, row.CacheWriteTokens), row.ChargedTokens,
            row.StopReason, row.ProviderRequestId, row.OutputJson,
            row.ValidationJson is null ? [] : [.. JsonSerializer.Deserialize<string[]>(row.ValidationJson)!],
            row.ContextJson, new DateTimeOffset(row.StartedAtUtcTicks, TimeSpan.Zero),
            new DateTimeOffset(row.FinishedAtUtcTicks, TimeSpan.Zero));
        run.Validate();
        return run;
    }
}
