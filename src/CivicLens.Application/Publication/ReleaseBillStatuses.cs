using System.Collections.Immutable;
using System.Text.Json;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>
/// Owns the bill status line of a release. Each publish reads the stored status of every action record in the release and writes the status file.
/// The rule for the read time: a status keeps the time of the release that first showed its text and date. A later read of the same action
/// changes no row and no page, so the file and the page never show two times. A carried action page is written again only when its row differs.
/// </summary>
internal static class ReleaseBillStatuses
{
    public static async Task<IReadOnlyDictionary<string, PublishedBillStatus>> LoadAsync(ActionRecordLoader actions, PreviousRelease previous,
        ImmutableArray<PublishedRecordReference> references, CancellationToken cancellationToken)
    {
        ImmutableArray<string> ids = [.. ActionIdsOf(previous.Entries).Concat(references.Where(record => record.Kind == PublishedRecordKind.Action)
            .Select(record => record.RecordId)).Distinct(StringComparer.Ordinal)];
        return (await actions.BillStatusesAsync(ids, cancellationToken))
            .Select(read => previous.BillStatuses.TryGetValue(read.RecordId, out var shown) && !Differs(shown, read) ? shown : read)
            .ToDictionary(status => status.RecordId, StringComparer.Ordinal);
    }

    public static IEnumerable<string> ChangedActionIds(IEnumerable<string> actionIds, PreviousRelease previous,
        IReadOnlyDictionary<string, PublishedBillStatus> statuses) =>
        actionIds.Where(id => Differs(previous.BillStatuses.GetValueOrDefault(id), statuses.GetValueOrDefault(id)));

    public static IEnumerable<string> ActionIdsOf(IEnumerable<PublishedRecordEntry> entries) =>
        entries.Where(entry => entry.Kind == PublishedRecordKind.Action).Select(entry => entry.RecordId);

    public static IEnumerable<string> ActionIdsOf(IEnumerable<PublishedRevisionBinding> bindings) =>
        bindings.Where(binding => binding.Kind == PublishedRecordKind.Action).Select(binding => binding.RecordId);

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
