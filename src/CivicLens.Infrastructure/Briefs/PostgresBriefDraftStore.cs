using System.Collections.Immutable;
using CivicLens.Application;
using CivicLens.Application.Briefs;
using CivicLens.Application.Publication;
using CivicLens.Core.Briefs;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Infrastructure.Briefs.Persistence;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Review;
using Microsoft.EntityFrameworkCore;
using State = CivicLens.Application.Analysis.AnalysisState;

namespace CivicLens.Infrastructure.Briefs;

public sealed class PostgresBriefDraftStore : PostgresAnalysisStore<BriefDraftRun>, IBriefDraftStore
{
    public const string Table = "brief_analysis";

    private const string InsertWeeksSql = $$"""
        INSERT INTO {{Table}} (record_id, task, week_start, status, fence, attempts, reserved_tokens, created_at_utc_ticks)
        SELECT w.record_id, {0}, w.week_start, 'Pending', 0, 0, 0, {{DatabaseClock.TicksSql}} FROM unnest({1}, {2}) AS w(record_id, week_start)
        ON CONFLICT DO NOTHING
        """;

    private const string StartScheduleSql = $$"""
        INSERT INTO brief_schedule (task, first_week_start) VALUES ({0}, {{BriefWeekSql.CurrentWeekStart}}) ON CONFLICT (task) DO NOTHING
        """;

    private const string ReadScheduleSql = $$"""
        SELECT first_week_start AS "FirstWeekStart", {{BriefWeekSql.CurrentWeekStart}} AS "CurrentWeekStart"
        FROM brief_schedule WHERE task = {0}
        """;

    private const string ReadCurrentWeekSql = $$"""
        SELECT {{BriefWeekSql.CurrentWeekStart}} AS "Value"
        """;

    private static readonly string ReadWindowSql = $$"""
        WITH w AS (SELECT {{BriefWeekSql.StartTicks("{0}")}} AS start_ticks, {{BriefWeekSql.EndTicks("{0}")}} AS end_ticks)
        SELECT w.start_ticks AS "StartTicks", w.end_ticks AS "EndTicks",
               (SELECT max(release_number) FROM publication_releases WHERE published_at_utc_ticks < w.end_ticks) AS "CutoffReleaseNumber",
               (SELECT max(release_number) FROM publication_releases WHERE published_at_utc_ticks < w.start_ticks) AS "PreviousReleaseNumber"
        FROM w
        """;

    private static readonly string ReadFirstPublishedInWeekSql = $$"""
        WITH {{BriefFirstRelease.WantedFirstPublishedCtes}}
        SELECT f.kind AS "Kind", f.record_id AS "RecordId" FROM first_published f WHERE f.ticks >= {2} AND f.ticks < {3}
        """;

    private static readonly State[] RedraftableStates = [State.Succeeded, State.Blocked, State.Failed];

    private readonly IDbContextFactory<CollectionAttemptDbContext> contextFactory;
    private readonly PostgresAnalysisQueue<BriefAnalysisRow, BriefAnalysisBudgetRow, BriefDraftRun> queue;

    public PostgresBriefDraftStore(IDbContextFactory<CollectionAttemptDbContext> contextFactory)
        : this(contextFactory, new(contextFactory, new BriefDraftQueueMapping())) { }

    private PostgresBriefDraftStore(IDbContextFactory<CollectionAttemptDbContext> contextFactory,
        PostgresAnalysisQueue<BriefAnalysisRow, BriefAnalysisBudgetRow, BriefDraftRun> queue) : base(queue)
    {
        this.contextFactory = contextFactory;
        this.queue = queue;
    }

    public static IWorkerWakeup Wakeup(string connectionString) =>
        PostgresAnalysisWakeup.FromConnectionString(connectionString, Table, BriefDraftTask.Task, BriefWeekSql.NextBoundaryTicks);

    public async Task<BriefDraftRun?> GetRunAsync(string runId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.Set<BriefAnalysisRunRow>().AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.RunId == runId && candidate.Task == BriefDraftTask.Task, cancellationToken);
        return row is null ? null : new(AnalysisRunRows.ToRun(row), row.Reason);
    }

    public async Task<BriefDraftEvidence?> ReadEvidenceAsync(string recordId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recordId);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await PostgresAdvisoryLock.BeginWriteAsync(db, PostgresAdvisoryLock.BriefReview, cancellationToken);
        var weekStart = await db.Set<BriefAnalysisRow>().AsNoTracking()
            .Where(row => row.RecordId == recordId && row.Task == BriefDraftTask.Task)
            .Select(row => (DateOnly?)row.WeekStart).SingleOrDefaultAsync(cancellationToken);
        if (weekStart is null) return null;
        var bounds = await db.Database.SqlQueryRaw<WindowRow>(ReadWindowSql, weekStart.Value).SingleAsync(cancellationToken);
        var window = new BriefWindow(weekStart.Value, bounds.StartTicks, bounds.EndTicks, (bounds.PreviousReleaseNumber ?? 0) + 1,
            bounds.CutoffReleaseNumber ?? 0);
        var weekRecords = await ReadNewRecordsAsync(db, bounds, cancellationToken);
        var records = await BriefRecordReader.ReadAsync(db, await AtActiveRevisionsAsync(db, weekRecords, cancellationToken), cancellationToken);
        var current = await BriefDraftRows.ReadCurrentAsync(db, recordId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(window, records, weekRecords.Length - records.Length, current);
    }

    public async Task EnqueueClosedWeeksAsync(CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        _ = await db.Database.ExecuteSqlRawAsync(StartScheduleSql, [BriefDraftTask.Task], cancellationToken);
        var schedule = await db.Database.SqlQueryRaw<ScheduleRow>(ReadScheduleSql, BriefDraftTask.Task).SingleAsync(cancellationToken);
        var weeks = BriefWeek.ClosedWeeks(schedule.FirstWeekStart, schedule.CurrentWeekStart).ToArray();
        if (weeks.Length > 0) _ = await InsertWeeksAsync(db, weeks, cancellationToken);
    }

    public async Task<int> RedraftAsync(DateOnly weekStart, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var currentWeekStart = await db.Database.SqlQueryRaw<DateOnly>(ReadCurrentWeekSql).SingleAsync(cancellationToken);
        if (weekStart >= currentWeekStart) throw new ArgumentException("Only a week that has ended can be redrafted.", nameof(weekStart));
        var requeued = await InsertWeeksAsync(db, [weekStart], cancellationToken);
        var recordId = BriefWeek.RecordIdOf(weekStart);
        foreach (var state in RedraftableStates)
            requeued += await queue.RequeueAsync(state, null, recordId, null, cancellationToken);
        return requeued;
    }

    private static async Task<ImmutableArray<PublishedRevisionBinding>> ReadNewRecordsAsync(CollectionAttemptDbContext db, WindowRow bounds,
        CancellationToken cancellationToken)
    {
        if (bounds.CutoffReleaseNumber is not { } cutoff || cutoff == bounds.PreviousReleaseNumber) return [];
        var published = await ReviewQueries.ReadReleaseRecordsAsync(db, cutoff, cancellationToken);
        var firstPublishedInWeek = (await db.Database.SqlQueryRaw<KindRecordRow>(ReadFirstPublishedInWeekSql,
            published.Select(binding => binding.Kind.ToString()).ToArray(), published.Select(binding => binding.RecordId).ToArray(),
            bounds.StartTicks, bounds.EndTicks).ToArrayAsync(cancellationToken)).Select(row => (row.Kind, row.RecordId)).ToHashSet();
        return [.. published.Where(binding => firstPublishedInWeek.Contains((binding.Kind.ToString(), binding.RecordId)))];
    }

    private static async Task<ImmutableArray<PublishedRevisionBinding>> AtActiveRevisionsAsync(CollectionAttemptDbContext db,
        ImmutableArray<PublishedRevisionBinding> weekRecords, CancellationToken cancellationToken)
    {
        if (weekRecords.IsEmpty) return [];
        var active = (await ReviewQueries.ReadActiveReleaseRecordsAsync(db, cancellationToken))
            .ToDictionary(binding => (binding.Kind, binding.RecordId));
        return [.. weekRecords.Where(binding => active.ContainsKey((binding.Kind, binding.RecordId)))
            .Select(binding => active[(binding.Kind, binding.RecordId)])];
    }

    private static Task<int> InsertWeeksAsync(CollectionAttemptDbContext db, DateOnly[] weeks, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlRawAsync(InsertWeeksSql, [BriefDraftTask.Task, weeks.Select(BriefWeek.RecordIdOf).ToArray(), weeks],
            cancellationToken);

    private sealed class WindowRow
    {
        public long StartTicks { get; set; }
        public long EndTicks { get; set; }
        public int? CutoffReleaseNumber { get; set; }
        public int? PreviousReleaseNumber { get; set; }
    }

    private sealed class KindRecordRow
    {
        public string Kind { get; set; } = "";
        public string RecordId { get; set; } = "";
    }

    private sealed class ScheduleRow
    {
        public DateOnly FirstWeekStart { get; set; }
        public DateOnly CurrentWeekStart { get; set; }
    }
}
