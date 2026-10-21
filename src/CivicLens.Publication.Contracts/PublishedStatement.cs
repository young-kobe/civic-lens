namespace CivicLens.Publication.Contracts;

public sealed record PublishedStatement
{
    public const int MaximumQuotations = 32;

    public required string RecordId { get; init; }
    public required int RevisionNumber { get; init; }
    public required string Headline { get; init; }
    public required string Summary { get; init; }
    public DateOnly? StatementDate { get; init; }
    public PublishedCitation? StatementDateEvidence { get; init; }
    public required PublishedOfficial[] Speakers { get; init; }
    public required string[] OtherSpeakers { get; init; }
    public required string[] IssueIds { get; init; }
    public required string[] PolicyAreaIds { get; init; }
    public required PublishedQuotation[] Quotations { get; init; }
    public required string[] BillNumbers { get; init; }
    public required PublishedDocumentVersion Source { get; init; }
    public required DateTimeOffset ApprovedAtUtc { get; init; }
    public required DateTimeOffset FirstPublishedAtUtc { get; init; }
    public PublishedCorrection[] Corrections { get; init; } = [];

    public PublishedRecordEntry ToEntry() => new()
    {
        Kind = PublishedRecordKind.Statement,
        RecordId = RecordId,
        RevisionNumber = RevisionNumber,
        Headline = Headline,
        FirstPublishedAtUtc = FirstPublishedAtUtc,
        Officials = Speakers,
        IssueIds = IssueIds,
        PolicyAreaIds = PolicyAreaIds,
        SourceHost = Source.Host,
        EventDate = StatementDate
    };

    public void Validate()
    {
        PublishedCorrection.ValidateAll(Corrections, RevisionNumber);
        if (!PublicationProtocol.IsRecordId(PublishedRecordKind.Statement, RecordId) || RevisionNumber < 1 ||
            string.IsNullOrWhiteSpace(Headline) || string.IsNullOrWhiteSpace(Summary))
            throw new InvalidDataException("Published record identity or wording is invalid.");
        PublishedEvidenceRules.RequireVersion(Source);
        if (Quotations is null || Quotations.Length is 0 or > MaximumQuotations)
            throw new InvalidDataException($"Published statement requires 1 to {MaximumQuotations} quotations.");
        foreach (var quotation in Quotations) ValidateQuotation(quotation);
        if ((StatementDate is null) != (StatementDateEvidence is null))
            throw new InvalidDataException("A statement date requires exact evidence.");
        if (StatementDateEvidence is not null)
            ValidateSpan(StatementDateEvidence.ExtractionId, StatementDateEvidence.Start, StatementDateEvidence.Length,
                StatementDateEvidence.Quote);
        ValidateLabels();
    }

    private void ValidateQuotation(PublishedQuotation? quotation)
    {
        if (quotation is null || !Enum.IsDefined(quotation.Kind))
            throw new InvalidDataException("Published quotation is missing or has an unknown kind.");
        ValidateSpan(quotation.ExtractionId, quotation.Start, quotation.Length, quotation.Quote);
    }

    private void ValidateSpan(string extractionId, int start, int length, string quote)
    {
        if (extractionId != Source.ExtractionId)
            throw new InvalidDataException("Published citation must cite the source of the statement.");
        PublishedEvidenceRules.RequireQuote(Source.Text, start, length, quote);
    }

    private void ValidateLabels()
    {
        if (Speakers is null || OtherSpeakers is null || IssueIds is null || PolicyAreaIds is null || BillNumbers is null)
            throw new InvalidDataException("Published statement speakers or labels are missing.");
        if (Speakers.Length == 0)
            throw new InvalidDataException("Published statement requires at least one speaker.");
        if (Speakers.Any(item => item is null || string.IsNullOrWhiteSpace(item.Name)))
            throw new InvalidDataException("Published statement speakers require a name.");
        if (!PublishedEvidenceRules.AreDistinctAndNotBlank(Speakers.Select(item => item.Id)))
            throw new InvalidDataException("Published statement speaker ids must be distinct and not blank.");
        foreach (var (name, labels) in new[]
        {
            ("other speakers", OtherSpeakers), ("issue ids", IssueIds), ("policy area ids", PolicyAreaIds), ("bill numbers", BillNumbers)
        })
            if (!PublishedEvidenceRules.AreDistinctAndNotBlank(labels))
                throw new InvalidDataException($"Published statement {name} must be distinct and not blank.");
    }
}
