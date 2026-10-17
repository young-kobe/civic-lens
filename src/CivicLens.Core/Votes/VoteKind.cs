using System.Text.Json.Serialization;

namespace CivicLens.Core.Votes;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum VoteKind { Rule, PreviousQuestion, MotionToRecommit, Amendment, Suspension, Passage, ConferenceReport, VetoOverride, Other }
