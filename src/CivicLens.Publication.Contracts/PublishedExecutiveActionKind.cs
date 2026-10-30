using System.Text.Json.Serialization;

namespace CivicLens.Publication.Contracts;

[JsonConverter(typeof(PublishedEnumJsonConverter))]
public enum PublishedExecutiveActionKind { ExecutiveOrder, Proclamation, Notice, Memorandum }
