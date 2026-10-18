using System.Text.Json.Serialization;

namespace CivicLens.Core.Relationships;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RelationshipType { DescribesOwnBill, StatesPositionOn, ClaimsCreditFor, StatementAboutVote }
