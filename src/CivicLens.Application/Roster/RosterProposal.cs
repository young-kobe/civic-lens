using CivicLens.Application.Collection;

namespace CivicLens.Application.Roster;

public sealed record RosterProposal(ProposedMember[] Joins, PersonConfiguration[] Leaves);
