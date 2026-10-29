using CivicLens.Host.Components.Evidence;
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

    public static string ReportCategory(string category) => category switch
    {
        ReaderReportProtocol.WrongQuoteCategory => "The quoted text is wrong",
        ReaderReportProtocol.WrongDateCategory => "A date is wrong",
        ReaderReportProtocol.WrongAttributionCategory => "The wrong person or source is named",
        ReaderReportProtocol.MissingContextCategory => "Context is missing",
        ReaderReportProtocol.BrokenSourceLinkCategory => "A source link is broken",
        ReaderReportProtocol.OtherCategory => "Something else",
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, "Report category has no label.")
    };

    public static string Quotation(PublishedQuotationKind kind) =>
        kind == PublishedQuotationKind.DirectQuotation ? "Direct quotation" : "Release text";

    public const string ModelWording = "An AI summary of the official record";

    public static IEnumerable<Fact> Wording(PublishedTextOrigin origin) =>
        origin == PublishedTextOrigin.Model ? [new("Wording", ModelWording)] : [];

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

    public const string Voteview = "Voteview, an academic database at UCLA";

    public static string VoteSource(PublishedVoteSource source) => source switch
    {
        PublishedVoteSource.CongressGov => "Congress.gov House vote data, from the House Clerk",
        PublishedVoteSource.SenateGov => "senate.gov vote file",
        PublishedVoteSource.Voteview => Voteview + " (voteview.com)",
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Vote source has no label.")
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
        PublishedVoteKind.Nomination => "Nomination",
        PublishedVoteKind.Cloture => "Cloture",
        PublishedVoteKind.MotionToProceed => "Motion to proceed",
        PublishedVoteKind.Other => "Other",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Vote kind has no label.")
    };
}
