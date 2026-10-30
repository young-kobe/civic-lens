using System.Text.Json.Serialization;
using CivicLens.Core.Documents;

namespace CivicLens.Core.ExecutiveActions;

public sealed record ExecutiveActionFact(string Label, string Value, SourceSpan Citation)
{
    [JsonIgnore]
    public string Text => ExecutiveActionFacts.Line(Label, Value);
}
