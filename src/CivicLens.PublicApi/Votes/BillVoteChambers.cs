using CivicLens.Publication.Contracts;

namespace CivicLens.PublicApi.Votes;

/// <summary>The chambers that hold at least one roll call vote that names one bill in the active release, in chamber order.</summary>
public sealed record BillVoteChambers(int Release, PublishedChamber[] Chambers);
