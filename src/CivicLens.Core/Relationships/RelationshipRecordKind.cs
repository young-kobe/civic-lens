using System.Text.Json.Serialization;

namespace CivicLens.Core.Relationships;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RelationshipRecordKind { Statement, Action, Vote }
