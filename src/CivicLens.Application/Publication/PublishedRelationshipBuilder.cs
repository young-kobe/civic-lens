using CivicLens.Application.Relationships;
using CivicLens.Core.Relationships;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

internal sealed class PublishedRelationshipBuilder(PublishedRecordRules rules)
{
    private static string HeadlineOf(string statementHeadline, string targetHeadline) => $"{statementHeadline} and {targetHeadline}";

    public PublishedRelationship Build(PublishableRelationship relationship, DateTimeOffset? firstPublishedAtUtc, PublishedCorrection[] corrections, DateTimeOffset now)
    {
        var review = relationship.Review;
        var revision = review.CurrentRevision;
        var statement = relationship.Statement.Revision;
        var target = relationship.Target;
        var record = new PublishedRelationship
        {
            RecordId = review.RecordId,
            RevisionNumber = revision.RevisionNumber,
            Headline = HeadlineOf(statement.Headline, target.Headline),
            Statement = Cite(revision.Statement, statement.Headline),
            Target = Cite(revision.Target, target.Headline),
            Type = TypeOf(revision.Type),
            Reason = revision.Reason,
            Quote = QuoteOf(relationship.Statement, revision),
            TargetFact = target.Facts.FirstOrDefault(fact => fact.Id == revision.TargetFactId)?.Text
                ?? throw new InvalidDataException($"Relationship {review.RecordId} names a fact that its target does not list."),
            StatementDate = statement.StatementDate,
            MatchedOfficialId = revision.OfficialId,
            Officials = rules.MapOfficials(review.RecordId, statement.SpeakerOfficialIds),
            IssueIds = [.. statement.IssueIds.Union(target.IssueIds, StringComparer.Ordinal)],
            PolicyAreaIds = [.. statement.PolicyAreaIds.Union(target.PolicyAreaIds, StringComparer.Ordinal)],
            ApprovedAtUtc = PublishedRecordRules.GetApprovalTime(revision.RevisionNumber, review.Decisions),
            FirstPublishedAtUtc = firstPublishedAtUtc ?? now,
            Corrections = corrections
        };
        record.Validate();
        return record;
    }

    private static PublishedRelationshipCitation Cite(RelationshipCitation citation, string headline) => new()
    {
        Kind = RelationshipRecordKinds.PublishedKindOf(citation.Kind),
        RecordId = citation.RecordId,
        RevisionNumber = citation.RevisionNumber,
        Headline = headline
    };

    private static string QuoteOf(ApprovedStatement statement, RelationshipRevision revision)
    {
        var span = revision.StatementQuote;
        if (!statement.HasPassageCovering(span))
            throw new InvalidDataException($"Relationship {revision.RecordId} quotes text outside every passage of its statement.");
        return statement.ArticleText.Substring(span.Start, span.Length);
    }

    private static PublishedRelationshipType TypeOf(RelationshipType type) => type switch
    {
        RelationshipType.DescribesOwnBill => PublishedRelationshipType.DescribesOwnBill,
        RelationshipType.StatesPositionOn => PublishedRelationshipType.StatesPositionOn,
        RelationshipType.ClaimsCreditFor => PublishedRelationshipType.ClaimsCreditFor,
        RelationshipType.StatementAboutVote => PublishedRelationshipType.StatementAboutVote,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Relationship type has no published form.")
    };
}
