namespace CivicLens.Publication.Contracts;

public sealed record PublishedRecordEntry
{
    public const int MaximumFilterValues = 64;
    public const int MaximumFilterValueLength = 256;

    public required PublishedRecordKind Kind { get; init; }
    public required string RecordId { get; init; }
    public required int RevisionNumber { get; init; }
    public required string Headline { get; init; }
    public required DateTimeOffset FirstPublishedAtUtc { get; init; }
    public required PublishedOfficial[] Officials { get; init; }
    public required string[] IssueIds { get; init; }
    public required string[] PolicyAreaIds { get; init; }
    public string? SourceHost { get; init; }
    public DateOnly? EventDate { get; init; }
    public string? BillTitle { get; init; }
    public PublishedRelationshipType? LinkType { get; init; }
    public string? StatementRecordId { get; init; }
    public PublishedRecordKind? TargetKind { get; init; }
    public string? TargetRecordId { get; init; }
    public string? MatchedOfficialId { get; init; }
    public PublishedExecutiveActionKind? DocumentType { get; init; }
    public int? YeaCount { get; init; }
    public int? NayCount { get; init; }
    public PublishedVoteAlignment? Alignment { get; init; }
    public PublishedOfficial[]? CosponsorOfficials { get; init; }

    public void Validate()
    {
        if (!PublicationProtocol.IsRecordId(Kind, RecordId) || RevisionNumber < 1 || string.IsNullOrWhiteSpace(Headline))
            throw new InvalidDataException("Release manifest entry is invalid.");
        if (Officials is null || Officials.Any(official => official is null || !IsFilterValue(official.Name)) ||
            !AreFilterValues(Officials.Select(official => official?.Id)) || !AreFilterValues(IssueIds) ||
            !AreFilterValues(PolicyAreaIds) || (SourceHost is not null && !IsFilterValue(SourceHost)))
            throw new InvalidDataException(
                $"Release manifest filters allow {MaximumFilterValues} distinct values of up to {MaximumFilterValueLength} characters each.");
        if (Kind == PublishedRecordKind.Action ? !HasValidBillTitle : BillTitle is not null)
            throw new InvalidDataException($"An action manifest entry holds a bill title of 1 to {PublishedAction.MaximumBillTitleLength} characters. Other entries hold none.");
        if ((Kind == PublishedRecordKind.ExecutiveAction) != (DocumentType is { } type && Enum.IsDefined(type)))
            throw new InvalidDataException("An executive action manifest entry names its document type. Other entries name none.");
        if (Kind == PublishedRecordKind.Relationship ? !HasValidLink : HasAnyLink)
            throw new InvalidDataException("A relationship manifest entry names its link type, statement, and target. Other entries name none.");
        if (!HasValidVoteFacts)
            throw new InvalidDataException("A vote manifest entry holds both Yea and Nay counts or none, and an alignment only with them. Other entries hold none.");
        if (!HasValidCosponsors)
            throw new InvalidDataException(
                $"An action manifest entry may hold at most {PublishedAction.MaximumSponsorships} distinct cosponsors. Other entries hold none.");
    }

    private bool HasValidVoteFacts =>
        YeaCount is null && NayCount is null && Alignment is null ||
        Kind == PublishedRecordKind.Vote && YeaCount is >= 0 && NayCount is >= 0 &&
        (long)YeaCount.Value + NayCount.Value <= PublishedVote.MaximumMembers && (Alignment is null || Enum.IsDefined(Alignment.Value));

    private bool HasValidCosponsors =>
        CosponsorOfficials is null ||
        Kind == PublishedRecordKind.Action && CosponsorOfficials.Length <= PublishedAction.MaximumSponsorships &&
        CosponsorOfficials.All(official => official is not null && IsFilterValue(official.Id) && IsFilterValue(official.Name)) &&
        PublishedEvidenceRules.AreDistinctAndNotBlank(CosponsorOfficials.Select(official => official.Id));

    private bool HasValidBillTitle => !string.IsNullOrWhiteSpace(BillTitle) && BillTitle.Length <= PublishedAction.MaximumBillTitleLength;

    private bool HasAnyLink => LinkType is not null || StatementRecordId is not null || TargetKind is not null || TargetRecordId is not null ||
        MatchedOfficialId is not null;

    private bool HasValidLink => LinkType is { } type && Enum.IsDefined(type) && TargetKind is { } target &&
        PublishedRelationship.IsTargetKind(target) && PublishedRelationship.TypeFits(type, target) &&
        PublicationProtocol.IsRecordId(PublishedRecordKind.Statement, StatementRecordId) && PublicationProtocol.IsRecordId(target, TargetRecordId) &&
        Officials.Any(official => official.Id == MatchedOfficialId);

    internal static bool AreFilterValues(IEnumerable<string?>? values)
    {
        if (values is null) return false;
        var all = values.ToArray();
        return all.Length <= MaximumFilterValues && all.All(IsFilterValue) && PublishedEvidenceRules.AreDistinctAndNotBlank(all);
    }

    private static bool IsFilterValue(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= MaximumFilterValueLength;
}
