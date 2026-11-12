using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Collection.FederalRegister;

/// <summary>PendingJobs above 0 means the run was refused and nothing was examined. Refetches counts the detail refetch jobs admitted
/// for originals with no pinned body URL. The refetch admits the text job when it fills the URL. NoUrlWithRefetch counts the originals
/// that have no body URL and have a refetch job in any state. A count that stays above 0 after the refetch jobs end means the origin gave no URL or a job failed.</summary>
public sealed record FederalRegisterTextBackfillResult(int PendingJobs, int Examined, int Admitted, int Refetches, int NoUrlWithRefetch);

/// <summary>
/// Admits a text job for original documents that were recorded before the text source existed. The jobs carry the revision
/// of the current configuration, because the document jobs hold a revision with no enabled text source.
/// An original with no pinned body URL gets a refetch job of its detail instead.
/// A text job costs no model budget. The drain guard follows <see cref="Bills.BackfillBillText"/>.
/// </summary>
public sealed class BackfillFederalRegisterText(IFederalRegisterTextBackfillStore store, CollectionConfigurationRevision revision)
{
    public async Task<FederalRegisterTextBackfillResult> ExecuteAsync(ReviewActor actor, int limit, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireOwner(actor);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        var textSource = EnabledTextSource();
        if (await store.CountPendingJobsAsync(textSource, cancellationToken) is var pending and > 0) return new(pending, 0, 0, 0, 0);
        var listing = await store.ListNewestOriginalsWithoutTextAsync(textSource, limit, cancellationToken);
        var admitted = 0;
        foreach (var target in listing.Targets)
            if (await store.AdmitTextJobAsync(textSource, target, cancellationToken)) admitted++;
        var refetches = 0;
        foreach (var target in listing.Unpinned)
            if (await store.AdmitBodyUrlJobAsync(textSource, target, cancellationToken)) refetches++;
        return new(0, listing.Targets.Count, admitted, refetches, listing.NoUrlWithRefetch);
    }

    private ConfiguredCollectionSource EnabledTextSource() =>
        revision.Configuration.SourceOf(ApiParser.FederalRegisterText) is { Enabled: true } source
            ? new ConfiguredCollectionSource(revision, source.Id)
            : throw new ArgumentException("The collection configuration has no enabled Federal Register text source.");
}
