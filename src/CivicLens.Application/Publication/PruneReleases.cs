using Microsoft.Extensions.Logging;

namespace CivicLens.Application.Publication;

public sealed class PruneReleases(IPublicationStore publications, IReleaseDirectory releases, ILogger<PruneReleases> logger)
{
    // The publish is done when this runs, so no failure and no shutdown may fail it.
    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        await TryAsync(() => publications.PruneAsync(RetireAsync, CancellationToken.None));
        await TryAsync(() => releases.DeleteRetiredAsync(cancellationToken));
    }

    // The store holds the publication lock, so the served release cannot change while this reads it.
    private async Task RetireAsync(ReleaseRetentionPlan plan, string? activeDirectory, CancellationToken cancellationToken)
    {
        var served = await releases.GetActiveAsync(cancellationToken);
        await releases.RetireAsync(plan, [.. new[] { activeDirectory, served }.OfType<string>()], cancellationToken);
    }

    private async Task TryAsync(Func<Task> step)
    {
        try
        {
            await step();
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Old release files could not be pruned. The next publish tries again.");
        }
    }
}
