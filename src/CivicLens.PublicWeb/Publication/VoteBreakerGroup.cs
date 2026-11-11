namespace CivicLens.PublicWeb.Publication;

/// <summary>The tracked members of one major party who voted against most of that party on a split vote.</summary>
public sealed record VoteBreakerGroup(string Party, string Name, VoteMemberData[] Members);
