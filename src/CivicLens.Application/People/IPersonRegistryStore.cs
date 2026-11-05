using CivicLens.Application.Collection;

namespace CivicLens.Application.People;

public interface IPersonRegistryStore
{
    Task<PeopleImport> ImportAsync(IReadOnlyList<PersonConfiguration> people, string actorSubject, CancellationToken cancellationToken);
}
