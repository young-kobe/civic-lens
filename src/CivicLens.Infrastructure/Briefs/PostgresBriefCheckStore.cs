using System.Collections.Immutable;
using CivicLens.Application;
using CivicLens.Application.Briefs;
using CivicLens.Core.Analysis;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Infrastructure.Briefs.Persistence;
using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Briefs;

public sealed class PostgresBriefCheckStore : PostgresAnalysisStore<BriefCheckRun>, IBriefCheckStore
{
    private readonly IDbContextFactory<CollectionAttemptDbContext> contextFactory;
    private readonly PostgresBriefDraftStore drafts;

    public PostgresBriefCheckStore(IDbContextFactory<CollectionAttemptDbContext> contextFactory)
        : base(BriefCheckQueue.Create(contextFactory))
    {
        this.contextFactory = contextFactory;
        drafts = new(contextFactory);
    }

    public static IWorkerWakeup Wakeup(string connectionString) =>
        PostgresAnalysisWakeup.FromConnectionString(connectionString, PostgresBriefDraftStore.Table, BriefCheckTask.Task);

    public async Task<BriefCheckRun?> GetRunAsync(string runId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.Set<BriefAnalysisRunRow>().AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.RunId == runId && candidate.Task == BriefCheckTask.Task, cancellationToken);
        return row is null ? null : new(AnalysisRunRows.ToRun(row), row.Reason, null);
    }

    public async Task<BriefCheckEvidence?> ReadEvidenceAsync(string recordId, CancellationToken cancellationToken)
    {
        var week = await drafts.ReadEvidenceAsync(recordId, cancellationToken);
        if (week?.Current is not { } revision) return null;
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var bindings = revision.Citations.Select(BriefRecordReader.BindingOf).Distinct().ToImmutableArray();
        var records = await BriefRecordReader.ReadExactAsync(db, bindings, cancellationToken);
        var lastInputHash = await db.Set<BriefAnalysisRunRow>().AsNoTracking()
            .Where(row => row.RecordId == recordId && row.Task == BriefCheckTask.Task && row.Outcome == AnalysisRunOutcome.Drafted)
            .OrderByDescending(row => row.StartedAtUtcTicks).Select(row => row.InputHash).FirstOrDefaultAsync(cancellationToken);
        var cited = revision.Citations.ToImmutableDictionary(citation => citation.Id, citation => BriefRecordReader.RecordOf(citation, records),
            StringComparer.Ordinal);
        var notCited = week.Records.Where(record => !revision.Citations.Any(citation =>
            citation.Kind == record.Kind && citation.RecordId == record.RecordId)).ToImmutableArray();
        return new(revision, cited, notCited, lastInputHash);
    }
}
