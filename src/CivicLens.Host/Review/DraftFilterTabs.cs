using CivicLens.Application.Review;
using CivicLens.Host.Components.Ui;

namespace CivicLens.Host.Review;

internal static class DraftFilterTabs
{
    public static IReadOnlyList<FilterTab> For(DraftStatusFilter selected, Func<string?, string> hrefOf,
        int? needsActionCount = null, int? approvedCount = null) => [
        Tab("All", DraftStatusFilter.All, selected, hrefOf, needsActionCount + approvedCount),
        Tab("Needs action", DraftStatusFilter.NeedsAction, selected, hrefOf, needsActionCount),
        Tab("Approved", DraftStatusFilter.Approved, selected, hrefOf, approvedCount)
    ];

    private static FilterTab Tab(string label, DraftStatusFilter filter, DraftStatusFilter selected,
        Func<string?, string> hrefOf, int? count) =>
        new(label, hrefOf(DraftFilterNames.NameOf(filter)), filter == selected, count);
}
