namespace CivicLens.Application.Publication;

/// <summary>The action on the bill of a vote, which its page links, and the bill line of that action.</summary>
public sealed record VoteBillLink(string ActionRecordId, string? Line);
