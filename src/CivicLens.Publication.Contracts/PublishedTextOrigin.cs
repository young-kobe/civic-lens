using System.Text.Json.Serialization;

namespace CivicLens.Publication.Contracts;

[JsonConverter(typeof(PublishedEnumJsonConverter))]
public enum PublishedTextOrigin { Model, Person }
