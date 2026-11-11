using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Collection.FederalRegister;

public sealed record FederalRegisterTextBackfillResult(int Examined, int Admitted);

/// <summary>
/// Admits a text job for original documents that were recorded before the text source existed. The jobs carry the revision
/// of the current configuration, because the document jobs hold a revision with no enabled text source.
/// A text job costs no model budget.
/// </summary>
public sealed class BackfillFederalRegisterText(IFederalRegisterTextBackfillStore store, CollectionConfigurationRevision revision)
{
    public async Task<FederalRegisterTextBackfillResult> ExecuteAsync(ReviewActor actor, int limit, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireOwner(actor);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        var textSource = EnabledTextSource();
        var targets = await store.ListNewestOriginalsWithoutTextAsync(textSource, limit, cancellationToken);
        var admitted = 0;
        foreach (var target in targets)
            if (await store.AdmitTextJobAsync(textSource, target, cancellationToken)) admitted++;
        return new FederalRegisterTextBackfillResult(targets.Count, admitted);
    }

    private ConfiguredCollectionSource EnabledTextSource() =>
        revision.Configuration.SourceOf(ApiParser.FederalRegisterText) is { Enabled: true } source
            ? new ConfiguredCollectionSource(revision, source.Id)
            : throw new ArgumentException("The collection configuration has no enabled Federal Register text source.");
}
