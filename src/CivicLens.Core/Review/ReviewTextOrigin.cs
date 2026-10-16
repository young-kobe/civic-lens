using System.Text.Json.Serialization;

namespace CivicLens.Core.Review;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ReviewTextOrigin { Model, Person }
