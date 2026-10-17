using CivicLens.Core.Documents;

namespace CivicLens.Application.Relationships;

internal sealed record BillMention(int? Congress, string BillType, int Number, SourceSpan Span)
{
    public bool IsOf(int congress, string? billType, int? number) =>
        Congress == congress && string.Equals(BillType, billType, StringComparison.Ordinal) && Number == number;
}
