namespace CivicLens.Application.Publication;

/// <summary>One official on a board. Cast counts the split votes with a Yea or Nay cast. Missed is null when the official has no roll calls in the chamber.</summary>
public sealed record BoardMember(OfficialListing Official, int Cast, int Breaks, MissedVotes? Missed)
{
    public double BreakShare => Breaks / (double)Cast;
}
