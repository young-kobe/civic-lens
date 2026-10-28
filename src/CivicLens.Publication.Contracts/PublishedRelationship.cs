namespace CivicLens.Publication.Contracts;

public sealed record PublishedRelationship
{
    public const int MaximumHeadlineLength = 520;
    public const int MaximumCitedHeadlineLength = 256;
    public const int MaximumReasonLength = 400;
    public const int MaximumQuoteLength = 4_096;
    public const int MaximumFactLength = 8_192;
    public const int MaximumFileBytes = 1024 * 1024;

    public required string RecordId { get; init; }
    public required int RevisionNumber { get; init; }
    public required string Headline { get; init; }
    public required PublishedRelationshipCitation Statement { get; init; }
    public required PublishedRelationshipCitation Target { get; init; }
    public required PublishedRelationshipType Type { get; init; }
    public required string Reason { get; init; }
    public required string Quote { get; init; }
    public required string TargetFact { get; init; }
    public DateOnly? StatementDate { get; init; }
    public required string MatchedOfficialId { get; init; }
    public required PublishedOfficial[] Officials { get; init; }
    public required string[] IssueIds { get; init; }
    public required string[] PolicyAreaIds { get; init; }
    public required DateTimeOffset ApprovedAtUtc { get; init; }
    public required DateTimeOffset FirstPublishedAtUtc { get; init; }
    public required PublishedCorrection[] Corrections { get; init; }

    public PublishedRecordEntry ToEntry() => new()
    {
        Kind = PublishedRecordKind.Relationship,
        RecordId = RecordId,
        RevisionNumber = RevisionNumber,
        Headline = Headline,
        FirstPublishedAtUtc = FirstPublishedAtUtc,
        Officials = Officials,
        IssueIds = IssueIds,
        PolicyAreaIds = PolicyAreaIds,
        EventDate = StatementDate,
        LinkType = Type,
        StatementRecordId = Statement.RecordId,
        TargetKind = Target.Kind,
        TargetRecordId = Target.RecordId,
        MatchedOfficialId = MatchedOfficialId
    };

    public static bool IsCitedKind(PublishedRecordKind kind) => kind == PublishedRecordKind.Statement || IsTargetKind(kind);

    public PublishedRelationshipCitation Other(PublishedRecordKind kind, string recordId) =>
        Statement.Kind == kind && Statement.RecordId == recordId ? Target : Statement;

    public void Validate()
    {
        PublishedCorrection.ValidateAll(Corrections, RevisionNumber);
        if (!PublicationProtocol.IsRecordId(PublishedRecordKind.Relationship, RecordId) || RevisionNumber < 1)
            throw new InvalidDataException("Published relationship identity is invalid.");
        RequireText(Headline, MaximumHeadlineLength);
        RequireText(Reason, MaximumReasonLength);
        RequireText(Quote, MaximumQuoteLength);
        RequireText(TargetFact, MaximumFactLength);
        ValidateCitations();
        if (!Enum.IsDefined(Type) || !TypeFits(Type, Target.Kind))
            throw new InvalidDataException("The published relationship type does not fit the kind of the target.");
        if (Officials is null || IssueIds is null || PolicyAreaIds is null ||
            Officials.Any(official => official is null || string.IsNullOrWhiteSpace(official.Name)) ||
            !PublishedEvidenceRules.AreDistinctAndNotBlank(Officials.Select(official => official.Id)) ||
            !PublishedEvidenceRules.AreDistinctAndNotBlank(IssueIds) || !PublishedEvidenceRules.AreDistinctAndNotBlank(PolicyAreaIds))
            throw new InvalidDataException("Published relationship officials, issues, and policy areas must be distinct and not blank.");
        if (!Officials.Any(official => official.Id == MatchedOfficialId))
            throw new InvalidDataException("The matched official of a published relationship must be one of its officials.");
    }

    internal static bool IsTargetKind(PublishedRecordKind kind) => kind is PublishedRecordKind.Action or PublishedRecordKind.Vote;

    internal static bool TypeFits(PublishedRelationshipType type, PublishedRecordKind targetKind) =>
        (targetKind == PublishedRecordKind.Vote) == (type == PublishedRelationshipType.StatementAboutVote);

    private void ValidateCitations()
    {
        if (Statement is not { Kind: PublishedRecordKind.Statement } ||
            Target is not { Kind: var targetKind } || !IsTargetKind(targetKind))
            throw new InvalidDataException("A published relationship cites one statement and one action or vote.");
        foreach (var citation in new[] { Statement, Target })
        {
            if (!PublicationProtocol.IsRecordId(citation.Kind, citation.RecordId) || citation.RevisionNumber < 1)
                throw new InvalidDataException("A published relationship citation needs a record and a revision.");
            RequireText(citation.Headline, MaximumCitedHeadlineLength);
        }
    }

    private static void RequireText(string? text, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > maximumLength)
            throw new InvalidDataException($"Published relationship text must be 1 to {maximumLength} characters.");
    }
}
