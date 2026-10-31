using System.Collections.Immutable;
using System.Text.Json;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>
/// Owns the bill status line of a release. Each publish reads the stored status of every action record in the release and writes
/// the status file. A carried action page is written again only when its latest action or its date differs from the previous release,
/// so a new check time alone never rewrites a page.
/// </summary>
internal static class ReleaseBillStatuses
{
    public static async Task<IReadOnlyDictionary<string, PublishedBillStatus>> LoadAsync(ActionRecordLoader actions, PreviousRelease previous,
        ImmutableArray<PublishedRecordReference> references, ImmutableArray<PublishedRecordReference> withdrawn, CancellationToken cancellationToken)
    {
        var gone = withdrawn.Select(record => record.RecordId).ToHashSet(StringComparer.Ordinal);
        ImmutableArray<string> ids = [.. previous.Entries.Where(entry => entry.Kind == PublishedRecordKind.Action).Select(entry => entry.RecordId)
            .Concat(references.Where(record => record.Kind == PublishedRecordKind.Action).Select(record => record.RecordId))
            .Where(id => !gone.Contains(id)).Distinct(StringComparer.Ordinal)];
        return (await actions.BillStatusesAsync(ids, cancellationToken)).ToDictionary(status => status.RecordId, StringComparer.Ordinal);
    }

    public static IEnumerable<string> ChangedActionIds(IEnumerable<string> actionIds, PreviousRelease previous,
        IReadOnlyDictionary<string, PublishedBillStatus> statuses) =>
        actionIds.Where(id => Differs(previous.BillStatuses.GetValueOrDefault(id), statuses.GetValueOrDefault(id)));

    public static IEnumerable<string> ActionIdsOf(IEnumerable<PublishedRecordEntry> entries) =>
        entries.Where(entry => entry.Kind == PublishedRecordKind.Action).Select(entry => entry.RecordId);

    public static Task WriteAsync(IReleaseStaging staging, IEnumerable<string> actionIds,
        IReadOnlyDictionary<string, PublishedBillStatus> statuses, CancellationToken cancellationToken)
    {
        var file = new PublishedBillStatuses
        {
            Records = [.. actionIds.Select(id => statuses.GetValueOrDefault(id)).OfType<PublishedBillStatus>()
                .OrderBy(status => status.RecordId, StringComparer.Ordinal)]
        };
        file.Validate();
        var json = JsonSerializer.SerializeToUtf8Bytes(file, PublicationProtocol.JsonOptions);
        if (json.Length > PublishedBillStatuses.MaximumBytes)
            throw new ArgumentException("The bill status file would exceed its size limit.", nameof(statuses));
        return staging.WriteFileAsync(PublicationProtocol.BillStatusPath, json, cancellationToken);
    }

    private static bool Differs(PublishedBillStatus? before, PublishedBillStatus? after) =>
        before is null || after is null ? before != after : before.Text != after.Text || before.Date != after.Date;
}
