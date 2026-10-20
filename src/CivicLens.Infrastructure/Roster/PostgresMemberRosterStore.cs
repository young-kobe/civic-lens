using CivicLens.Application.Roster;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Collection.Persistence;
using CivicLens.Infrastructure.Roster.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace CivicLens.Infrastructure.Roster;

/// <summary>Retains one append-only snapshot of the member roster for each capture body not yet seen.</summary>
public sealed class PostgresMemberRosterStore(IDbContextFactory<CollectionAttemptDbContext> contextFactory) : IMemberRosterStore
{
    public static PostgresMemberRosterStore FromConnectionString(string connectionString)
    {
        _ = PostgresCollectionAttemptStore.FromConnectionString(connectionString);
        var options = new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options;
        return new PostgresMemberRosterStore(new PooledDbContextFactory<CollectionAttemptDbContext>(options));
    }

    public async Task<bool> HasSnapshotOfCaptureAsync(string attemptId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(attemptId);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var attempt = await db.Attempts.AsNoTracking().SingleOrDefaultAsync(row => row.AttemptId == attemptId, cancellationToken);
        if (attempt?.CaptureSha256 is null)
            throw new InvalidOperationException("A roster snapshot needs an imported captured attempt.");
        return await SnapshotAttempts(db, attempt.SourceId).AnyAsync(snapshot => snapshot.CaptureSha256 == attempt.CaptureSha256,
            cancellationToken);
    }

    public async Task AdmitAsync(string attemptId, IReadOnlyList<RosterMember> members, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(attemptId);
        ArgumentNullException.ThrowIfNull(members);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var attempt = await db.Attempts.AsNoTracking().SingleAsync(row => row.AttemptId == attemptId, cancellationToken);
        db.Add(new RosterSnapshotRow { AttemptId = attemptId, SourceId = attempt.SourceId });
        foreach (var member in members)
        {
            db.Add(ToRow(attemptId, member));
            db.AddRange(member.Terms.Select(term => ToRow(attemptId, member.BioguideId, term)));
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RosterMember>> ReadLatestAsync(string sourceId, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var snapshots = SnapshotAttempts(db, sourceId);
        var newestCapture = await db.Attempts.AsNoTracking()
            .Where(attempt => attempt.SourceId == sourceId && attempt.CaptureSha256 != null &&
                snapshots.Any(snapshot => snapshot.CaptureSha256 == attempt.CaptureSha256))
            .OrderByDescending(attempt => attempt.ObservedAtUtcTicks).ThenByDescending(attempt => attempt.AttemptId)
            .FirstOrDefaultAsync(cancellationToken);
        if (newestCapture is null) return [];
        var snapshot = await snapshots.Where(candidate => candidate.CaptureSha256 == newestCapture.CaptureSha256)
            .OrderBy(candidate => candidate.ObservedAtUtcTicks).ThenBy(candidate => candidate.AttemptId)
            .FirstAsync(cancellationToken);
        var members = await db.Set<RosterMemberRow>().AsNoTracking().Where(row => row.AttemptId == snapshot.AttemptId)
            .OrderBy(row => row.BioguideId).ToListAsync(cancellationToken);
        var terms = (await db.Set<RosterTermRow>().AsNoTracking().Where(row => row.AttemptId == snapshot.AttemptId)
            .OrderBy(row => row.TermIndex).ToListAsync(cancellationToken)).ToLookup(row => row.BioguideId);
        return [.. members.Select(member => ToMember(member, terms[member.BioguideId]))];
    }

    private static IQueryable<AttemptRow> SnapshotAttempts(CollectionAttemptDbContext db, string sourceId) =>
        from snapshot in db.Set<RosterSnapshotRow>().AsNoTracking()
        where snapshot.SourceId == sourceId
        join attempt in db.Attempts.AsNoTracking() on snapshot.AttemptId equals attempt.AttemptId
        select attempt;

    private static RosterMemberRow ToRow(string attemptId, RosterMember member) => new()
    {
        AttemptId = attemptId,
        BioguideId = member.BioguideId,
        LisId = member.LisId,
        FullName = member.FullName,
        Office = member.Office,
        Phone = member.Phone,
        Url = member.Url
    };

    private static RosterTermRow ToRow(string attemptId, string bioguideId, RosterTerm term) => new()
    {
        AttemptId = attemptId,
        BioguideId = bioguideId,
        TermIndex = term.Index,
        Chamber = term.Chamber,
        StartsOn = term.Start,
        EndsOn = term.End,
        State = term.State,
        District = term.District,
        SenateClass = term.SenateClass,
        Party = term.Party
    };

    private static RosterMember ToMember(RosterMemberRow member, IEnumerable<RosterTermRow> terms) =>
        new(member.BioguideId, member.LisId, member.FullName, member.Office, member.Phone, member.Url,
            [.. terms.Select(term => new RosterTerm(term.TermIndex, term.Chamber, term.StartsOn, term.EndsOn, term.State,
                term.District, term.SenateClass, term.Party))]);
}
