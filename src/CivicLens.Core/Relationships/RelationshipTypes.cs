using System.Collections.Immutable;
using System.Text.Json;

namespace CivicLens.Core.Relationships;

public static class RelationshipTypes
{
    private static readonly ImmutableArray<RelationshipType> ForAction =
        [RelationshipType.DescribesOwnBill, RelationshipType.StatesPositionOn, RelationshipType.ClaimsCreditFor];

    private static readonly ImmutableArray<RelationshipType> ForVote = [RelationshipType.StatementAboutVote];

    public static ImmutableArray<RelationshipType> For(RelationshipRecordKind target) => target switch
    {
        RelationshipRecordKind.Action => ForAction,
        RelationshipRecordKind.Vote => ForVote,
        _ => throw new ArgumentOutOfRangeException(nameof(target))
    };

    public static string WireName(RelationshipType type) => JsonNamingPolicy.CamelCase.ConvertName(type.ToString());

    public static RelationshipType? Parse(string? wireName, RelationshipRecordKind target)
    {
        foreach (var type in For(target))
            if (WireName(type) == wireName) return type;
        return null;
    }
}
