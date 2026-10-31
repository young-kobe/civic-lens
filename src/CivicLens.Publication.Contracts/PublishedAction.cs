namespace CivicLens.Publication.Contracts;

public sealed record PublishedAction
{
    public const int MaximumSponsorships = 600;
    public const int MaximumCitationsPerSponsorship = 2;
    public const int MaximumSources = 8;
    public const int MaximumBillTitleLength = 4_000;

    public required string RecordId { get; init; }
    public required int RevisionNumber { get; init; }
    public required string Headline { get; init; }
    public required string Summary { get; init; }
    public required PublishedTextOrigin TextOrigin { get; init; }
    public required int Congress { get; init; }
    public required string BillType { get; init; }
    public required int BillNumber { get; init; }
    public required string BillTitle { get; init; }
    public string? PolicyArea { get; init; }
    public string? PolicyAreaId { get; init; }
    public PublishedCitation? PolicyAreaCitation { get; init; }
    public required string[] TrackedIssueIds { get; init; }
    public PublishedCitation? SummaryCitation { get; init; }
    public PublishedCitation? BillCitation { get; init; }
    public required PublishedSponsorship[] Sponsorships { get; init; }
    public required PublishedDocumentVersion[] Sources { get; init; }
    public required DateTimeOffset ApprovedAtUtc { get; init; }
    public required DateTimeOffset FirstPublishedAtUtc { get; init; }
    public required PublishedCorrection[] Corrections { get; init; }

    public PublishedRecordEntry ToEntry() => new()
    {
        Kind = PublishedRecordKind.Action,
        RecordId = RecordId,
        RevisionNumber = RevisionNumber,
        Headline = Headline,
        FirstPublishedAtUtc = FirstPublishedAtUtc,
        Officials = [.. Sponsorships.Where(item => item.Role == PublishedSponsorshipRole.Sponsor).Select(item => item.Official)],
        IssueIds = TrackedIssueIds,
        PolicyAreaIds = PolicyAreaId is null ? [] : [PolicyAreaId],
        SourceHost = Sources[0].Host,
        BillTitle = BillTitle,
        EventDate = Sponsorships.Max(item => item.LatestDate),
        CosponsorIds = CosponsorIdsOfEntry()
    };

    private string[] CosponsorIdsOfEntry()
    {
        var sponsorIds = Sponsorships.Where(item => item.Role == PublishedSponsorshipRole.Sponsor).Select(item => item.Official.Id).ToHashSet(StringComparer.Ordinal);
        return [.. Sponsorships
            .Where(item => item.Role == PublishedSponsorshipRole.Cosponsor && item.WithdrawnOn is null && !sponsorIds.Contains(item.Official.Id))
            .Select(item => item.Official.Id).Distinct(StringComparer.Ordinal)];
    }

    public void Validate()
    {
        PublishedCorrection.ValidateAll(Corrections, RevisionNumber);
        if (!PublicationProtocol.IsRecordId(PublishedRecordKind.Action, RecordId) || RevisionNumber < 1 ||
            string.IsNullOrWhiteSpace(Headline) || string.IsNullOrWhiteSpace(Summary))
            throw new InvalidDataException("Published record identity or wording is invalid.");
        if (Congress < 1 || BillNumber < 1 || string.IsNullOrWhiteSpace(BillType) || string.IsNullOrWhiteSpace(BillTitle) ||
            BillTitle.Length > MaximumBillTitleLength)
            throw new InvalidDataException("Published action requires a bill identity and title.");
        PublishedEvidenceRules.RequireSources(Sources, MaximumSources, "action");
        if (Sponsorships is null || Sponsorships.Length is 0 or > MaximumSponsorships)
            throw new InvalidDataException($"Published action requires 1 to {MaximumSponsorships} sponsorships.");
        foreach (var sponsorship in Sponsorships) ValidateSponsorship(sponsorship);
        if ((PolicyArea is null) != (PolicyAreaCitation is null) || PolicyArea is not null && string.IsNullOrWhiteSpace(PolicyArea))
            throw new InvalidDataException("A policy area requires exact evidence.");
        if (PolicyAreaId is not null && (PolicyArea is null || string.IsNullOrWhiteSpace(PolicyAreaId)))
            throw new InvalidDataException("A policy area id requires a policy area.");
        if (PolicyAreaCitation is not null) ValidateCitation(PolicyAreaCitation);
        if (SummaryCitation is not null) ValidateCitation(SummaryCitation);
        if (BillCitation is not null) ValidateCitation(BillCitation);
        if (TrackedIssueIds is null || !PublishedEvidenceRules.AreDistinctAndNotBlank(TrackedIssueIds))
            throw new InvalidDataException("Published action tracked issue ids must be distinct and not blank.");
        PublishedEvidenceRules.RequireEverySourceCited(Sources, Citations(), "action");
    }

    public IEnumerable<PublishedCitation> Citations() => Sponsorships.SelectMany(item => item.Citations)
        .Concat(PolicyAreaCitation is { } area ? [area] : []).Concat(SummaryCitation is { } summary ? [summary] : [])
        .Concat(BillCitation is { } bill ? [bill] : []);

    private void ValidateSponsorship(PublishedSponsorship? sponsorship)
    {
        if (sponsorship?.Official is null || string.IsNullOrWhiteSpace(sponsorship.Official.Id) ||
            string.IsNullOrWhiteSpace(sponsorship.Official.Name) || !Enum.IsDefined(sponsorship.Role))
            throw new InvalidDataException("Published sponsorship requires a named official and a known role.");
        if (sponsorship.Citations is null || sponsorship.Citations.Length is 0 or > MaximumCitationsPerSponsorship)
            throw new InvalidDataException($"Published sponsorship requires 1 to {MaximumCitationsPerSponsorship} citations.");
        if (sponsorship.WithdrawnOn < sponsorship.SponsoredOn)
            throw new InvalidDataException("A sponsorship cannot be withdrawn before it began.");
        foreach (var citation in sponsorship.Citations) ValidateCitation(citation);
    }

    private void ValidateCitation(PublishedCitation? citation) => PublishedEvidenceRules.RequireCitation(Sources, citation, "action");
}
