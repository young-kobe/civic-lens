using System.Collections.Immutable;
using System.Text.Json;
using CivicLens.Application.Actions;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>
/// Owns the bill status line of a release. Each publish reads the stored status of every action record in the release and writes the status file.
/// The rule for the read time: a status keeps the time of the release that first showed its text and date. A later read of the same action
/// changes no row and no page, so the file and the page never show two times. A carried action page is written again only when its row differs.
/// </summary>
internal static class ReleaseBillStatuses
{
    public static IReadOnlyDictionary<string, ActionBillStatus> KeepObservationTimes(PreviousRelease previous,
        IReadOnlyList<BillPublicationInput> inputs) => inputs.Select(input => input.Status).OfType<ActionBillStatus>()
        .Select(read => previous.BillStatuses.TryGetValue(read.Status.RecordId, out var shown) && !Differs(shown, read.Status) ? read with { Status = shown } : read)
        .ToDictionary(read => read.Status.RecordId, StringComparer.Ordinal);

    public static IEnumerable<string> ChangedActionIds(IEnumerable<string> actionIds, PreviousRelease previous,
        IReadOnlyDictionary<string, ActionBillStatus> statuses) =>
        actionIds.Where(id => Differs(previous.BillStatuses.GetValueOrDefault(id), statuses.GetValueOrDefault(id)?.Status));

    public static IEnumerable<string> ActionIdsOf(IEnumerable<PublishedRecordEntry> entries) =>
        entries.Where(entry => entry.Kind == PublishedRecordKind.Action).Select(entry => entry.RecordId);

    public static IEnumerable<string> ActionIdsOf(IEnumerable<PublishedRevisionBinding> bindings) =>
        bindings.Where(binding => binding.Kind == PublishedRecordKind.Action).Select(binding => binding.RecordId);

    /// <summary>The rows that a release pins: the status of each of its action records that has one. The file and the commit both take these rows. The commit also needs the bill type.</summary>
    public static ImmutableArray<ActionBillStatus> RowsOf(IEnumerable<string> actionIds, IReadOnlyDictionary<string, ActionBillStatus> statuses) =>
        [.. actionIds.Select(id => statuses.GetValueOrDefault(id)).OfType<ActionBillStatus>()
            .OrderBy(status => status.Status.RecordId, StringComparer.Ordinal)];

    public static Task WriteAsync(IReleaseStaging staging, ImmutableArray<ActionBillStatus> rows, CancellationToken cancellationToken)
    {
        var file = new PublishedBillStatuses { Records = [.. rows.Select(row => row.Status)] };
        file.Validate();
        var json = JsonSerializer.SerializeToUtf8Bytes(file, PublicationProtocol.JsonOptions);
        if (json.Length > PublishedBillStatuses.MaximumBytes)
            throw new ArgumentException("The bill status file would exceed its size limit.", nameof(rows));
        return staging.WriteFileAsync(PublicationProtocol.BillStatusPath, json, cancellationToken);
    }

    private static bool Differs(PublishedBillStatus? before, PublishedBillStatus? after) =>
        before is null || after is null ? before != after : before.Text != after.Text || before.Date != after.Date;
}
