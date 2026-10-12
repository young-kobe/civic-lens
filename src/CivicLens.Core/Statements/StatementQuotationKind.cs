using System.Text.Json.Serialization;

namespace CivicLens.Core.Statements;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum StatementQuotationKind { DirectQuotation, ReleaseText }
