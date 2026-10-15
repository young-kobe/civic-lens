namespace CivicLens.Publication.Contracts;

public sealed record PublishedAction
{
    public const int MaximumSponsorships = 600;
    public const int MaximumCitationsPerSponsorship = 2;
    public const int MaximumSources = 8;

    public required string RecordId { get; init; }
    public required int RevisionNumber { get; init; }
    public required string Headline { get; init; }
    public required string Summary { get; init; }
    public required int Congress { get; init; }
    public required string BillType { get; init; }
    public required int BillNumber { get; init; }
    public required string BillTitle { get; init; }
    public string? PolicyArea { get; init; }
    public PublishedCitation? PolicyAreaCitation { get; init; }
    public required string[] TrackedIssueIds { get; init; }
    public PublishedCitation? SummaryCitation { get; init; }
    public required PublishedSponsorship[] Sponsorships { get; init; }
    public required PublishedDocumentVersion[] Sources { get; init; }
    public required DateTimeOffset ApprovedAtUtc { get; init; }
    public required DateTimeOffset FirstPublishedAtUtc { get; init; }

    public PublishedRecordEntry ToEntry() => new()
    {
        Kind = PublishedRecordKind.Action,
        RecordId = RecordId,
        RevisionNumber = RevisionNumber,
        Headline = Headline,
        FirstPublishedAtUtc = FirstPublishedAtUtc,
        Officials = [.. Sponsorships.Where(item => item.Role == PublishedSponsorshipRole.Sponsor).Select(item => item.Official)],
        IssueIds = TrackedIssueIds,
        PolicyAreaIds = PolicyArea is null ? [] : [PolicyArea],
        SourceHost = Sources[0].Host,
        EventDate = Sponsorships.Max(item => item.LatestDate)
    };

    public void Validate()
    {
        if (!PublicationProtocol.IsRecordId(PublishedRecordKind.Action, RecordId) || RevisionNumber < 1 ||
            string.IsNullOrWhiteSpace(Headline) || string.IsNullOrWhiteSpace(Summary))
            throw new InvalidDataException("Published record identity or wording is invalid.");
        if (Congress < 1 || BillNumber < 1 || string.IsNullOrWhiteSpace(BillType) || string.IsNullOrWhiteSpace(BillTitle))
            throw new InvalidDataException("Published action requires a bill identity and title.");
        ValidateSources();
        if (Sponsorships is null || Sponsorships.Length is 0 or > MaximumSponsorships)
            throw new InvalidDataException($"Published action requires 1 to {MaximumSponsorships} sponsorships.");
        foreach (var sponsorship in Sponsorships) ValidateSponsorship(sponsorship);
        if ((PolicyArea is null) != (PolicyAreaCitation is null) || PolicyArea is not null && string.IsNullOrWhiteSpace(PolicyArea))
            throw new InvalidDataException("A policy area requires exact evidence.");
        if (PolicyAreaCitation is not null) ValidateCitation(PolicyAreaCitation);
        if (SummaryCitation is not null) ValidateCitation(SummaryCitation);
        if (TrackedIssueIds is null || !PublishedEvidenceRules.AreDistinctAndNotBlank(TrackedIssueIds))
            throw new InvalidDataException("Published action tracked issue ids must be distinct and not blank.");
        if (Sources.Any(source => !Citations.Any(citation => citation.ExtractionId == source.ExtractionId)))
            throw new InvalidDataException("Each published action source must be cited.");
    }

    private IEnumerable<PublishedCitation> Citations => Sponsorships.SelectMany(item => item.Citations)
        .Concat(PolicyAreaCitation is { } area ? [area] : []).Concat(SummaryCitation is { } summary ? [summary] : []);

    private void ValidateSources()
    {
        if (Sources is null || Sources.Length is 0 or > MaximumSources)
            throw new InvalidDataException($"Published action requires 1 to {MaximumSources} sources.");
        foreach (var source in Sources) PublishedEvidenceRules.RequireVersion(source);
        if (Sources.DistinctBy(source => source.ExtractionId).Count() != Sources.Length)
            throw new InvalidDataException("Published action sources must be distinct.");
    }

    private void ValidateSponsorship(PublishedSponsorship? sponsorship)
    {
        if (sponsorship?.Official is null || string.IsNullOrWhiteSpace(sponsorship.Official.Id) ||
            string.IsNullOrWhiteSpace(sponsorship.Official.Name) || !Enum.IsDefined(sponsorship.Role))
            throw new InvalidDataException("Published sponsorship requires a named official and a known role.");
        if (sponsorship.Citations is null || sponsorship.Citations.Length is 0 or > MaximumCitationsPerSponsorship)
            throw new InvalidDataException($"Published sponsorship requires 1 to {MaximumCitationsPerSponsorship} citations.");
        foreach (var citation in sponsorship.Citations) ValidateCitation(citation);
    }

    private void ValidateCitation(PublishedCitation? citation)
    {
        if (citation is null) throw new InvalidDataException("Published citation is missing.");
        var source = Sources.FirstOrDefault(item => item.ExtractionId == citation.ExtractionId)
            ?? throw new InvalidDataException("Published citation must cite one of the action's sources.");
        PublishedEvidenceRules.RequireQuote(source.Text, citation.Start, citation.Length, citation.Quote);
    }
}
