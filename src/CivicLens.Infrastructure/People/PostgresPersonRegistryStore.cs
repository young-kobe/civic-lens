using CivicLens.Application.People;
using CivicLens.Core.Review;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.People.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace CivicLens.Infrastructure.People;

/// <summary>Keeps people and their owner revisions. Rows are only inserted, never updated or deleted.</summary>
public sealed class PostgresPersonRegistryStore(IDbContextFactory<CollectionAttemptDbContext> contextFactory) : IPersonRegistryStore
{
    public static PostgresPersonRegistryStore FromConnectionString(string connectionString)
    {
        _ = PostgresCollectionAttemptStore.FromConnectionString(connectionString);
        var options = new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options;
        return new PostgresPersonRegistryStore(new PooledDbContextFactory<CollectionAttemptDbContext>(options));
    }

    public async Task<PersonSettingRevision> AppendSettingAsync(string personId, Func<PersonSetting, PersonSetting> change, string note,
        string actorSubject, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(personId);
        ArgumentNullException.ThrowIfNull(change);
        if (!ReviewAuthor.IsValidSubject(actorSubject))
            throw new ArgumentException($"The actor must have 1 to {ReviewAuthor.MaximumSubjectLength} characters.", nameof(actorSubject));

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (!await db.Set<PersonRow>().AnyAsync(row => row.PersonId == personId, cancellationToken))
            throw new ArgumentException($"Person '{personId}' is unknown. A member has a row after a roster snapshot lists them.", nameof(personId));
        var newest = await db.Set<PersonSettingRevisionRow>().AsNoTracking().Where(row => row.PersonId == personId)
            .OrderByDescending(row => row.RevisionNumber).FirstOrDefaultAsync(cancellationToken);
        var setting = change(newest is null ? PersonSetting.None : new PersonSetting(newest.WithholdPhoto, newest.Website));
        var row = new PersonSettingRevisionRow
        {
            PersonId = personId,
            RevisionNumber = (newest?.RevisionNumber ?? 0) + 1,
            WithholdPhoto = setting.WithholdPhoto,
            Website = setting.Website,
            Note = note,
            ActorSubject = actorSubject,
            RecordedAtUtcTicks = (await DatabaseClock.ReadAsync(db, cancellationToken)).Ticks
        };
        db.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new PersonSettingRevision(personId, row.RevisionNumber, row.WithholdPhoto, row.Website);
    }

    public async Task<IReadOnlyDictionary<string, PersonSetting>> ReadNewestSettingsAsync(IReadOnlyCollection<string> personIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(personIds);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var revisions = db.Set<PersonSettingRevisionRow>().AsNoTracking();
        var newest = await revisions
            .Where(row => personIds.Contains(row.PersonId) &&
                row.RevisionNumber == revisions.Where(other => other.PersonId == row.PersonId).Max(other => other.RevisionNumber))
            .ToListAsync(cancellationToken);
        return newest.ToDictionary(row => row.PersonId, row => new PersonSetting(row.WithholdPhoto, row.Website), StringComparer.Ordinal);
    }

    public async Task<IReadOnlyList<PersonKey>> ReadKeysAsync(CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Set<PersonRow>().AsNoTracking().OrderBy(row => row.PersonId)
            .Select(row => new PersonKey(row.PersonId, row.BioguideId)).ToListAsync(cancellationToken);
    }
}
