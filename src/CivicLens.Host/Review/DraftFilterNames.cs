using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Host.Review;

internal static class DraftFilterNames
{
    public const string NeedsAction = "needs-action";
    public const string Approved = "approved";

    public static DraftStatusFilter Parse(string? name) => name switch
    {
        NeedsAction => DraftStatusFilter.NeedsAction,
        Approved => DraftStatusFilter.Approved,
        _ => DraftStatusFilter.All
    };

    public static string? NameOf(DraftStatusFilter filter) => filter switch
    {
        DraftStatusFilter.NeedsAction => NeedsAction,
        DraftStatusFilter.Approved => Approved,
        _ => null
    };
}
