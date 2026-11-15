using System.Collections.Immutable;
using CivicLens.Application.Roster;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

internal sealed record PublicationBuild(PreviousRelease Previous, DateTimeOffset Now, PublishedRecordRules Rules,
    PublicationCorrections Corrections, IReadOnlyList<RosterMember> Roster, bool ByCode,
    IReadOnlyDictionary<PublishedRecordReference, DateTimeOffset> FirstPublished)
{
    public ImmutableArray<PublishedRevisionBinding> ReleaseRecords { get; private init; } =
        [.. Previous.Entries.Select(PublishedRevisionBinding.Of)];

    public ImmutableArray<ReleaseVoteBill> VoteBills { get; private init; } = [];

    public ImmutableArray<PublishedRecordReference> Returned { get; private init; } = [];

    public DateTimeOffset? FirstPublishedAt(PublishedRecordKind kind, string recordId) =>
        FirstPublished.TryGetValue(new(kind, recordId), out var firstPublishedAt) ? firstPublishedAt : null;

    public PublicationBuild WithVoteBills(IEnumerable<PublishedRecordEntry> releaseEntries, IReadOnlyDictionary<string, ImmutableArray<string>> voteBillActions) =>
        this with { VoteBills = ReleaseVoteBills.From(releaseEntries, voteBillActions) };

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
            ReleaseRecords = [.. ReleaseRecords.Where(binding => !gone.Contains(new(binding.Kind, binding.RecordId)))]
        };
    }
}
