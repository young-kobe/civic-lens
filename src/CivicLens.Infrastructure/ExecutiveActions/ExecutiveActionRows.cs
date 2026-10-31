using CivicLens.Application.ExecutiveActions;
using CivicLens.Application.Collection.FederalRegister;
using CivicLens.Application.Review;
using CivicLens.Core.ExecutiveActions;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Documents;
using CivicLens.Infrastructure.ExecutiveActions.Persistence;
using CivicLens.Infrastructure.Review;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.ExecutiveActions;

internal static class ExecutiveActionRows
{
    internal static readonly ReviewRecords<ExecutiveActionDraftRow, ExecutiveActionRevisionRow, ExecutiveActionReviewDecisionRow, ExecutiveActionRevision> Records =
        new(PostgresAdvisoryLock.ExecutiveActionReview, "Executive action", nameof(ExecutiveActionDraftRow.RecordId), Revised);

    /// <summary>Records one document. An original makes revision 1 with the correction notices that waited for it. A correction appends a revision
    /// to the record of its original, or waits when the original is not recorded. Code approves each revision in the same transaction.
    /// Returns null when the document waits. A correction of a correction is invalid data, in either arrival order.</summary>
    public static async Task<ExecutiveActionRevision?> AdmitAsync(CollectionAttemptDbContext db, string sourceId, string documentJobId,
        FederalRegisterEvidence evidence, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await PostgresAdvisoryLock.AcquireAsync(db, PostgresAdvisoryLock.ExecutiveActionReview, cancellationToken);
        var document = evidence.Document;
        var recordId = ExecutiveActionIdentity.RecordId(sourceId, document.RecordDocumentNumber);
        if (document.CorrectionOf is { } corrected && await CorrectsACorrectionAsync(db, sourceId, document.DocumentNumber, corrected, cancellationToken))
            throw new CorrectionOfCorrectionException(document.DocumentNumber, corrected);
        var row = new ExecutiveActionDocumentRow
        {
            SourceId = sourceId,
            DocumentNumber = document.DocumentNumber,
            RecordId = recordId,
            PublishedOn = document.PublishedOn,
            DocumentJobId = documentJobId
        };
        db.Add(row);
        var revision = document.CorrectionOf is null
            ? await StartRecordAsync(db, sourceId, recordId, evidence, now, cancellationToken)
            : await AddNoticeAsync(db, recordId, row, evidence, now, cancellationToken);
        row.RevisionNumber = revision?.RevisionNumber;
        return revision;
    }

    private static async Task<bool> CorrectsACorrectionAsync(CollectionAttemptDbContext db, string sourceId, string documentNumber,
        string correctedNumber, CancellationToken cancellationToken)
    {
        var ownRecordId = ExecutiveActionIdentity.RecordId(sourceId, documentNumber);
        var documents = db.Set<ExecutiveActionDocumentRow>().Where(row => row.SourceId == sourceId);
        return await documents.AnyAsync(row => row.DocumentNumber == correctedNumber && row.NoticeJson != null, cancellationToken) ||
            await documents.AnyAsync(row => row.RecordId == ownRecordId, cancellationToken);
    }

    private static async Task<ExecutiveActionRevision> StartRecordAsync(CollectionAttemptDbContext db, string sourceId, string recordId,
        FederalRegisterEvidence evidence, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var waiting = await db.Set<ExecutiveActionDocumentRow>().Where(row => row.RecordId == recordId && row.NoticeJson != null)
            .ToListAsync(cancellationToken);
        var revision = ExecutiveActionRevisions.Create(sourceId, now, evidence,
            waiting.Select(row => ReviewJson.Deserialize<ExecutiveActionNotice>(row.NoticeJson!, "notice")));
        foreach (var row in waiting) row.RevisionNumber = revision.RevisionNumber;
        var draft = NewDraft(recordId, now);
        db.Add(draft);
        await AppendApprovedAsync(db, draft, revision, cancellationToken);
        return revision;
    }

    private static async Task<ExecutiveActionRevision?> AddNoticeAsync(CollectionAttemptDbContext db, string recordId,
        ExecutiveActionDocumentRow row, FederalRegisterEvidence evidence, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var notice = ExecutiveActionRevisions.NoticeOf(evidence);
        row.NoticeJson = ReviewJson.Serialize(notice);
        var draft = await db.Set<ExecutiveActionDraftRow>().SingleOrDefaultAsync(candidate => candidate.RecordId == recordId, cancellationToken);
        if (draft is null) return null;
        var current = await Records.FindRevisionAsync(db, recordId, draft.CurrentRevisionNumber, cancellationToken)
            ?? throw new InvalidOperationException("Current executive action revision is missing.");
        var revision = ExecutiveActionRevisions.Add(current, now, notice);
        await AppendApprovedAsync(db, draft, revision, cancellationToken);
        return revision;
    }

    private static async Task AppendApprovedAsync(CollectionAttemptDbContext db, ExecutiveActionDraftRow draft, ExecutiveActionRevision revision,
        CancellationToken cancellationToken)
    {
        revision.ValidateShape();
        await Records.AppendCodeApprovedAsync(db, draft, revision, NewRevision(revision), await Records.ReadCycleAsync(db, draft, cancellationToken),
            () => ApprovalCheck.OfAsync(() => PostgresExecutiveActionReviewStore.ValidateApprovalAsync(db, revision, cancellationToken)));
    }

    private static void Revised(ExecutiveActionDraftRow draft, ExecutiveActionRevision revision)
    {
        ListedDraftRows.Set(draft, revision.SignedOn, [], revision.OfficialIds());
    }

    private static ExecutiveActionDraftRow NewDraft(string recordId, DateTimeOffset now) => new()
    {
        RecordId = recordId,
        CreatedAtUtcTicks = now.UtcTicks
    };

    public static ExecutiveActionRevisionRow NewRevision(ExecutiveActionRevision revision) => new()
    {
        RecordId = revision.RecordId,
        RevisionNumber = revision.RevisionNumber,
        RevisionJson = ReviewJson.Serialize(revision),
        CreatedAtUtcTicks = revision.CreatedAtUtc.UtcTicks
    };
}
