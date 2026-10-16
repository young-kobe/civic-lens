using System.Collections.Immutable;
using CivicLens.Application.Review;
using CivicLens.Application.Statements;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

internal sealed class StatementRecordLoader(IStatementReviewStore statements)
    : PublishedRecordLoader<UnpublishedApprovedStatement, PublishableStatement>(PublishedRecordKind.Statement, "Statement")
{
    public override void ValidateId(string recordId) => ReviewValidation.ValidateCandidateId(recordId);

    protected override Task<IReadOnlyList<UnpublishedApprovedStatement>> ListCandidatesAsync(int limit,
        CancellationToken cancellationToken) => statements.ListUnpublishedApprovedAsync(limit, cancellationToken);

    protected override ReadyToPublishRecord? ToReady(UnpublishedApprovedStatement candidate) =>
        candidate.Statement.CurrentRevision is { } revision
            ? Ready(candidate.Statement.CurrentStatus, candidate.Statement.UnresolvedConcernCount, candidate.Statement.CandidateId,
                revision.Headline, revision.RevisionNumber, candidate.PublishedRevisionNumber)
            : null;

    protected override Task<IReadOnlyDictionary<string, PublishableStatement>> GetForPublicationAsync(
        ImmutableArray<string> recordIds, PublicationBuild build, CancellationToken cancellationToken) =>
        statements.GetForPublicationAsync(recordIds, cancellationToken);

    protected override async Task<PendingRecord> BuildAsync(string recordId, PublishableStatement addition,
        PublicationBuild build, CancellationToken cancellationToken)
    {
        var review = addition.Review;
        var revision = review.CurrentRevision
            ?? throw new ArgumentException($"Statement {recordId} has no revision.", "recordIds");
        RequirePublishable(recordId, revision.RevisionNumber, review.Decisions, review.UnresolvedConcerns, build.Previous);
        var record = new PublishedStatementBuilder(build.Rules).Build(addition, build.Previous.Find(Kind, recordId), build.Now);
        return PendingRecord.Create(record.ToEntry(), review.ReviewStateVersion, record,
            await build.Renderer.RenderStatementAsync(record, cancellationToken));
    }
}
