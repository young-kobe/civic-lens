using CivicLens.Publication.Contracts;

namespace CivicLens.Host.Publication;

public static class PublishedRecordLabels
{
    public static string Kind(PublishedRecordKind kind) => kind switch
    {
        PublishedRecordKind.DocumentChange => "Document change",
        PublishedRecordKind.Statement => "Statement",
        PublishedRecordKind.Action => "Action",
        PublishedRecordKind.Brief => "Weekly brief",
        PublishedRecordKind.Vote => "Vote",
        PublishedRecordKind.Relationship => "Relationship",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Record kind has no label.")
    };

    public static string Quotation(PublishedQuotationKind kind) =>
        kind == PublishedQuotationKind.DirectQuotation ? "Direct quotation" : "Release text";

    public static string Role(PublishedSponsorshipRole role) => role switch
    {
        PublishedSponsorshipRole.Sponsor => "Sponsor",
        PublishedSponsorshipRole.Cosponsor => "Cosponsor",
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Sponsorship role has no label.")
    };

    public static string RelationshipType(PublishedRelationshipType type) => type switch
    {
        PublishedRelationshipType.DescribesOwnBill => "Describes own bill",
        PublishedRelationshipType.StatesPositionOn => "States position on",
        PublishedRelationshipType.ClaimsCreditFor => "Claims credit for",
        PublishedRelationshipType.StatementAboutVote => "Statement about a vote",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Relationship type has no label.")
    };

    public static string VoteKind(PublishedVoteKind kind) => kind switch
    {
        PublishedVoteKind.Rule => "Rule",
        PublishedVoteKind.PreviousQuestion => "Previous question",
        PublishedVoteKind.MotionToRecommit => "Motion to recommit",
        PublishedVoteKind.Amendment => "Amendment",
        PublishedVoteKind.Suspension => "Suspension of the rules",
        PublishedVoteKind.Passage => "Passage",
        PublishedVoteKind.ConferenceReport => "Conference report",
        PublishedVoteKind.VetoOverride => "Veto override",
        PublishedVoteKind.Other => "Other",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Vote kind has no label.")
    };
}
