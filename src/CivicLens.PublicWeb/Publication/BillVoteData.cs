using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

/// <summary>One published roll call that names the bill. The counts are null when a cast is a candidate name.</summary>
public sealed record BillVoteData(
    string RecordId, PublishedChamber Chamber, DateOnly VotedOn, string Headline, string ResultWords,
    int? Yea, int? Nay, int? NotVoting, PublishedVoteAlignment? Alignment);
