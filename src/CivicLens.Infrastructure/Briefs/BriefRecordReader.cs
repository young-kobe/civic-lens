using System.Collections.Immutable;
using CivicLens.Application.Briefs;
using CivicLens.Application.Publication;
using CivicLens.Core.Actions;
using CivicLens.Core.Briefs;
using CivicLens.Core.Review;
using CivicLens.Core.Statements;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Review;

namespace CivicLens.Infrastructure.Briefs;

internal static class BriefRecordReader
{
    public static Task<ImmutableArray<BriefRecord>> ReadAsync(CollectionAttemptDbContext db, ImmutableArray<PublishedRevisionBinding> bindings,
        CancellationToken cancellationToken) => ReadKindsAsync(db, bindings, approvedOnly: true, cancellationToken);

    public static Task<ImmutableArray<BriefRecord>> ReadExactAsync(CollectionAttemptDbContext db,
        ImmutableArray<PublishedRevisionBinding> bindings, CancellationToken cancellationToken) =>
        ReadKindsAsync(db, bindings, approvedOnly: false, cancellationToken);

    public static PublishedRevisionBinding BindingOf(BriefCitation citation) =>
        new(BriefCitationKinds.PublishedKindOf(citation.Kind), citation.RecordId, citation.RevisionNumber);

    public static Task<HashSet<PublishedRevisionBinding>> FindChangedInActiveReleaseAsync(CollectionAttemptDbContext db,
        IEnumerable<BriefCitation> citations, CancellationToken cancellationToken) =>
        CitedRevisions.FindNotCurrentInActiveReleaseAsync(db, citations.Select(BindingOf), cancellationToken);

    public static Task<HashSet<PublishedRevisionBinding>> FindChangedAsync(CollectionAttemptDbContext db,
        IEnumerable<BriefCitation> citations, ImmutableArray<PublishedRevisionBinding> releaseRecords, CancellationToken cancellationToken) =>
        CitedRevisions.FindNotCurrentAsync(db, citations.Select(BindingOf), releaseRecords, cancellationToken);

    private static async Task<ImmutableArray<BriefRecord>> ReadKindsAsync(CollectionAttemptDbContext db,
        ImmutableArray<PublishedRevisionBinding> bindings, bool approvedOnly, CancellationToken cancellationToken)
    {
        var records = new List<BriefRecord>();
        records.AddRange(await ReadKindAsync<DocumentChangeDraftRevision>(db, bindings,
            BriefCitationKind.DocumentChange, approvedOnly, BriefRecord.From, cancellationToken));
        records.AddRange(await ReadKindAsync<StatementDraftRevision>(db, bindings,
            BriefCitationKind.Statement, approvedOnly, BriefRecord.From, cancellationToken));
        records.AddRange(await ReadKindAsync<ActionDraftRevision>(db, bindings,
            BriefCitationKind.Action, approvedOnly, BriefRecord.From, cancellationToken));
        return [.. records.OrderBy(record => record.Kind).ThenBy(record => record.RecordId, StringComparer.Ordinal)];
    }

    private static async Task<IReadOnlyList<BriefRecord>> ReadKindAsync<TRevision>(CollectionAttemptDbContext db,
        ImmutableArray<PublishedRevisionBinding> bindings, BriefCitationKind kind, bool approvedOnly,
        Func<TRevision, BriefRecord> toRecord, CancellationToken cancellationToken)
        where TRevision : class, IReviewRevision
    {
        var publishedKind = BriefCitationKinds.PublishedKindOf(kind);
        var requested = bindings.Where(binding => binding.Kind == publishedKind).ToArray();
        var wanted = approvedOnly
            ? requested.Except(await CitedRevisions.FindNotApprovedAsync(db, publishedKind, requested, cancellationToken)).ToArray() : requested;
        if (wanted.Length == 0) return [];
        var json = await CitedRevisions.ReadRevisionJsonAsync(db, publishedKind, wanted, cancellationToken);
        return [.. json.Select(item => toRecord(ReviewJson.Deserialize<TRevision>(item, "revision")))];
    }
}
