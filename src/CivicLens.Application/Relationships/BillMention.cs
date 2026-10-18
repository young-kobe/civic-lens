using CivicLens.Core.Documents;

namespace CivicLens.Application.Relationships;

internal sealed record BillMention(int? Congress, string BillType, int Number, SourceSpan Span)
{
    public bool IsOf(int congress, string? billType, int? number) =>
        RelationshipPairRule.IsSameBill(Congress, BillType, Number, congress, billType, number);
}
