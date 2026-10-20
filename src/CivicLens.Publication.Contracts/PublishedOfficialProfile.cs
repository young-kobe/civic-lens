namespace CivicLens.Publication.Contracts;

public sealed record PublishedOfficialProfile
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? WikidataId { get; init; }
    public PublishedOfficeTerm[] Terms { get; init; } = [];

    public void Validate()
    {
        if (!PublishedEvidenceRules.IsBoundedText(Id) || !PublishedEvidenceRules.IsBoundedText(Name) || (WikidataId is not null && !PublishedEvidenceRules.IsBoundedText(WikidataId)) ||
            Terms is null || Terms.Length > PublishedOfficials.MaximumTerms || Terms.Any(term => term is null))
            throw new InvalidDataException("Published official is invalid.");
        foreach (var term in Terms) term.Validate();
    }

}
