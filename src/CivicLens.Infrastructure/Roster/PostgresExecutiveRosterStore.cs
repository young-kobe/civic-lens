using CivicLens.Application.Roster;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Roster.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace CivicLens.Infrastructure.Roster;

/// <summary>Retains one append-only snapshot of the executive file for each capture body not yet seen.</summary>
public sealed class PostgresExecutiveRosterStore(IDbContextFactory<CollectionAttemptDbContext> contextFactory) : IExecutiveRosterStore
{
    public static PostgresExecutiveRosterStore FromConnectionString(string connectionString)
    {
        _ = PostgresCollectionAttemptStore.FromConnectionString(connectionString);
        var options = new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options;
        return new PostgresExecutiveRosterStore(new PooledDbContextFactory<CollectionAttemptDbContext>(options));
    }

    public async Task<bool> HasSnapshotOfCaptureAsync(string attemptId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(attemptId);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await RosterSnapshotRules.HasSnapshotOfCaptureAsync(db, attemptId, cancellationToken);
    }

    public async Task AdmitAsync(string attemptId, IReadOnlyList<ExecutiveOfficial> officials, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(attemptId);
        ArgumentNullException.ThrowIfNull(officials);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        RosterSnapshotRules.AddHeader(db, attemptId);
        foreach (var official in officials)
        {
            db.Add(ToRow(attemptId, official));
            db.AddRange(official.Terms.Select(term => ToRow(attemptId, official.GovtrackId, term)));
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ExecutiveOfficial>> ReadLatestAsync(string sourceId, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        if (await RosterSnapshotRules.ReadLatestSnapshotAttemptIdAsync(db, sourceId, cancellationToken) is not { } snapshotAttemptId)
            return [];
        var officials = await db.Set<ExecutiveOfficialRow>().AsNoTracking().Where(row => row.AttemptId == snapshotAttemptId)
            .OrderBy(row => row.GovtrackId).ToListAsync(cancellationToken);
        var terms = (await db.Set<ExecutiveTermRow>().AsNoTracking().Where(row => row.AttemptId == snapshotAttemptId)
            .OrderBy(row => row.TermIndex).ToListAsync(cancellationToken)).ToLookup(row => row.GovtrackId);
        return [.. officials.Select(official => ToOfficial(official, terms[official.GovtrackId]))];
    }

    private static ExecutiveOfficialRow ToRow(string attemptId, ExecutiveOfficial official) => new()
    {
        AttemptId = attemptId,
        GovtrackId = official.GovtrackId,
        WikidataId = official.WikidataId,
        FullName = official.FullName
    };

    private static ExecutiveTermRow ToRow(string attemptId, int govtrackId, ExecutiveTerm term) => new()
    {
        AttemptId = attemptId,
        GovtrackId = govtrackId,
        TermIndex = term.Index,
        Office = term.Office,
        StartsOn = term.Start,
        EndsOn = term.End,
        Party = term.Party
    };

    private static ExecutiveOfficial ToOfficial(ExecutiveOfficialRow official, IEnumerable<ExecutiveTermRow> terms) =>
        new(official.GovtrackId, official.WikidataId, official.FullName,
            [.. terms.Select(term => new ExecutiveTerm(term.TermIndex, term.Office, term.StartsOn, term.EndsOn, term.Party))]);
}
