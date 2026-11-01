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
        IReadOnlyCollection<string> excludedRecordIds, CancellationToken cancellationToken) =>
        statements.ListUnpublishedApprovedAsync(limit, excludedRecordIds, cancellationToken);

    protected override string RecordIdOf(UnpublishedApprovedStatement candidate) => candidate.Statement.CandidateId;

    protected override ReadyToPublishRecord? ToReady(UnpublishedApprovedStatement candidate) =>
        candidate.Statement.CurrentRevision is { } revision
            ? Ready(candidate.Statement.CurrentStatus, candidate.Statement.UnresolvedConcernCount, candidate.Statement.CandidateId,
                revision.RevisionNumber)
            : null;

    protected override Task<IReadOnlyDictionary<string, PublishableStatement>> GetForPublicationAsync(
        ImmutableArray<string> recordIds, PublicationBuild build, CancellationToken cancellationToken) =>
        statements.GetForPublicationAsync(recordIds, cancellationToken);

    protected override PendingRecord Build(string recordId, PublishableStatement addition, PublicationBuild build)
    {
        var review = addition.Review;
        var revision = review.CurrentRevision
            ?? throw new ArgumentException($"Statement {recordId} has no revision.", "recordIds");
        RequirePublishable(recordId, revision.RevisionNumber, review.Decisions, review.UnresolvedConcerns, build);
        var record = new PublishedStatementBuilder(build.Rules).Build(addition, build.Previous.FirstPublishedAt(Kind, recordId), build.Corrections.For(Kind, recordId, revision.RevisionNumber, review.Decisions), build.Now);
        return PendingRecord.Create(record.ToEntry(), review.ReviewStateVersion, record,
            (related, cancellationToken) => build.Renderer.RenderStatementAsync(record, related, build.Catalog, cancellationToken));
    }

    public override Task<string> RenderCarriedAsync(string recordId, byte[] json, ReleaseLinks links, PublicationBuild build,
        CancellationToken cancellationToken) =>
        build.Renderer.RenderStatementAsync(Parse<PublishedStatement>(json), links.Citing(Kind, recordId), build.Catalog, cancellationToken);
}
