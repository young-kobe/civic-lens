using CivicLens.PublicApi.Records;
using CivicLens.PublicWeb.Publication;
using CivicLens.Publication.Contracts;

namespace CivicLens.PublicApi.ExecutiveActions;

/// <summary>Reads everything that the server-rendered executive action page shows: the record with its notices, targets, and summary, and its corrections. One snapshot holds both reads, so the page comes from one release.</summary>
public sealed class ExecutiveActionPageReader(ExecutiveActionStore actions, CorrectionHistoryStore corrections, RecordSnapshot snapshot)
{
    /// <summary>Reads the page of one executive action. Null means the active release holds no such record.</summary>
    public async Task<ExecutiveActionPageData?> ReadAsync(string recordId, CancellationToken cancellationToken)
    {
        var (connection, transaction) = await snapshot.OpenAsync(cancellationToken);
        await using var _ = connection;
        await using var __ = transaction;
        if (await actions.FindAsync(transaction, recordId, cancellationToken) is not { } detail) return null;
        var row = detail.ExecutiveAction;
        var corrected = await corrections.ListAsync(transaction, PublishedRecordKind.ExecutiveAction, recordId, row.RevisionNumber, cancellationToken);
        return new(detail.Release, row.RecordId, row.RevisionNumber, row.Headline, row.Kind, row.DocumentNumber, row.SignedOn, row.PublishedOn,
            row.FederalRegisterCitation, row.DocumentUrl, row.OfficialId, row.PresidentName,
            [.. detail.Notices.Select(notice => new ExecutiveActionNoticeData(notice.DocumentNumber, notice.PublishedOn, notice.Url))],
            [.. detail.Targets.Select(target => new ExecutiveActionTargetData(target.Relation, target.Kind, target.Number, target.Reference, target.TargetRecordId))],
            detail.Summary is { } summary ? new(summary.TextOrigin, summary.Label, summary.Cut, summary.Sentences) : null)
        {
            Corrections = corrected
        };
    }
}
