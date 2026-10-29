using System.Globalization;

namespace CivicLens.Publication.Contracts;

/// <summary>A presidential document as the Federal Register lists it. The headline is the official title. Code approves the record.</summary>
public sealed record PublishedExecutiveAction
{
    public const int MaximumHeadlineLength = 1_000;
    public const int MaximumFacts = 16;
    public const int MaximumSources = 1;
    public const string DocumentNumberLabel = "Federal Register document";
    public const string TitleLabel = "Title";
    public const string SignedLabel = "Signed";
    public const string PublishedLabel = "Published";
    public const string CitationLabel = "Citation";
    public const string CorrectionOfLabel = "Corrects document";
    public const string UrlLabel = "Document URL";

    public required string RecordId { get; init; }
    public required int RevisionNumber { get; init; }
    public required string Headline { get; init; }
    public required PublishedExecutiveActionKind Kind { get; init; }
    public required string DocumentNumber { get; init; }
    public string? CorrectionOf { get; init; }
    public required DateOnly SignedOn { get; init; }
    public required DateOnly PublishedOn { get; init; }
    public required string FederalRegisterCitation { get; init; }
    public required string DocumentUrl { get; init; }
    public required PublishedOfficial President { get; init; }
    public required PublishedExecutiveActionFact[] Facts { get; init; }
    public required PublishedDocumentVersion[] Sources { get; init; }
    public required DateTimeOffset ApprovedAtUtc { get; init; }
    public required DateTimeOffset FirstPublishedAtUtc { get; init; }
    public required PublishedCorrection[] Corrections { get; init; }

    public PublishedRecordEntry ToEntry() => new()
    {
        Kind = PublishedRecordKind.ExecutiveAction,
        RecordId = RecordId,
        RevisionNumber = RevisionNumber,
        Headline = Headline,
        FirstPublishedAtUtc = FirstPublishedAtUtc,
        Officials = [President],
        IssueIds = [],
        PolicyAreaIds = [],
        SourceHost = Sources[0].Host,
        EventDate = SignedOn
    };

    public IEnumerable<PublishedCitation> Citations() => Facts.Select(fact => fact.Citation);

    public void Validate()
    {
        PublishedCorrection.ValidateAll(Corrections, RevisionNumber);
        if (!PublicationProtocol.IsRecordId(PublishedRecordKind.ExecutiveAction, RecordId) || RevisionNumber < 1 ||
            string.IsNullOrWhiteSpace(Headline) || Headline.Length > MaximumHeadlineLength || !Enum.IsDefined(Kind))
            throw new InvalidDataException("Published record identity or wording is invalid.");
        if (President?.Id is null || string.IsNullOrWhiteSpace(President.Id) || string.IsNullOrWhiteSpace(President.Name))
            throw new InvalidDataException("A published executive action names its President.");
        PublishedEvidenceRules.RequireSources(Sources, MaximumSources, "executive action");
        if (Facts is null || Facts.Length is 0 or > MaximumFacts || Facts.Any(fact => fact?.Citation is null ||
                string.IsNullOrWhiteSpace(fact.Label) || fact.Citation.Quote != $"{fact.Label}: {fact.Value}"))
            throw new InvalidDataException($"A published executive action holds 1 to {MaximumFacts} facts that equal their citations.");
        foreach (var citation in Citations()) PublishedEvidenceRules.RequireCitation(Sources, citation, "executive action");
        PublishedEvidenceRules.RequireEverySourceCited(Sources, Citations(), "executive action");
        RequireFact(DocumentNumberLabel, DocumentNumber);
        RequireFact(TitleLabel, Headline);
        RequireFact(SignedLabel, Day(SignedOn));
        RequireFact(PublishedLabel, Day(PublishedOn));
        RequireFact(CitationLabel, FederalRegisterCitation);
        RequireFact(UrlLabel, DocumentUrl);
        if (CorrectionOf is not null) RequireFact(CorrectionOfLabel, CorrectionOf);
        else if (Facts.Any(fact => fact.Label == CorrectionOfLabel))
            throw new InvalidDataException("A published executive action that corrects no document has no correction fact.");
    }

    private static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private void RequireFact(string label, string value)
    {
        if (!Facts.Any(fact => fact.Label == label && fact.Value == value))
            throw new InvalidDataException($"A published executive action must cite its {label.ToLowerInvariant()}.");
    }
}
