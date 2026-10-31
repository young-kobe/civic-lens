using System.Text.Json.Serialization;

namespace CivicLens.Publication.Contracts;

/// <summary>
/// How the two major parties voted on one substantive roll call. Split: the party majorities took opposite sides. PartiesAgree: both
/// majorities took the same side. Mixed: both parties voted, but one has no majority, as in a tie.
/// </summary>
[JsonConverter(typeof(PublishedEnumJsonConverter))]
public enum PublishedVoteAlignment { Split, PartiesAgree, Mixed }
