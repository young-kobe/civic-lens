using CivicLens.Core.Actions;

namespace CivicLens.Host.Actions;

internal static class ActionLabels
{
    public static string Bill(ActionDraftRevision revision) => Bill(revision.Congress, revision.BillType, revision.BillNumber);

    public static string Bill(int congress, string billType, int billNumber) =>
        $"{billType.ToUpperInvariant()} {billNumber}, Congress {congress}";
}
