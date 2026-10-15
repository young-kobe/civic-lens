using CivicLens.Core.Actions;

namespace CivicLens.Host.Review;

internal static class ActionLabels
{
    public static string Bill(ActionDraftRevision revision) =>
        $"{revision.BillType.ToUpperInvariant()} {revision.BillNumber}, Congress {revision.Congress}";
}
