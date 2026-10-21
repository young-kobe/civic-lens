using System.Text.Json.Serialization;

namespace CivicLens.Publication.Contracts;

[JsonConverter(typeof(PublishedEnumJsonConverter))]
public enum PublishedVoteKind { Rule, PreviousQuestion, MotionToRecommit, Amendment, Suspension, Passage, ConferenceReport, VetoOverride, Nomination, Cloture, MotionToProceed, Other }
