using System.Collections.Immutable;
using System.Text.Json;
using CivicLens.Core.Review;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>Owns one record kind for publication: its ID shape, its ready list, and how it loads and builds a record.</summary>
internal abstract class PublishedRecordLoader(PublishedRecordKind kind)
{
    public PublishedRecordKind Kind => kind;

    public abstract void ValidateId(string recordId);

    public abstract Task<(IReadOnlyList<ReadyToPublishRecord> Ready, bool HasMore)> ListReadyAsync(int limit,
        CancellationToken cancellationToken);

    public abstract Task<List<PendingRecord>> LoadAsync(ImmutableArray<string> recordIds, PublicationBuild build,
        CancellationToken cancellationToken);

    protected static Dictionary<string, TRecord> ParseAll<TRecord>(IReadOnlyDictionary<string, byte[]> records) =>
        records.ToDictionary(record => record.Key, record => JsonSerializer.Deserialize<TRecord>(record.Value, PublicationProtocol.JsonOptions)
            ?? throw new InvalidDataException("A carried record file is empty."), StringComparer.Ordinal);

    protected static async Task<IReadOnlyDictionary<string, string>> RenderEachAsync<TRecord>(Dictionary<string, TRecord> records,
        Func<TRecord, string, Task<string>> render)
    {
        var pages = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (recordId, record) in records) pages[recordId] = await render(record, recordId);
        return pages;
    }
}

internal abstract class PublishedRecordLoader<TCandidate, TAddition>(PublishedRecordKind kind, string noun)
    : PublishedRecordLoader(kind)
    where TAddition : class
{
    public sealed override async Task<(IReadOnlyList<ReadyToPublishRecord> Ready, bool HasMore)> ListReadyAsync(int limit,
        CancellationToken cancellationToken)
    {
        var candidates = await ListCandidatesAsync(limit + 1, cancellationToken);
        return ([.. candidates.Select(ToReady).OfType<ReadyToPublishRecord>()], candidates.Count > limit);
    }

    public sealed override async Task<List<PendingRecord>> LoadAsync(ImmutableArray<string> recordIds, PublicationBuild build,
        CancellationToken cancellationToken)
    {
        if (recordIds.IsEmpty) return [];
        var found = await GetForPublicationAsync(recordIds, build, cancellationToken);
        var pending = new List<PendingRecord>(recordIds.Length);
        foreach (var recordId in recordIds)
        {
            var addition = found.GetValueOrDefault(recordId)
                ?? throw new ArgumentException($"{noun} {recordId} does not exist.", nameof(recordIds));
            pending.Add(Build(recordId, addition, build));
        }
        return pending;
    }

    protected abstract Task<IReadOnlyList<TCandidate>> ListCandidatesAsync(int limit, CancellationToken cancellationToken);

    protected abstract ReadyToPublishRecord? ToReady(TCandidate candidate);

    protected abstract Task<IReadOnlyDictionary<string, TAddition>> GetForPublicationAsync(ImmutableArray<string> recordIds,
        PublicationBuild build, CancellationToken cancellationToken);

    protected abstract PendingRecord Build(string recordId, TAddition addition, PublicationBuild build);

    protected ReadyToPublishRecord? Ready(ReviewStatus status, int unresolvedConcernCount, string recordId, string headline,
        int revisionNumber, int? publishedRevisionNumber) =>
        PublishedRecordRules.IsPublishable(status, unresolvedConcernCount)
            ? new(Kind, recordId, headline, revisionNumber, publishedRevisionNumber)
            : null;

    protected void RequirePublishable(string recordId, int revisionNumber, ImmutableArray<ReviewDecision> decisions,
        ImmutableArray<ReviewDecision> unresolvedConcerns, PreviousRelease previous)
    {
        var status = ReviewPolicy.GetCurrentStatus(revisionNumber, decisions, unresolvedConcerns);
        if (!PublishedRecordRules.IsPublishable(status, unresolvedConcerns.Length))
            throw new ArgumentException($"{noun} {recordId} is not approved or has unresolved concerns.");
        if (previous.Find(Kind, recordId)?.RevisionNumber == revisionNumber)
            throw new ArgumentException($"{noun} {recordId} is already published at its current revision.");
    }
}
