using System.Collections.Immutable;

namespace CivicLens.Application.Publication;

internal sealed record PublicationBuild(PreviousRelease Previous, DateTimeOffset Now, PublishedRecordRules Rules,
    IReleaseRenderer Renderer, PublicationCatalog Catalog, PublicationCorrections Corrections, PublicationOfficials Officials)
{
    public ImmutableArray<PublishedRevisionBinding> ReleaseRecords { get; private init; } =
        [.. Previous.Entries.Select(PublishedRevisionBinding.Of)];

    public ImmutableArray<PublishedRecordReference> Removed { get; private init; } = [];

    public ImmutableArray<PublishedRecordReference> Returned { get; private init; } = [];

    public PublicationBuild WithReturned(ImmutableArray<PublishedRecordReference> returned) => this with { Returned = returned };

    public PublicationBuild WithAdded(IEnumerable<PendingRecord> added)
    {
        var replacements = added.Select(record => PublishedRevisionBinding.Of(record.Entry)).ToArray();
        var replaced = replacements.Select(binding => (binding.Kind, binding.RecordId)).ToHashSet();
        return this with
        {
            ReleaseRecords = [.. ReleaseRecords.Where(binding => !replaced.Contains((binding.Kind, binding.RecordId))), .. replacements]
        };
    }

    public PublicationBuild WithRemoved(ImmutableArray<PublishedRecordReference> removed)
    {
        var gone = removed.ToHashSet();
        return this with
        {
            ReleaseRecords = [.. ReleaseRecords.Where(binding => !gone.Contains(new(binding.Kind, binding.RecordId)))],
            Removed = removed
        };
    }
}
