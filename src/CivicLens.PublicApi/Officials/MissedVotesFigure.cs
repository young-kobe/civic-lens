using CivicLens.Publication.Contracts;

namespace CivicLens.PublicApi.Officials;

/// <summary>
/// The roll calls that the official missed in one chamber and one Congress. <c>Held</c> is the base: the roll calls of that chamber and Congress
/// in the active release that list the official. A roll call is missed when the cast is Not Voting.
/// </summary>
public sealed record MissedVotesFigure(PublishedChamber Chamber, int Congress, int Missed, int Held, DateOnly FirstVotedOn, DateOnly LastVotedOn, string Scope);
