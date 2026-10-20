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
        return await RosterSnapshots.HasSnapshotOfCaptureAsync(db, attemptId, cancellationToken);
    }

    public async Task AdmitAsync(string attemptId, IReadOnlyList<ExecutiveOfficial> officials, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(attemptId);
        ArgumentNullException.ThrowIfNull(officials);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        RosterSnapshots.AddHeader(db, attemptId);
        foreach (var official in officials)
        {
            db.Add(ToRow(attemptId, official));
            db.AddRange(official.Terms.Select(term => ToRow(attemptId, official.GovtrackId, term)));
        }
        await db.SaveChangesAsync(cancellationToken);
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
}
