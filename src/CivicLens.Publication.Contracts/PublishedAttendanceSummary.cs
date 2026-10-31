namespace CivicLens.Publication.Contracts;

/// <summary>The roll calls of one chamber on this site while one official served, how many the official missed, and the first and last day.</summary>
public sealed record PublishedAttendanceSummary
{
    public required PublishedChamber Chamber { get; init; }
    public required int Held { get; init; }
    public required int Missed { get; init; }
    public required DateOnly First { get; init; }
    public required DateOnly Last { get; init; }

    public void Validate()
    {
        if (!Enum.IsDefined(Chamber) || Missed < 0 || Missed > Held || Held > PublishedOfficialVotes.MaximumVotes || First > Last)
            throw new InvalidDataException("Published attendance summary is invalid.");
    }
}
