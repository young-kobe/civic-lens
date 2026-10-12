using System.Data;
using System.Linq.Expressions;
using System.Text.Json;
using CivicLens.Application.Analysis;
using CivicLens.Application.Statements;
using CivicLens.Core.Analysis;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Documents;
using CivicLens.Infrastructure.Statements.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using State = CivicLens.Application.Analysis.DocumentChangeAnalysisState;

namespace CivicLens.Infrastructure.Statements;

public sealed class PostgresStatementAnalysisStore(IDbContextFactory<CollectionAttemptDbContext> contextFactory)
    : IStatementAnalysisStore
{
    private const string DraftTask = "draft";

    public static PostgresStatementAnalysisStore FromConnectionString(string connectionString)
    {
        _ = PostgresCollectionAttemptStore.FromConnectionString(connectionString);
        var options = new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options;
        return new(new PooledDbContextFactory<CollectionAttemptDbContext>(options));
    }

    public async Task<IReadOnlyList<StatementAnalysisRecord>> GetEligibleAsync(int limit, CancellationToken cancellationToken)
    {
        if (limit is < 1 or > DocumentChangeAnalysisPolicy.MaximumBatch) throw new ArgumentOutOfRangeException(nameof(limit));
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var now = (await DatabaseClock.ReadAsync(db, cancellationToken)).Ticks;
        var rows = await db.Set<StatementAnalysisRow>().AsNoTracking()
            .Where(row => row.Task == StatementGateTask.Task && (row.Status == State.Pending ||
                (row.Status == State.RetryWaiting || row.Status == State.WaitingForBudget) && row.RetryAt <= now ||
                row.Status == State.Running && row.LeaseExpiresAt <= now))
            .OrderBy(row => row.CreatedAtUtcTicks).ThenBy(row => row.CandidateId).Take(limit)
            .ToArrayAsync(cancellationToken);
        return [.. rows.Select(ToRecord)];
    }

    public async Task<StatementGateRun?> GetRunAsync(string runId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.Set<StatementAnalysisRunRow>().AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.RunId == runId, cancellationToken);
        return row is null ? null : ToRun(row);
    }

    public async Task<long> GetChargedTodayAsync(CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var day = (await DatabaseClock.ReadAsync(db, cancellationToken)).Day;
        return await db.Set<StatementAnalysisBudgetRow>().AsNoTracking()
            .Where(row => row.DayUtc == day && row.Task == StatementGateTask.Task)
            .Select(row => row.ChargedTokens).SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<StatementGateEvidence?> ReadEvidenceAsync(string candidateId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(candidateId);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var extractionId = await db.Set<StatementCandidateRow>().AsNoTracking().Where(row => row.CandidateId == candidateId)
            .Select(row => row.ExtractionId).SingleOrDefaultAsync(cancellationToken);
        if (extractionId is null) return null;
        var extraction = await PostgresDocumentExtractionStore.ReadAsync(db, extractionId, cancellationToken);
        return extraction is null ? null : new(candidateId, extraction);
    }

    public async Task<StatementAnalysisClaim?> TryClaimAsync(string candidateId, TimeSpan leaseDuration, CancellationToken cancellationToken)
    {
        DocumentChangeAnalysisPolicy.ValidateLeaseDuration(leaseDuration);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var row = await LockAsync(db, candidateId, cancellationToken);
        var clock = await DatabaseClock.ReadAsync(db, cancellationToken);
        if (row is null || !IsDue(row, clock.Ticks)) return null;
        if (row.Status == State.Running) Interrupt(db, row, clock.Ticks);
        if (!Transition(row, Unclaimed(row, State.Running), clock, leaseDuration))
            throw new InvalidOperationException("Statement analysis claim is not a lifecycle-valid move.");
        var claim = new StatementAnalysisClaim(ToRecord(row), row.LeaseToken!, row.Fence);
        if (row.Attempts >= DocumentChangeAnalysisPolicy.MaximumAttempts)
        {
            _ = Transition(row, StatementAnalysisCheckpoint.From(claim, State.Failed, DocumentChangeAnalysisErrorCodes.AttemptsExhausted), clock);
            claim = null;
        }
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return claim;
    }

    public async Task<bool> RenewAsync(StatementAnalysisClaim claim, TimeSpan leaseDuration, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(claim);
        DocumentChangeAnalysisPolicy.ValidateLeaseDuration(leaseDuration);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var row = await LockAsync(db, claim.Record.CandidateId, cancellationToken);
        if (row is null || !StatementAnalysisLifecycle.CanTransition(ToRecord(row), StatementAnalysisCheckpoint.From(claim, State.Running)))
            return false;
        row.LeaseExpiresAt = (await DatabaseClock.ReadAsync(db, cancellationToken)).Ticks + leaseDuration.Ticks;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<DocumentChangeAnalysisReservation> ReserveAsync(StatementAnalysisClaim claim, string runId, string inputHash,
        long tokens, long dailyTokenLimit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(claim);
        if (tokens < 1 || dailyTokenLimit < tokens) throw new ArgumentOutOfRangeException(nameof(tokens));
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var row = await LockAsync(db, claim.Record.CandidateId, cancellationToken);
        var wait = StatementAnalysisCheckpoint.From(claim, State.WaitingForBudget, DocumentChangeAnalysisErrorCodes.DailyTokenLimit);
        if (row is null || row.ReservedTokens != 0 || !StatementAnalysisLifecycle.CanTransition(ToRecord(row), wait))
            return DocumentChangeAnalysisReservation.LeaseLost;
        var clock = await DatabaseClock.ReadAsync(db, cancellationToken);
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

    public async Task<bool> CheckpointAsync(StatementAnalysisCheckpoint checkpoint, StatementGateRun? gateRun, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        gateRun?.Run.Validate();
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var row = await LockAsync(db, checkpoint.CandidateId, cancellationToken);
        if (row is null || !StatementAnalysisLifecycle.CanTransition(ToRecord(row), checkpoint)) return false;
        var clock = await DatabaseClock.ReadAsync(db, cancellationToken);
        if (gateRun is not null) await AddRunAsync(db, row, gateRun, cancellationToken);
        else if (row.ReservedTokens != 0)
            throw new InvalidOperationException("A statement analysis run must settle the reservation that it was made under.");
        if (checkpoint.Status == State.Running && gateRun is not null) row.RetryOfRunId = gateRun.Run.RunId;
        if (checkpoint.Status is State.Succeeded or State.Blocked or State.Failed) row.RetryOfRunId = null;
        if (!Transition(row, checkpoint, clock)) throw new InvalidOperationException("Statement analysis checkpoint is not valid.");
        if (checkpoint.Status == State.Succeeded && StatementGateReasons.QueuesDraft(checkpoint.ErrorCode))
            await QueueDraftAsync(db, row.CandidateId, clock.Ticks, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<bool> ReleaseAsync(StatementAnalysisClaim claim, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(claim);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var row = await LockAsync(db, claim.Record.CandidateId, cancellationToken);
        var release = StatementAnalysisCheckpoint.From(claim, State.Pending);
        if (row is null || !StatementAnalysisLifecycle.CanTransition(ToRecord(row), release)) return false;
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
        var rows = await db.Set<StatementAnalysisRow>().FromSqlInterpolated($"""
            SELECT * FROM statement_analysis WHERE task = {StatementGateTask.Task} AND status = 'WaitingForBudget' FOR UPDATE
            """).ToArrayAsync(cancellationToken);
        var clock = await DatabaseClock.ReadAsync(db, cancellationToken);
        var released = rows.Count(row => Transition(row, Unclaimed(row, State.Pending), clock));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return released;
    }

    public async Task<int> RequeueFailedAsync(string? candidateId, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var rows = await db.Set<StatementAnalysisRow>().FromSqlInterpolated($"""
            SELECT * FROM statement_analysis
            WHERE task = {StatementGateTask.Task} AND status = 'Failed' AND (CAST({candidateId} AS text) IS NULL OR candidate_id = {candidateId})
            FOR UPDATE
            """).ToArrayAsync(cancellationToken);
        var clock = await DatabaseClock.ReadAsync(db, cancellationToken);
        var requeued = RequeueRows(rows, clock);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return requeued;
    }

    public async Task<int> RegateAsync(string issueRevisionId, DateOnly? since, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var skipped = await db.Set<StatementAnalysisRow>().FromSqlInterpolated($"""
            SELECT * FROM statement_analysis
            WHERE task = {StatementGateTask.Task} AND status = 'Succeeded' AND error_code = {StatementGateReasons.NoTrackedIssue}
            FOR UPDATE
            """).ToArrayAsync(cancellationToken);
        var ids = skipped.Select(row => row.CandidateId).ToArray();
        var boundToOlder = await CandidatesBoundToOlderRevisionAsync(db, ids, issueRevisionId, cancellationToken);
        var capturedSince = since is null ? null : await CandidatesCapturedSinceAsync(db, ids, since.Value, cancellationToken);
        var clock = await DatabaseClock.ReadAsync(db, cancellationToken);
        var requeued = RequeueRows(skipped.Where(row => boundToOlder.Contains(row.CandidateId) &&
            (capturedSince is null || capturedSince.Contains(row.CandidateId))), clock);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return requeued;
    }

    public async Task<IReadOnlyList<StatementGateListItem>> ListAsync(StatementGateView? view, int limit, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var gates = db.Set<StatementAnalysisRow>().AsNoTracking().Where(row => row.Task == StatementGateTask.Task);
        if (view is { } selected) gates = gates.Where(InView(selected));
        var rows = await (from gate in gates
                          join candidate in db.Set<StatementCandidateRow>().AsNoTracking() on gate.CandidateId equals candidate.CandidateId
                          orderby gate.CreatedAtUtcTicks, gate.CandidateId
                          select new { Gate = gate, candidate.SourceId, candidate.Url }).Take(limit).ToArrayAsync(cancellationToken);
        var ids = rows.Select(row => row.Gate.CandidateId).ToArray();
        var runs = await db.Set<StatementAnalysisRunRow>().AsNoTracking()
            .Where(run => run.Task == StatementGateTask.Task && ids.Contains(run.CandidateId))
            .Select(run => new { run.CandidateId, run.RunId, run.IssueRevisionId, run.StartedAtUtcTicks }).ToArrayAsync(cancellationToken);
        var latest = runs.GroupBy(run => run.CandidateId).ToDictionary(group => group.Key,
            group => group.OrderByDescending(run => run.StartedAtUtcTicks).First());
        return [.. rows.Select(row => new StatementGateListItem(row.Gate.CandidateId, row.SourceId, row.Url, row.Gate.Status,
            ViewOf(row.Gate), row.Gate.ErrorCode, row.Gate.Attempts, latest.GetValueOrDefault(row.Gate.CandidateId)?.RunId,
            latest.GetValueOrDefault(row.Gate.CandidateId)?.IssueRevisionId))];
    }

    public async Task<IReadOnlyDictionary<StatementGateView, int>> CountAsync(CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var groups = await db.Set<StatementAnalysisRow>().AsNoTracking().Where(row => row.Task == StatementGateTask.Task)
            .GroupBy(row => new { row.Status, row.ErrorCode }).Select(group => new { group.Key.Status, group.Key.ErrorCode, Count = group.Count() })
            .ToArrayAsync(cancellationToken);
        var counts = Enum.GetValues<StatementGateView>().ToDictionary(view => view, _ => 0);
        foreach (var group in groups)
            counts[ViewOf(group.Status, group.ErrorCode)] += group.Count;
        return counts;
    }

    private static async Task AddRunAsync(CollectionAttemptDbContext db, StatementAnalysisRow row, StatementGateRun gateRun,
        CancellationToken cancellationToken)
    {
        var run = gateRun.Run;
        if (run.ComparisonId != row.CandidateId) throw new InvalidOperationException("A statement analysis run must belong to its candidate.");
        if (run.Outcome == AnalysisRunOutcome.Bypassed)
        {
            if (row.ReservedTokens != 0 || run.ChargedTokens != 0)
                throw new InvalidOperationException("A run without a model call cannot hold or charge a reservation.");
        }
        else
        {
            if (run.RunId != row.PendingRunId)
                throw new InvalidOperationException("A statement analysis run must settle the reservation that it was made under.");
            var day = await LockDayAsync(db, row.BudgetDay!.Value, cancellationToken);
            day.ChargedTokens += run.ChargedTokens - row.ReservedTokens;
            ClearReservation(row);
        }
        db.Add(ToRow(gateRun));
        if (run.Outcome is AnalysisRunOutcome.CitationRejected or AnalysisRunOutcome.OutputRejected) row.Attempts++;
    }

    private static Task QueueDraftAsync(CollectionAttemptDbContext db, string candidateId, long now, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO statement_analysis (candidate_id, task, status, fence, attempts, reserved_tokens, created_at_utc_ticks)
            VALUES ({candidateId}, {DraftTask}, 'Pending', 0, 0, 0, {now})
            ON CONFLICT (candidate_id, task) DO NOTHING
            """, cancellationToken);

    private static int RequeueRows(IEnumerable<StatementAnalysisRow> rows, DatabaseTime clock)
    {
        var requeued = 0;
        foreach (var row in rows.Where(row => Transition(row, Unclaimed(row, State.Pending), clock)))
        {
            row.Attempts = 0;
            row.RetryOfRunId = null;
            requeued++;
        }
        return requeued;
    }

    private static async Task<HashSet<string>> CandidatesBoundToOlderRevisionAsync(CollectionAttemptDbContext db, string[] candidateIds,
        string issueRevisionId, CancellationToken cancellationToken)
    {
        var runs = await db.Set<StatementAnalysisRunRow>().AsNoTracking()
            .Where(run => run.Task == StatementGateTask.Task && candidateIds.Contains(run.CandidateId))
            .Select(run => new { run.CandidateId, run.IssueRevisionId, run.StartedAtUtcTicks }).ToArrayAsync(cancellationToken);
        return runs.GroupBy(run => run.CandidateId)
            .Where(group => group.OrderByDescending(run => run.StartedAtUtcTicks).First().IssueRevisionId != issueRevisionId)
            .Select(group => group.Key).ToHashSet(StringComparer.Ordinal);
    }

    private static async Task<HashSet<string>> CandidatesCapturedSinceAsync(CollectionAttemptDbContext db, string[] candidateIds,
        DateOnly since, CancellationToken cancellationToken)
    {
        var sinceTicks = since.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).Ticks;
        var captured = await (from candidate in db.Set<StatementCandidateRow>().AsNoTracking()
                              join attempt in db.Attempts.AsNoTracking() on candidate.AttemptId equals attempt.AttemptId
                              where candidateIds.Contains(candidate.CandidateId) && attempt.ObservedAtUtcTicks >= sinceTicks
                              select candidate.CandidateId).ToArrayAsync(cancellationToken);
        return captured.ToHashSet(StringComparer.Ordinal);
    }

    private static Expression<Func<StatementAnalysisRow, bool>> InView(StatementGateView view) => view switch
    {
        StatementGateView.Pending => row => row.Status == State.Pending || row.Status == State.Running ||
            row.Status == State.RetryWaiting || row.Status == State.WaitingForBudget,
        StatementGateView.Passed => row => row.Status == State.Succeeded && row.ErrorCode == StatementGateReasons.Passed,
        StatementGateView.Skipped => row => row.Status == State.Succeeded && row.ErrorCode == StatementGateReasons.NoTrackedIssue,
        StatementGateView.FailedOpen => row => row.Status == State.Succeeded && row.ErrorCode == StatementGateReasons.FailedOpen,
        StatementGateView.OverCap => row => row.Status == State.Succeeded && row.ErrorCode == StatementGateReasons.OverByteCap,
        _ => row => row.Status == State.Failed || row.Status == State.Blocked
    };

    private static StatementGateView ViewOf(StatementAnalysisRow row) => ViewOf(row.Status, row.ErrorCode);

    private static StatementGateView ViewOf(State status, string? errorCode) => status switch
    {
        State.Succeeded => errorCode switch
        {
            StatementGateReasons.Passed => StatementGateView.Passed,
            StatementGateReasons.NoTrackedIssue => StatementGateView.Skipped,
            StatementGateReasons.FailedOpen => StatementGateView.FailedOpen,
            _ => StatementGateView.OverCap
        },
        State.Failed or State.Blocked => StatementGateView.Failed,
        _ => StatementGateView.Pending
    };

    private static bool Transition(StatementAnalysisRow row, StatementAnalysisCheckpoint update, DatabaseTime clock, TimeSpan? newLease = null)
    {
        if (!StatementAnalysisLifecycle.CanTransition(ToRecord(row), update)) return false;
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

    private static StatementAnalysisCheckpoint Unclaimed(StatementAnalysisRow row, State status) =>
        new(row.CandidateId, row.Task, row.Status, row.LeaseToken, row.Fence, status);

    private static bool IsDue(StatementAnalysisRow row, long now) => row.Status switch
    {
        State.Pending => true,
        State.RetryWaiting or State.WaitingForBudget => row.RetryAt <= now,
        State.Running => row.LeaseExpiresAt <= now,
        _ => false
    };

    private static void ClearReservation(StatementAnalysisRow row)
    {
        row.ReservedTokens = 0;
        row.BudgetDay = null;
        row.ReservedAtUtcTicks = null;
        row.PendingRunId = null;
        row.PendingInputHash = null;
    }

    private static void Interrupt(CollectionAttemptDbContext db, StatementAnalysisRow row, long now)
    {
        row.Attempts++;
        if (row.ReservedTokens == 0) return;
        var run = new AnalysisRun(row.PendingRunId!, row.CandidateId, row.Task, StatementGateTask.TaskVersion, StatementGateTask.Model,
            StatementGateTask.PromptVersion, StatementGateTask.SchemaVersion, row.PendingInputHash!, row.RetryOfRunId,
            AnalysisRunOutcome.Interrupted, AnalysisTokenUsage.None, row.ReservedTokens, null, null, null, [], null,
            new DateTimeOffset(row.ReservedAtUtcTicks!.Value, TimeSpan.Zero), new DateTimeOffset(now, TimeSpan.Zero));
        run.Validate();
        db.Add(ToRow(new StatementGateRun(run, null, null, [])));
        ClearReservation(row);
    }

    private static Task<StatementAnalysisRow?> LockAsync(CollectionAttemptDbContext db, string candidateId,
        CancellationToken cancellationToken) => db.Set<StatementAnalysisRow>().FromSqlInterpolated($"""
            SELECT * FROM statement_analysis
            WHERE candidate_id = {candidateId} AND task = {StatementGateTask.Task} FOR UPDATE
            """).SingleOrDefaultAsync(cancellationToken);

    private static async Task<StatementAnalysisBudgetRow> LockDayAsync(CollectionAttemptDbContext db, DateOnly day,
        CancellationToken cancellationToken)
    {
        _ = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO statement_analysis_budget (day_utc, task, charged_tokens) VALUES ({day}, {StatementGateTask.Task}, 0) ON CONFLICT DO NOTHING
            """, cancellationToken);
        return await db.Set<StatementAnalysisBudgetRow>().FromSqlInterpolated($"""
            SELECT * FROM statement_analysis_budget WHERE day_utc = {day} AND task = {StatementGateTask.Task} FOR UPDATE
            """).SingleAsync(cancellationToken);
    }

    private static StatementAnalysisRecord ToRecord(StatementAnalysisRow row) => new(row.CandidateId, row.Task, row.Status,
        row.LeaseToken, row.Fence, row.Attempts, row.RetryAt is { } retry ? new DateTimeOffset(retry, TimeSpan.Zero) : null,
        row.ErrorCode, row.RetryOfRunId);

    private static StatementAnalysisRunRow ToRow(StatementGateRun gateRun)
    {
        var run = gateRun.Run;
        return new()
        {
            RunId = run.RunId,
            CandidateId = run.ComparisonId,
            Task = run.Task,
            TaskVersion = run.TaskVersion,
            Model = run.Model,
            PromptVersion = run.PromptVersion,
            SchemaVersion = run.SchemaVersion,
            InputHash = run.InputHash,
            IssueRevisionId = gateRun.IssueRevisionId,
            PreviousRunId = run.PreviousRunId,
            Outcome = run.Outcome,
            Reason = gateRun.Reason,
            InputTokens = run.Usage.InputTokens,
            OutputTokens = run.Usage.OutputTokens,
            CacheReadTokens = run.Usage.CacheReadTokens,
            CacheWriteTokens = run.Usage.CacheWriteTokens,
            ChargedTokens = run.ChargedTokens,
            StopReason = run.StopReason,
            ProviderRequestId = run.ProviderRequestId,
            OutputJson = run.OutputJson,
            QuotesJson = gateRun.Quotes.IsEmpty ? null : JsonSerializer.Serialize(gateRun.Quotes),
            ValidationJson = run.ValidationErrors.IsEmpty ? null : JsonSerializer.Serialize(run.ValidationErrors),
            ContextJson = run.ContextJson,
            StartedAtUtcTicks = run.StartedAtUtc.UtcTicks,
            FinishedAtUtcTicks = run.FinishedAtUtc.UtcTicks
        };
    }

    private static StatementGateRun ToRun(StatementAnalysisRunRow row)
    {
        var run = new AnalysisRun(row.RunId, row.CandidateId, row.Task, row.TaskVersion, row.Model, row.PromptVersion,
            row.SchemaVersion, row.InputHash, row.PreviousRunId, row.Outcome,
            new AnalysisTokenUsage(row.InputTokens, row.OutputTokens, row.CacheReadTokens, row.CacheWriteTokens), row.ChargedTokens,
            row.StopReason, row.ProviderRequestId, row.OutputJson,
            row.ValidationJson is null ? [] : [.. JsonSerializer.Deserialize<string[]>(row.ValidationJson)!],
            row.ContextJson, new DateTimeOffset(row.StartedAtUtcTicks, TimeSpan.Zero),
            new DateTimeOffset(row.FinishedAtUtcTicks, TimeSpan.Zero));
        run.Validate();
        return new(run, row.IssueRevisionId, row.Reason,
            row.QuotesJson is null ? [] : [.. JsonSerializer.Deserialize<StatementGateQuote[]>(row.QuotesJson)!]);
    }
}
