using CivicLens.Application.Collection;

namespace CivicLens.Application.Collection.Jobs;

/// <summary>Synchronizes configured source schedules and admits bounded due source checks.</summary>
public interface ICollectionScheduleStore
{
    /// <summary>Reconciles persisted schedules with the active configuration; call once at worker startup.</summary>
    Task SynchronizeAsync(CollectionConfiguration configuration, CancellationToken cancellationToken);

    /// <summary>Advances at most <paramref name="limit"/> due source schedules, admitting eligible checks. Returns the number advanced, including skipped slots.</summary>
    Task<int> AdvanceDueAsync(int limit, CancellationToken cancellationToken);
}
