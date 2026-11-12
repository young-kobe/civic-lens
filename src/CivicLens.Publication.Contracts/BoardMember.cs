namespace CivicLens.Publication.Contracts;

/// <summary>One official on a board. Cast counts the split votes with a Yea or Nay cast. Missed is null when the official has no roll calls in the chamber. PagePath is the official page of the Congress of the board.</summary>
public sealed record BoardMember(OfficialListing Official, int Cast, int Breaks, PublishedAttendanceSummary? Missed, string PagePath)
{
    public int Matched => Cast - Breaks;

    public double BreakShare => Breaks / (double)Cast;

    public double MatchShare => Matched / (double)Cast;
}
