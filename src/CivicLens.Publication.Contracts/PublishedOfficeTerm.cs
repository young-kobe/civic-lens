namespace CivicLens.Publication.Contracts;

/// <summary>One dated term as the roster gives it. The office is sen, rep, prez, or viceprez. A president has no state or district.</summary>
public sealed record PublishedOfficeTerm
{
    public required string Office { get; init; }
    public string? State { get; init; }
    public int? District { get; init; }
    public required string Party { get; init; }
    public required DateOnly Start { get; init; }
    public required DateOnly End { get; init; }

    public void Validate()
    {
        if (!PublishedEvidenceRules.IsBoundedText(Office) || !PublishedEvidenceRules.IsBoundedText(Party) || (State is not null && !PublishedEvidenceRules.IsBoundedText(State)) ||
            District < 0 || End < Start)
            throw new InvalidDataException("Published office term is invalid.");
    }
}
