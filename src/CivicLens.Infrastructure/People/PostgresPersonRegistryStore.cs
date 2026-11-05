using CivicLens.Application.Collection;
using CivicLens.Application.People;
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
        ArgumentException.ThrowIfNullOrWhiteSpace(actorSubject);
        if (actorSubject.Length > PersonRegistryLimits.ActorSubject)
            throw new ArgumentException($"The actor must be at most {PersonRegistryLimits.ActorSubject} characters.", nameof(actorSubject));
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
        if (person.Website is not null || person.WithholdPhoto)
            db.Add(new PersonSettingRevisionRow
            {
                PersonId = person.Id,
                RevisionNumber = FirstRevision,
                WithholdPhoto = person.WithholdPhoto,
                Website = person.Website,
                Note = ImportNote,
                ActorSubject = actorSubject,
                RecordedAtUtcTicks = now
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
