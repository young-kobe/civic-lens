namespace CivicLens.Publication.Contracts;

public sealed record PublishedOfficialProfile
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? WikidataId { get; init; }
    public string? Website { get; init; }
    public PublishedPhoto? Photo { get; init; }
    public required PublishedOfficeTerm[] Terms { get; init; }

    public void Validate()
    {
        if (!PublishedEvidenceRules.IsBoundedText(Id) || !PublishedEvidenceRules.IsBoundedText(Name) || (WikidataId is not null && !PublishedEvidenceRules.IsBoundedText(WikidataId)) ||
            (Website is not null && !PublishedWebsite.IsValid(Website)) ||
            Terms is null || Terms.Length > PublishedOfficials.MaximumTerms || Terms.Any(term => term is null))
            throw new InvalidDataException("Published official is invalid.");
        foreach (var term in Terms) term.Validate();
        Photo?.Validate();
    }

}
