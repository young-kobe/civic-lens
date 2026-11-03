using CivicLens.Application.Districts;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Districts.Persistence;
using CivicLens.Infrastructure.Roster;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace CivicLens.Infrastructure.Districts;

public sealed class PostgresZctaDistrictStore(IDbContextFactory<CollectionAttemptDbContext> contextFactory) : IZctaDistrictStore
{
    public static PostgresZctaDistrictStore FromConnectionString(string connectionString)
    {
        _ = PostgresCollectionAttemptStore.FromConnectionString(connectionString);
        var options = new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options;
        return new PostgresZctaDistrictStore(new PooledDbContextFactory<CollectionAttemptDbContext>(options));
    }

    public async Task<bool> HasSnapshotOfCaptureAsync(string attemptId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(attemptId);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await RosterSnapshotRules.HasSnapshotOfCaptureAsync(db, attemptId, cancellationToken);
    }

    public async Task AdmitAsync(string attemptId, IReadOnlyList<ZctaDistrict> rows, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(attemptId);
        ArgumentNullException.ThrowIfNull(rows);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        RosterSnapshotRules.AddHeader(db, attemptId);
        db.AddRange(rows.Select(row => new ZctaDistrictRow
        {
            AttemptId = attemptId,
            Zcta = row.Zcta,
            State = row.State,
            District = row.District,
            LandAreaSquareMeters = row.LandAreaSquareMeters
        }));
        await db.SaveChangesAsync(cancellationToken);
    }
}
