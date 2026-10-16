using System.Collections.Immutable;

namespace CivicLens.Application.Publication;

internal sealed record PublicationBuild(PreviousRelease Previous, DateTimeOffset Now, PublishedRecordRules Rules,
    IReleaseRenderer Renderer)
{
    public ImmutableArray<PublishedRevisionBinding> ReleaseRecords { get; private init; } =
        [.. Previous.Entries.Select(PublishedRevisionBinding.Of)];

    public PublicationBuild WithAdded(IEnumerable<PendingRecord> added)
    {
        var replacements = added.Select(record => PublishedRevisionBinding.Of(record.Entry)).ToArray();
        var replaced = replacements.Select(binding => (binding.Kind, binding.RecordId)).ToHashSet();
        return this with
        {
            ReleaseRecords = [.. ReleaseRecords.Where(binding => !replaced.Contains((binding.Kind, binding.RecordId))), .. replacements]
        };
    }
}
