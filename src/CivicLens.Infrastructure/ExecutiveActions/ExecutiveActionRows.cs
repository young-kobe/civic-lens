using CivicLens.Application;
using CivicLens.Application.ExecutiveActions;
using CivicLens.Application.Collection.FederalRegister;
using CivicLens.Application.Review;
using CivicLens.Core.ExecutiveActions;
using CivicLens.Core.Review;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Documents;
using CivicLens.Infrastructure.ExecutiveActions.Persistence;
using CivicLens.Infrastructure.Review;
using CivicLens.Publication.Contracts;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.ExecutiveActions;

internal sealed class ExecutiveActionRows
{
    /// <summary>For reads only. A write through it fails, because the build has no code version.</summary>
    internal static readonly ExecutiveActionRows ForReading = new(CodeVersion.Unknown);

    private readonly CodeVersion codeVersion;

    public ExecutiveActionRows(CodeVersion codeVersion)
    {
        this.codeVersion = codeVersion;
        Records = new(PostgresAdvisoryLock.ExecutiveActionReview, PublishedRecordKind.ExecutiveAction, "Executive action", nameof(ExecutiveActionDraftRow.RecordId),
            ExecutiveActionRevisionRows.ReadAsync, Revised, new(Apply: ApplyWaitingNoticesAsync));
    }

    internal ReviewRecords<ExecutiveActionDraftRow, ExecutiveActionRevisionRow, ExecutiveActionReviewDecisionRow, ExecutiveActionRevision> Records { get; }

    /// <summary>Records one document. An original makes revision 1 with the correction notices that waited for it. A correction appends a revision
    /// to the record of its original, or waits when the original is not recorded. Code approves each revision in the same transaction.
    /// A correction of a rejected record waits too, so a rejection stays. Returns null when the document waits. A correction of a correction is invalid data, in either arrival order.</summary>
    public async Task<ExecutiveActionRevision?> AdmitAsync(CollectionAttemptDbContext db, string sourceId, string documentJobId,
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
            BodyHtmlUrl = document.BodyHtmlUrl,
            DocumentJobId = documentJobId
        };
        db.Add(row);
        var revision = document.CorrectionOf is null
            ? await StartRecordAsync(db, sourceId, recordId, evidence, now, cancellationToken)
            : await AddNoticeAsync(db, recordId, row, evidence, now, cancellationToken);
        row.RevisionNumber = revision?.RevisionNumber;
        return revision;
    }

    /// <summary>A person approved a revision. The notices that waited for the record now revise it, and the correction note of the person moves to
    /// the code approval of the new revision, because that approval is the one that publishes. The decision is saved first, because the cycle read needs it.</summary>
    private async Task ApplyWaitingNoticesAsync(CollectionAttemptDbContext db, ExecutiveActionDraftRow draft, ReviewDecision approval,
        CancellationToken cancellationToken)
    {
        await db.SaveChangesAsync(cancellationToken);
        await AppendWaitingNoticesAsync(db, draft, null, approval.CreatedAtUtc, cancellationToken, approval.CorrectionNote);
    }

    /// <summary>Appends one revision that holds every waiting notice of the record, with the new one if the document brings it. Code approves it.</summary>
    private async Task<ExecutiveActionRevision?> AppendWaitingNoticesAsync(CollectionAttemptDbContext db, ExecutiveActionDraftRow draft,
        ExecutiveActionDocumentRow? arriving, DateTimeOffset now, CancellationToken cancellationToken, string? correctionNote = null)
    {
        var waiting = await db.Set<ExecutiveActionDocumentRow>().Where(row => row.RecordId == draft.RecordId && row.NoticeUrl != null &&
            row.RevisionNumber == null).ToListAsync(cancellationToken);
        if (arriving is not null) waiting.Add(arriving);
        if (waiting.Count == 0) return null;
        var current = await Records.FindRevisionAsync(db, draft.RecordId, draft.CurrentRevisionNumber, cancellationToken)
            ?? throw new InvalidOperationException("Current executive action revision is missing.");
        var revision = ExecutiveActionRevisions.Add(current, now, waiting.Select(ExecutiveActionRevisionRows.NoticeOf));
        await AppendApprovedAsync(db, draft, revision, cancellationToken, correctionNote);
        foreach (var row in waiting) row.RevisionNumber = revision.RevisionNumber;
        return revision;
    }

    private static async Task<bool> CorrectsACorrectionAsync(CollectionAttemptDbContext db, string sourceId, string documentNumber,
        string correctedNumber, CancellationToken cancellationToken)
    {
        var ownRecordId = ExecutiveActionIdentity.RecordId(sourceId, documentNumber);
        var documents = db.Set<ExecutiveActionDocumentRow>().Where(row => row.SourceId == sourceId);
        return await documents.AnyAsync(row => row.DocumentNumber == correctedNumber && row.NoticeUrl != null, cancellationToken) ||
            await documents.AnyAsync(row => row.RecordId == ownRecordId, cancellationToken);
    }

    private async Task<ExecutiveActionRevision> StartRecordAsync(CollectionAttemptDbContext db, string sourceId, string recordId,
        FederalRegisterEvidence evidence, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var waiting = await db.Set<ExecutiveActionDocumentRow>().Where(row => row.RecordId == recordId && row.NoticeUrl != null)
            .ToListAsync(cancellationToken);
        var revision = ExecutiveActionRevisions.Create(sourceId, now, evidence,
            waiting.Select(ExecutiveActionRevisionRows.NoticeOf));
        foreach (var row in waiting) row.RevisionNumber = revision.RevisionNumber;
        var draft = NewDraft(recordId, now);
        db.Add(draft);
        await AppendApprovedAsync(db, draft, revision, cancellationToken);
        return revision;
    }

    private async Task<ExecutiveActionRevision?> AddNoticeAsync(CollectionAttemptDbContext db, string recordId,
        ExecutiveActionDocumentRow row, FederalRegisterEvidence evidence, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ExecutiveActionRevisionRows.SetNotice(row, ExecutiveActionRevisions.NoticeOf(evidence));
        var draft = await db.Set<ExecutiveActionDraftRow>().SingleOrDefaultAsync(candidate => candidate.RecordId == recordId, cancellationToken);
        if (draft is null || draft.ReviewStatus == ReviewStatus.Rejected) return null;
        return await AppendWaitingNoticesAsync(db, draft, row, now, cancellationToken);
    }

    private async Task AppendApprovedAsync(CollectionAttemptDbContext db, ExecutiveActionDraftRow draft, ExecutiveActionRevision revision,
        CancellationToken cancellationToken, string? correctionNote = null)
    {
        revision.ValidateShape();
        await Records.AppendCodeApprovedAsync(db, draft, revision, ExecutiveActionRevisionRows.Create(revision, codeVersion), await Records.ReadCycleAsync(db, draft, cancellationToken),
            () => ApprovalCheck.OfAsync(() => PostgresExecutiveActionReviewStore.ValidateApprovalAsync(db, revision, cancellationToken)), correctionNote);
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
}
