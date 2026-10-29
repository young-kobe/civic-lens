namespace CivicLens.Publication.Contracts;

/// <summary>One dated term. The kind groups officials on the officials list. A roster gives the office as its value. A configured office gives its title. An executive has no state or district. A caucus is the party whose caucus the official joins, when it differs from the party. A term with no end is ongoing.</summary>
public sealed record PublishedOfficeTerm
{
    public required string Office { get; init; }
    public required PublishedOfficeKind Kind { get; init; }
    public string? State { get; init; }
    public int? District { get; init; }
    public string? Party { get; init; }
    public string? Caucus { get; init; }
    public required DateOnly Start { get; init; }
    public DateOnly? End { get; init; }

    public void Validate()
    {
        if (!PublishedEvidenceRules.IsBoundedText(Office) || (Party is not null && !PublishedEvidenceRules.IsBoundedText(Party)) || (Caucus is not null && !PublishedEvidenceRules.IsBoundedText(Caucus)) ||
            (State is not null && !PublishedEvidenceRules.IsBoundedText(State)) || District < 0 || End < Start || !Enum.IsDefined(Kind))
            throw new InvalidDataException("Published office term is invalid.");
    }
}
