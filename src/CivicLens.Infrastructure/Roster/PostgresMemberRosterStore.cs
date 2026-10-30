using CivicLens.Application.Collection;
using CivicLens.Application.Roster;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Collection.Jobs;
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
        return await RosterSnapshotRules.HasSnapshotOfCaptureAsync(db, attemptId, cancellationToken);
    }

    public async Task AdmitAsync(string attemptId, IReadOnlyList<RosterMember> members, ConfiguredCollectionSource? photos,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(attemptId);
        ArgumentNullException.ThrowIfNull(members);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = photos is null
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : await PostgresAdvisoryLock.BeginWriteAsync(db, PostgresAdvisoryLock.CollectionJobs, cancellationToken);
        if (photos is not null) await AddPhotoJobsAsync(db, photos, cancellationToken);
        RosterSnapshotRules.AddHeader(db, attemptId);
        foreach (var member in members)
        {
            db.Add(ToRow(attemptId, member));
            db.AddRange(member.Terms.Select(term => ToRow(attemptId, member.BioguideId, term)));
        }
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task AdmitPhotoJobsAsync(ConfiguredCollectionSource photos, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(photos);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await PostgresAdvisoryLock.BeginWriteAsync(db, PostgresAdvisoryLock.CollectionJobs, cancellationToken);
        await AddPhotoJobsAsync(db, photos, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task AdmitPhotoAsync(MemberPhoto photo, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(photo);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await PostgresAdvisoryLock.BeginWriteAsync(db, PostgresAdvisoryLock.CollectionJobs, cancellationToken);
        var captureSha256 = (await db.Attempts.AsNoTracking().SingleOrDefaultAsync(attempt => attempt.AttemptId == photo.AttemptId, cancellationToken))
            ?.CaptureSha256 ?? throw new InvalidOperationException("A member photo needs an imported captured attempt.");
        var newestCaptureSha256 = await NewestPhotosFirst(db, [photo.PersonId]).Select(newest => newest.CaptureSha256)
            .FirstOrDefaultAsync(cancellationToken);
        if (newestCaptureSha256 == captureSha256 ||
            await db.Set<OfficialPhotoRow>().AnyAsync(row => row.AttemptId == photo.AttemptId, cancellationToken))
            return;
        db.Add(new OfficialPhotoRow { AttemptId = photo.AttemptId, PersonId = photo.PersonId, SourceUrl = photo.SourceUrl, Credit = photo.Credit });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<OfficialPhoto>> ReadNewestPhotosAsync(IReadOnlyCollection<string> personIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(personIds);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var newestFirst = await NewestPhotosFirst(db, personIds).ToListAsync(cancellationToken);
        return [.. newestFirst.DistinctBy(photo => photo.PersonId, StringComparer.Ordinal)];
    }

    private static IQueryable<OfficialPhoto> NewestPhotosFirst(CollectionAttemptDbContext db, IReadOnlyCollection<string> personIds) =>
        from row in db.Set<OfficialPhotoRow>().AsNoTracking()
        where personIds.Contains(row.PersonId)
        join attempt in db.Attempts.AsNoTracking() on row.AttemptId equals attempt.AttemptId
        where attempt.CaptureSha256 != null
        orderby attempt.ObservedAtUtcTicks descending, attempt.AttemptId descending
        select new OfficialPhoto(row.PersonId, row.SourceUrl, row.Credit, row.AttemptId, attempt.CaptureSha256!);

    public async Task<IReadOnlyList<RosterMember>> ReadLatestAsync(string sourceId, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        if (await RosterSnapshotRules.ReadLatestSnapshotAttemptIdAsync(db, sourceId, cancellationToken) is not { } snapshotAttemptId)
            return [];
        var members = await db.Set<RosterMemberRow>().AsNoTracking().Where(row => row.AttemptId == snapshotAttemptId)
            .OrderBy(row => row.BioguideId).ToListAsync(cancellationToken);
        var terms = (await db.Set<RosterTermRow>().AsNoTracking().Where(row => row.AttemptId == snapshotAttemptId)
            .OrderBy(row => row.TermIndex).ToListAsync(cancellationToken)).ToLookup(row => row.BioguideId);
        return [.. members.Select(member => ToMember(member, terms[member.BioguideId]))];
    }

    private static async Task AddPhotoJobsAsync(CollectionAttemptDbContext db, ConfiguredCollectionSource photos,
        CancellationToken cancellationToken)
    {
        var now = (await DatabaseClock.ReadAsync(db, cancellationToken)).Offset;
        var batch = photos.CreateMemberPhotoBatch(DateOnly.FromDateTime(now.UtcDateTime));
        var (result, _) = await PostgresCollectionJobStore.AdmitApiJobsAsync(db, batch, int.MaxValue, now, cancellationToken);
        if (result.DeferredCount > 0)
            throw new InvalidDataException($"Source '{photos.SourceId}' admission policy cannot hold every member photo job.");
    }

    private static RosterMemberRow ToRow(string attemptId, RosterMember member) => new()
    {
        AttemptId = attemptId,
        BioguideId = member.BioguideId,
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
        Party = term.Party,
        Caucus = term.Caucus
    };

    private static RosterMember ToMember(RosterMemberRow member, IEnumerable<RosterTermRow> terms) =>
        new(member.BioguideId, member.FullName, member.Office, member.Phone, member.Url,
            [.. terms.Select(term => new RosterTerm(term.TermIndex, term.Chamber, term.StartsOn, term.EndsOn, term.State,
                term.District, term.SenateClass, term.Party, term.Caucus))]);
}
