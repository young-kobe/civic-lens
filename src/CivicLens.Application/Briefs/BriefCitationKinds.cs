using CivicLens.Core.Briefs;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Briefs;

public static class BriefCitationKinds
{
    private static readonly PublishedRecordKind[] Citable = [.. Enum.GetValues<BriefCitationKind>().Select(PublishedKindOf)];

    public static bool IsCitable(PublishedRecordKind kind) => Citable.Contains(kind);

    public static PublishedRecordKind PublishedKindOf(BriefCitationKind kind) => kind switch
    {
        BriefCitationKind.DocumentChange => PublishedRecordKind.DocumentChange,
        BriefCitationKind.Statement => PublishedRecordKind.Statement,
        BriefCitationKind.Action => PublishedRecordKind.Action,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Citation kind has no published record kind.")
    };
}
