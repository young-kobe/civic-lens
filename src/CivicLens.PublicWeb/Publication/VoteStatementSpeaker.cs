namespace CivicLens.PublicWeb.Publication;

/// <summary>A speaker of a linked statement. The page finds the cast of the speaker on this vote from the vote members, so a speaker with no vote row shows no cast.</summary>
public sealed record VoteStatementSpeaker(string OfficialId, string Name);
