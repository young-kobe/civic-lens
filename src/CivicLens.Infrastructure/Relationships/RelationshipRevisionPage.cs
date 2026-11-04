using CivicLens.Core.Documents;
using CivicLens.Core.Relationships;
using CivicLens.Infrastructure.Relationships.Persistence;
using CivicLens.Infrastructure.Review;

namespace CivicLens.Infrastructure.Relationships;

internal sealed record RelationshipRevisionPage(IReadOnlyList<RelationshipRevisionRow> Rows) : IRevisionRowPage<RelationshipRevisionRow>
{
    public RelationshipRevision Rebuild(RelationshipRevisionRow row)
    {
        var (statement, target) = RelationshipRevisionRows.CitationsOf(row);
        var match = new RelationshipMatch(row.MatchBasis!.Value, new(row.MatchExtractionId!, row.MatchStart!.Value, row.MatchLength!.Value));
        var quote = new SourceSpan(row.StatementQuoteExtractionId!, row.StatementQuoteStart!.Value, row.StatementQuoteLength!.Value);
        return new(row.RecordId, row.RevisionNumber, row.AuthorSubject, new DateTimeOffset(row.CreatedAtUtcTicks, TimeSpan.Zero), statement, target,
            match, row.Type!.Value, row.Reason!, quote, row.TargetFactId!, row.OfficialId!);
    }

    public IReadOnlyList<StoredRevision<RelationshipRevisionRow, RelationshipRevision>> ToStoredRevisions() =>
        [.. Rows.Select(row => new StoredRevision<RelationshipRevisionRow, RelationshipRevision>(row, () => Rebuild(row)))];
}
