using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>The substantive votes of one kind in one chamber and Congress that have a party alignment, counted by alignment.</summary>
public sealed record KindAgreement(PublishedVoteKind Kind, int Agreed, int Split, int Mixed)
{
    public int Total => Agreed + Split + Mixed;
}
