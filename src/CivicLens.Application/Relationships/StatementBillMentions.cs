using System.Collections.Immutable;
using CivicLens.Application.Collection.Bills;
using CivicLens.Core.Documents;
using CivicLens.Core.Relationships;
using CivicLens.Core.Statements;

namespace CivicLens.Application.Relationships;

internal static class StatementBillMentions
{
    public static ImmutableArray<BillMention> In(ApprovedStatement statement)
    {
        var congress = statement.Revision.StatementDate is { } date ? CongressCalendar.CongressOn(date) : null;
        return [.. from passage in statement.Passages()
                   from match in BillNumberFinder.FindWithSpans(passage.Text)
                   select new BillMention(congress, CongressBillListItem.NormalizeCitationType(match.Type)!, match.Number,
                       new SourceSpan(passage.Span.ExtractionId, passage.Span.Start + match.Start, match.Length))];
    }
}
