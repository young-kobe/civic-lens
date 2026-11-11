using CivicLens.Application.Actions;
using CivicLens.Core.Actions;
using CivicLens.Publication.Contracts;

namespace CivicLens.Host.Actions;

internal static class ActionLabels
{
    public static string Bill(ActionDraftRevision revision) => Bill(revision.Congress, revision.BillType, revision.BillNumber);

    public static string Bill(int congress, string billType, int billNumber) => $"{BillNumbers.Of(billType, billNumber)}, Congress {congress}";
}
