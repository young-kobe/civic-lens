using System.Collections.Immutable;
using CivicLens.Application.Actions;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>
/// Owns the bill status line of a release. Each publish reads the stored status of every action record in the release and commits the rows.
/// The rule for the read time: a status keeps the time of the release that first showed its text and date. A later read of the same action
/// changes no row, so a reader never sees two times for one status. The bill input read joins the rows of the previous release, to keep the time and to find a changed status.
/// </summary>
internal static class ReleaseBillStatuses
{
    public static IReadOnlyDictionary<string, ActionBillStatus> KeepObservationTimes(IReadOnlyList<BillPublicationInput> inputs) =>
        inputs.Where(input => input.Status is not null).Select(input => KeepObservationTime(input.Status!, input.PublishedStatus))
            .ToDictionary(read => read.Status.RecordId, StringComparer.Ordinal);

    public static IEnumerable<string> ChangedActionIds(IEnumerable<string> actionIds, IReadOnlyList<BillPublicationInput> inputs,
        IReadOnlyDictionary<string, ActionBillStatus> statuses)
    {
        var shown = inputs.ToDictionary(input => input.RecordId, input => input.PublishedStatus, StringComparer.Ordinal);
        return actionIds.Where(id => Differs(shown.GetValueOrDefault(id), statuses.GetValueOrDefault(id)?.Status));
    }

    public static IEnumerable<string> ActionIdsOf(IEnumerable<PublishedRecordEntry> entries) =>
        entries.Where(entry => entry.Kind == PublishedRecordKind.Action).Select(entry => entry.RecordId);

    /// <summary>
    /// The rows that a release pins: the status of each of its action records that has one. The commit also needs the bill type.
    /// Each row is validated here, before the publish stages a file, because the text comes from a Congress.gov capture.
    /// </summary>
    public static ImmutableArray<ActionBillStatus> RowsOf(IEnumerable<string> actionIds, IReadOnlyDictionary<string, ActionBillStatus> statuses)
    {
        ImmutableArray<ActionBillStatus> rows = [.. actionIds.Select(id => statuses.GetValueOrDefault(id)).OfType<ActionBillStatus>()
            .OrderBy(status => status.Status.RecordId, StringComparer.Ordinal)];
        foreach (var row in rows) row.Status.Validate();
        return rows;
    }

    private static ActionBillStatus KeepObservationTime(ActionBillStatus read, PublishedBillStatus? shown) =>
        shown is not null && !Differs(shown, read.Status) ? read with { Status = shown } : read;

    private static bool Differs(PublishedBillStatus? before, PublishedBillStatus? after) =>
        before is null || after is null ? before != after : before.Text != after.Text || before.Date != after.Date;
}
