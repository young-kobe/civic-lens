using CivicLens.Application.Publication;
using CivicLens.Host.Components.Evidence;
using CivicLens.Publication.Contracts;

namespace CivicLens.Host.Publication;

public static class PublishedRecordLabels
{
    public static string Kind(PublishedRecordKind kind) => kind switch
    {
        PublishedRecordKind.DocumentChange => "Document change",
        PublishedRecordKind.Statement => "Statement",
        PublishedRecordKind.Action => "Bill or resolution",
        PublishedRecordKind.Brief => "Weekly brief",
        PublishedRecordKind.Vote => "Vote",
        PublishedRecordKind.Relationship => "Relationship",
        PublishedRecordKind.ExecutiveAction => "Executive action",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Record kind has no label.")
    };

    public const string NewestFirst = "Newest first";

    public const string DefaultOrder = "Default order";

    public static IReadOnlyDictionary<string, string> KindLabels { get; } =
        Enum.GetValues<PublishedRecordKind>().ToDictionary(PublicationProtocol.KindText, Kind);

    public static string KindPlural(PublishedRecordKind kind) => kind switch
    {
        PublishedRecordKind.DocumentChange => "Document changes",
        PublishedRecordKind.Statement => "Statements",
        PublishedRecordKind.Action => "Bills and resolutions",
        PublishedRecordKind.Brief => "Weekly briefs",
        PublishedRecordKind.Vote => "Votes",
        PublishedRecordKind.Relationship => "Relationships",
        PublishedRecordKind.ExecutiveAction => "Executive actions",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Record kind has no plural label.")
    };

    public static string Badge(PublishedRecordEntry entry) =>
        entry.DocumentType is { } type ? ExecutiveActionKind(type) : Kind(entry.Kind);

    public static string ExecutiveActionKind(PublishedExecutiveActionKind kind) => kind switch
    {
        PublishedExecutiveActionKind.ExecutiveOrder => "Executive order",
        PublishedExecutiveActionKind.Proclamation => "Proclamation",
        PublishedExecutiveActionKind.Notice => "Presidential notice",
        PublishedExecutiveActionKind.Memorandum => "Memorandum",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Executive action kind has no label.")
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
        kind == PublishedQuotationKind.DirectQuotation ? "Inside quotation marks" : "Not in quotation marks";

    public const string OtherPeopleQuoted = "Other people quoted, named as the article writes them";

    public const string ModelWording = "An AI summary of the official record";

    public const string CodeWording = "Written automatically from Congress.gov data, with no AI";

    public const string ModelChosenIssues = "Chosen by an AI model from the bill title and subjects";

    public static IEnumerable<Fact> Wording(PublishedTextOrigin origin, string modelWording = ModelWording) => origin switch
    {
        PublishedTextOrigin.Model => [new("Wording", modelWording)],
        PublishedTextOrigin.Code => [new("Wording", CodeWording)],
        _ => []
    };

    public const string StatementModelWording = "An AI summary of this release.";

    public static IEnumerable<Fact> StatementWording(PublishedStatement statement) =>
        statement.ApprovedBy == PublishedApprover.Code ? Wording(statement.TextOrigin, StatementModelWording) : [];

    public const string CodeReadDate = "Date read from the release by code";

    public static string StatementDate(PublishedStatement statement, string date) =>
        statement.ApprovedBy == PublishedApprover.Code && statement.StatementDateEvidence is not null ? $"{date}. {CodeReadDate}." : date;

    public const string PersonApproval = "Approved";

    private const string CodeApproval = "Checked by code";

    public static string Approval(PublishedApprover approver) => approver == PublishedApprover.Code ? CodeApproval : PersonApproval;

    public static string ApprovalOn(PublishedApprover approver, string moment) =>
        approver == PublishedApprover.Code ? $"{CodeApproval} on {moment}" : $"{PersonApproval} {moment}";

    public static string StatementPolicyAreas(PublishedApprover approver) =>
        approver == PublishedApprover.Code ? "Policy areas (chosen by AI)" : "Policy areas";

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
        PublishedVoteSource.Voteview => Voteview + " (voteview.com)",
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Vote source has no label.")
    };

    public const string OtherQuestion = "Other question";

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
        PublishedVoteKind.Other => OtherQuestion,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Vote kind has no label.")
    };

    public static string? VoteKindTermKey(PublishedVoteKind kind) => kind switch
    {
        PublishedVoteKind.Rule => "house-rule",
        PublishedVoteKind.PreviousQuestion => "previous-question",
        PublishedVoteKind.MotionToRecommit => "motion-to-recommit",
        PublishedVoteKind.Amendment => "amendment",
        PublishedVoteKind.Suspension => "suspension-of-the-rules",
        PublishedVoteKind.Passage => "passage",
        PublishedVoteKind.ConferenceReport => "conference-report",
        PublishedVoteKind.VetoOverride => "veto-override",
        PublishedVoteKind.Nomination => "confirmation-vote",
        PublishedVoteKind.Cloture => "cloture",
        PublishedVoteKind.MotionToProceed => "motion-to-proceed",
        PublishedVoteKind.Other => null,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Vote kind has no term.")
    };

    public static string ExecutiveActionTermKey(PublishedExecutiveActionKind kind) => kind switch
    {
        PublishedExecutiveActionKind.ExecutiveOrder => "executive-order",
        PublishedExecutiveActionKind.Proclamation => "proclamation",
        PublishedExecutiveActionKind.Notice => "presidential-notice",
        PublishedExecutiveActionKind.Memorandum => "memorandum",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Executive action kind has no term.")
    };

    public const string Present = "Present";

    public static string? CastTermKey(string cast) => cast switch
    {
        PublishedOfficialVote.NotVoting => "not-voting",
        Present => "present",
        _ => PublishedOfficialVote.SideOf(cast) switch
        {
            PublishedPartyMajority.Yea => "yea",
            PublishedPartyMajority.Nay => "nay",
            _ => null
        }
    };

    public static string VoteFactValue(string text) => text[(text.IndexOf(':') + 1)..].TrimStart();

    public static string VoteResultLabel(PublishedVoteFact fact, string members)
    {
        var label = fact.Text[..fact.Text.IndexOf(':')];
        if (label.StartsWith(PublishedVote.PartyTotalPrefix, StringComparison.Ordinal))
            return PartyTotal(label[PublishedVote.PartyTotalPrefix.Length..]) ?? label;
        return label.StartsWith(PublishedVote.CandidateTotalPrefix, StringComparison.Ordinal)
            ? CandidateTotal(label[PublishedVote.CandidateTotalPrefix.Length..], members)
            : label;
    }

    private static string CandidateTotal(string name, string members) => name switch
    {
        PublishedOfficialVote.NotVoting => $"Not voting, all {members}",
        Present => $"{Present}, all {members}",
        _ => $"Votes for {name}, all {members}"
    };

    private static string? PartyTotal(string letter) => PartyLetters.NameOf(letter) switch
    {
        PublishedPartyMajority.Democrat => "Democratic party total",
        PublishedPartyMajority.Republican => "Republican party total",
        PartyLetters.Independent => "Independent members' total",
        _ => null
    };
}
