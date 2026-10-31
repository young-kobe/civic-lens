using System.Text.Json.Serialization;

namespace CivicLens.Publication.Contracts;

/// <summary>How the two major parties voted on one substantive roll call: on opposite sides, on the same side, or neither.</summary>
[JsonConverter(typeof(PublishedEnumJsonConverter))]
public enum PublishedVoteAlignment { PartyLine, Bipartisan, Mixed }
