using System.Text.Json.Serialization;

namespace CivicLens.Core.Votes;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum VoteKind { Rule, PreviousQuestion, MotionToRecommit, MotionToTable, MotionToDischarge, Amendment, Suspension, Passage, ConferenceReport, VetoOverride, Nomination, Cloture, MotionToProceed, Other }
