using System.Text.Json.Serialization;

namespace CivicLens.Core.Briefs;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BriefCitationKind { DocumentChange, Statement, Action }
