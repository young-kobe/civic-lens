using CivicLens.Publication.Contracts;

namespace CivicLens.PublicApi.Issues;

/// <summary>One substantive roll call on an issue. <c>ResultWords</c> is the plain word for the source result. <c>Yea</c> and <c>Nay</c> are null when the stored vote holds no member-list counts.</summary>
public sealed record IssueVote(string RecordId, DateOnly VotedOn, PublishedChamber Chamber, int Congress, string Headline, string? Label, string ResultWords,
    int? Yea, int? Nay);
