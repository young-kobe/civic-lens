using CivicLens.Application.Collection;
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
    private const int FirstRevision = 1;
    private const string ImportNote = "Imported from the collection configuration.";

    public static PostgresPersonRegistryStore FromConnectionString(string connectionString)
    {
        _ = PostgresCollectionAttemptStore.FromConnectionString(connectionString);
        var options = new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options;
        return new PostgresPersonRegistryStore(new PooledDbContextFactory<CollectionAttemptDbContext>(options));
    }

    public async Task<PeopleImport> ImportAsync(IReadOnlyList<PersonConfiguration> people, string actorSubject, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(people);
        if (!ReviewAuthor.IsValidSubject(actorSubject))
            throw new ArgumentException($"The actor must have 1 to {ReviewAuthor.MaximumSubjectLength} characters.", nameof(actorSubject));
        foreach (var office in people.SelectMany(person => person.Offices ?? []))
            if (office.Kind is null)
                throw new ArgumentException("Every office needs a kind.", nameof(people));

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var known = await db.Set<PersonRow>().AsNoTracking().ToListAsync(cancellationToken);
        var knownIds = known.Select(row => row.PersonId).ToHashSet(StringComparer.Ordinal);
        var added = people.Where(person => !knownIds.Contains(person.Id)).ToArray();
        RefuseKeysOfOtherPeople(added, known);
        var now = (await DatabaseClock.ReadAsync(db, cancellationToken)).Ticks;
        foreach (var person in added)
            AddPerson(db, person, actorSubject, now);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new PeopleImport(added.Length, people.Count - added.Length);
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
            throw new ArgumentException($"Person '{personId}' is unknown. Run people import first.", nameof(personId));
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

    private static void RefuseKeysOfOtherPeople(IEnumerable<PersonConfiguration> added, IReadOnlyList<PersonRow> known)
    {
        var bioguideIds = known.Where(row => row.BioguideId is not null).Select(row => row.BioguideId!).ToHashSet(StringComparer.Ordinal);
        var wikidataIds = known.Where(row => row.WikidataId is not null).Select(row => row.WikidataId!).ToHashSet(StringComparer.Ordinal);
        foreach (var person in added)
        {
            if (person.BioguideId is not null && bioguideIds.Contains(person.BioguideId))
                throw new ArgumentException($"Bioguide ID '{person.BioguideId}' belongs to another person.");
            if (person.WikidataId is not null && wikidataIds.Contains(person.WikidataId))
                throw new ArgumentException($"Wikidata ID '{person.WikidataId}' belongs to another person.");
        }
    }

    private static void AddPerson(CollectionAttemptDbContext db, PersonConfiguration person, string actorSubject, long now)
    {
        db.Add(new PersonRow
        {
            PersonId = person.Id,
            BioguideId = person.BioguideId,
            WikidataId = person.WikidataId,
            CreatedAtUtcTicks = now,
            CreatedByActor = actorSubject
        });
        if (person.Offices is { Length: > 0 } offices)
            db.Add(new PersonOfficeRevisionRow
            {
                PersonId = person.Id,
                RevisionNumber = FirstRevision,
                Note = ImportNote,
                ActorSubject = actorSubject,
                RecordedAtUtcTicks = now,
                Terms = [.. offices.Select((office, position) => ToRow(person.Id, position, office))]
            });
    }

    private static PersonOfficeRevisionTermRow ToRow(string personId, int position, OfficeConfiguration office) => new()
    {
        PersonId = personId,
        RevisionNumber = FirstRevision,
        Position = position,
        Title = office.Title,
        Kind = office.Kind!.Value,
        StartsOn = office.StartsOn,
        EndsOn = office.EndsOn,
        Party = office.Party
    };
}
