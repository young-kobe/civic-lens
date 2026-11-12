using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Collection.FederalRegister;

/// <summary>PendingJobs above 0 means the run was refused and nothing was examined. Unpinned counts the originals that need a detail
/// refetch before they can get a text job.</summary>
public sealed record FederalRegisterTextBackfillResult(int PendingJobs, int Examined, int Admitted, int Unpinned);

/// <summary>
/// Admits a text job for original documents that were recorded before the text source existed. The jobs carry the revision
/// of the current configuration, because the document jobs hold a revision with no enabled text source.
/// A text job costs no model budget. The drain guard follows <see cref="Bills.BackfillBillText"/>.
/// </summary>
public sealed class BackfillFederalRegisterText(IFederalRegisterTextBackfillStore store, CollectionConfigurationRevision revision)
{
    public async Task<FederalRegisterTextBackfillResult> ExecuteAsync(ReviewActor actor, int limit, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireOwner(actor);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        var textSource = EnabledTextSource();
        if (await store.CountPendingJobsAsync(textSource, cancellationToken) is var pending and > 0) return new(pending, 0, 0, 0);
        var listing = await store.ListNewestOriginalsWithoutTextAsync(textSource, limit, cancellationToken);
        var admitted = 0;
        foreach (var target in listing.Targets)
            if (await store.AdmitTextJobAsync(textSource, target, cancellationToken)) admitted++;
        return new(0, listing.Targets.Count, admitted, listing.Unpinned);
    }

    private ConfiguredCollectionSource EnabledTextSource() =>
        revision.Configuration.SourceOf(ApiParser.FederalRegisterText) is { Enabled: true } source
            ? new ConfiguredCollectionSource(revision, source.Id)
            : throw new ArgumentException("The collection configuration has no enabled Federal Register text source.");
}
