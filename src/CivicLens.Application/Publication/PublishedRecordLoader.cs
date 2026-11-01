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

    public abstract Task<(IReadOnlyList<ReadyToPublishRecord> Ready, IReadOnlyList<string> NotReadyIds, bool HasMore)> ListReadyAsync(int limit,
        IReadOnlyCollection<string> excludedRecordIds, CancellationToken cancellationToken);

    public abstract Task<List<PendingRecord>> LoadAsync(ImmutableArray<string> recordIds, PublicationBuild build,
        CancellationToken cancellationToken);

    // Renders the page of a record that the previous release holds, from its record file.
    public abstract Task<string> RenderCarriedAsync(string recordId, byte[] json, ReleaseLinks links, PublicationBuild build,
        CancellationToken cancellationToken);

    protected static TRecord Parse<TRecord>(byte[] json) =>
        JsonSerializer.Deserialize<TRecord>(json, PublicationProtocol.JsonOptions)
        ?? throw new InvalidDataException("A carried record file is empty.");
}

internal abstract class PublishedRecordLoader<TCandidate, TAddition>(PublishedRecordKind kind, string noun)
    : PublishedRecordLoader(kind)
    where TAddition : class
{
    public sealed override async Task<(IReadOnlyList<ReadyToPublishRecord> Ready, IReadOnlyList<string> NotReadyIds, bool HasMore)> ListReadyAsync(int limit,
        IReadOnlyCollection<string> excludedRecordIds, CancellationToken cancellationToken)
    {
        var candidates = await ListCandidatesAsync(limit + 1, excludedRecordIds, cancellationToken);
        var checkedCandidates = candidates.Select(candidate => (candidate, ready: ToReady(candidate))).ToArray();
        return ([.. checkedCandidates.Select(item => item.ready).OfType<ReadyToPublishRecord>()],
            [.. checkedCandidates.Where(item => item.ready is null).Select(item => RecordIdOf(item.candidate))], candidates.Count > limit);
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

    protected abstract string RecordIdOf(TCandidate candidate);

    protected abstract ReadyToPublishRecord? ToReady(TCandidate candidate);

    protected abstract Task<IReadOnlyDictionary<string, TAddition>> GetForPublicationAsync(ImmutableArray<string> recordIds,
        PublicationBuild build, CancellationToken cancellationToken);

    protected abstract PendingRecord Build(string recordId, TAddition addition, PublicationBuild build);

    protected ReadyToPublishRecord? Ready(ReviewStatus status, int unresolvedConcernCount, string recordId,
        int revisionNumber) =>
        PublishedRecordRules.IsPublishable(status, unresolvedConcernCount)
            ? new(Kind, recordId, revisionNumber)
            : null;

    protected void RequirePublishable(string recordId, int revisionNumber, ImmutableArray<ReviewDecision> decisions,
        ImmutableArray<ReviewDecision> unresolvedConcerns, PublicationBuild build)
    {
        var previous = build.Previous;
        var status = ReviewPolicy.GetCurrentStatus(revisionNumber, decisions, unresolvedConcerns);
        if (!PublishedRecordRules.IsPublishable(status, unresolvedConcerns.Length))
            throw new ArgumentException($"{noun} {recordId} is not approved or has unresolved concerns.");
        if (previous.Find(Kind, recordId)?.RevisionNumber == revisionNumber)
            throw new ArgumentException($"{noun} {recordId} is already published at its current revision.");
        if (previous.FindWithdrawal(recordId) is { } withdrawal)
            RequireMayReturn(recordId, revisionNumber, decisions, withdrawal.WithdrawnRevisionNumber, build.ByCode);
    }

    // A withdrawn record returns only when a person approved a higher revision. A code approval never returns it, on any path.
    private void RequireMayReturn(string recordId, int revisionNumber, ImmutableArray<ReviewDecision> decisions,
        int withdrawnRevisionNumber, bool byCode)
    {
        var canReturn = PublishedRecordRules.CanReturn(revisionNumber, withdrawnRevisionNumber);
        if (PublishedRecordRules.IsApprovedByCode(revisionNumber, decisions) || (byCode && !canReturn))
            throw new WithdrawnRecordException(new(Kind, recordId));
        if (!canReturn)
            throw new ArgumentException(
                $"{noun} {recordId} is at revision {revisionNumber}, but it was withdrawn at revision {withdrawnRevisionNumber}. Revise it before it returns.");
    }
}
