using CivicLens.PublicWeb.Publication;

namespace CivicLens.PublicApi.ExecutiveActions;

/// <summary>Reads everything that the server-rendered executive action page shows. The detail holds the record and its notices from one release, and the page needs no other read.</summary>
public sealed class ExecutiveActionPageReader(ExecutiveActionStore actions)
{
    /// <summary>Reads the page of one executive action. Null means the active release holds no such record.</summary>
    public async Task<ExecutiveActionPageData?> ReadAsync(string recordId, CancellationToken cancellationToken)
    {
        if (await actions.FindAsync(recordId, cancellationToken) is not { } detail) return null;
        var row = detail.ExecutiveAction;
        return new(detail.Release, row.RecordId, row.RevisionNumber, row.Headline, row.Kind, row.DocumentNumber, row.SignedOn, row.PublishedOn,
            row.FederalRegisterCitation, row.DocumentUrl, row.OfficialId, row.PresidentName,
            [.. detail.Notices.Select(notice => new ExecutiveActionNoticeData(notice.DocumentNumber, notice.PublishedOn, notice.Url))]);
    }
}
