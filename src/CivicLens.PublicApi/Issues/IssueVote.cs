using CivicLens.Publication.Contracts;

namespace CivicLens.PublicApi.Issues;

/// <summary>One substantive roll call on an issue. <c>ResultWords</c> is the plain word for the source result.</summary>
public sealed record IssueVote(string RecordId, DateOnly VotedOn, PublishedChamber Chamber, int Congress, string Headline, string? Label, string ResultWords,
    int Yea, int Nay);
