using System.Collections.Immutable;
using CivicLens.Application.Review;
using CivicLens.Application.Statements;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

internal sealed class StatementRecordLoader(IStatementReviewStore statements)
    : PublishedRecordLoader(PublishedRecordKind.Statement, "Statement")
{
    public override void ValidateId(string recordId) => ReviewValidation.ValidateCandidateId(recordId);

    public override async Task<(IReadOnlyList<ReadyToPublishRecord> Ready, bool HasMore)> ListReadyAsync(int limit,
        CancellationToken cancellationToken)
    {
        var candidates = await statements.ListUnpublishedApprovedAsync(limit + 1, cancellationToken);
        var ready = candidates
            .Where(candidate => candidate.Statement.CurrentRevision is not null &&
                PublishedRecordRules.IsPublishable(candidate.Statement.CurrentStatus, candidate.Statement.UnresolvedConcernCount))
            .Select(candidate => Ready(candidate.Statement.CandidateId, candidate.Statement.CurrentRevision!.Headline,
                candidate.Statement.CurrentRevision.RevisionNumber, candidate.PublishedRevisionNumber))
            .ToList();
        return (ready, candidates.Count > limit);
    }

    public override async Task<List<PendingRecord>> LoadAsync(ImmutableArray<string> recordIds, PublicationBuild build,
        CancellationToken cancellationToken)
    {
        if (recordIds.IsEmpty) return [];
        var found = await statements.GetForPublicationAsync(recordIds, cancellationToken);
        var builder = new PublishedStatementBuilder(build.Rules);
        var pending = new List<PendingRecord>(recordIds.Length);
        foreach (var candidateId in recordIds)
        {
            var addition = found.GetValueOrDefault(candidateId) ?? throw NotFound(candidateId);
            var review = addition.Review;
            var revision = review.CurrentRevision
                ?? throw new ArgumentException($"Statement {candidateId} has no revision.", nameof(recordIds));
            RequirePublishable(candidateId, revision.RevisionNumber, review.Decisions, review.UnresolvedConcerns, build.Previous);
            var record = builder.Build(addition, build.Previous.Find(Kind, candidateId), build.Now);
            pending.Add(PendingRecord.Create(record.ToEntry(), review.ReviewStateVersion, record,
                await build.Renderer.RenderStatementAsync(record, cancellationToken)));
        }
        return pending;
    }
}
