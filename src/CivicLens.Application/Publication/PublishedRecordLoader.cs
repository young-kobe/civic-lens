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
        IReadOnlyCollection<string> excludedRecordIds, CancellationToken cancellationToken);

    public abstract Task<List<PendingRecord>> LoadAsync(ImmutableArray<string> recordIds, PublicationBuild build,
        CancellationToken cancellationToken);

    protected static TRecord Parse<TRecord>(byte[] json) =>
        JsonSerializer.Deserialize<TRecord>(json, PublicationProtocol.JsonOptions)
        ?? throw new InvalidDataException("A carried record file is empty.");
}

internal abstract class PublishedRecordLoader<TCandidate, TAddition>(PublishedRecordKind kind, string noun)
    : PublishedRecordLoader(kind)
    where TAddition : class
{
    public sealed override async Task<(IReadOnlyList<ReadyToPublishRecord> Ready, bool HasMore)> ListReadyAsync(int limit,
        IReadOnlyCollection<string> excludedRecordIds, CancellationToken cancellationToken)
    {
        var candidates = await ListCandidatesAsync(limit + 1, excludedRecordIds, cancellationToken);
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
            try
            {
                pending.Add(Build(recordId, addition, build));
            }
            catch (ArgumentException exception) when (exception.GetType() == typeof(ArgumentException))
            {
                UnpublishableRecord.Tag(exception, new(Kind, recordId));
                throw;
            }
        }
        return pending;
    }

    protected abstract Task<IReadOnlyList<TCandidate>> ListCandidatesAsync(int limit,
        IReadOnlyCollection<string> excludedRecordIds, CancellationToken cancellationToken);

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
        if (previous.FindWithdrawal(recordId) is { } withdrawal && !PublishedRecordRules.CanReturn(revisionNumber, withdrawal.WithdrawnRevisionNumber))
            throw new ArgumentException(
                $"{noun} {recordId} is at revision {revisionNumber}, but it was withdrawn at revision {withdrawal.WithdrawnRevisionNumber}. Revise it before it returns.");
    }
}
