using System.Collections.Immutable;
using CivicLens.Application.Collection.Bills;
using CivicLens.Core.Documents;
using CivicLens.Core.Relationships;
using CivicLens.Core.Statements;

namespace CivicLens.Application.Relationships;

internal sealed record StatementReading(ApprovedStatement Statement, int? Congress, ImmutableArray<StatementPassage> Passages,
    ImmutableArray<BillMention> Mentions)
{
    public bool HasNumberWithoutCongress => Congress is null && !Mentions.IsEmpty;

    public static StatementReading Of(ApprovedStatement statement)
    {
        var congress = statement.Revision.StatementDate is { } date ? CongressCalendar.CongressOn(date) : null;
        var passages = statement.Passages();
        ImmutableArray<BillMention> mentions =
            [.. from passage in passages
                from match in BillNumberFinder.FindWithSpans(passage.Text)
                select new BillMention(congress, CongressBillListItem.NormalizeCitationType(match.Type)!, match.Number,
                    new SourceSpan(passage.Span.ExtractionId, passage.Span.Start + match.Start, match.Length))];
        return new(statement, congress, passages, mentions);
    }
}
