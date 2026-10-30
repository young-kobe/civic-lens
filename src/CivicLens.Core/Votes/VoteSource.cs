using System.Text.Json.Serialization;

namespace CivicLens.Core.Votes;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum VoteSource { CongressGov, Voteview }
