using System.Text.Json.Serialization;
using CivicLens.Core.Documents;

namespace CivicLens.Core.ExecutiveActions;

/// <summary>A correction notice of the Federal Register. The citation is the notice line in the extraction of the correction document.</summary>
public sealed record ExecutiveActionNotice(string DocumentNumber, DateOnly PublishedOn, string Url, SourceSpan Citation)
{
    [JsonIgnore]
    public string Value => ExecutiveActionFacts.NoticeValue(DocumentNumber, PublishedOn, Url);

    [JsonIgnore]
    public string Text => ExecutiveActionFacts.Line(ExecutiveActionFacts.CorrectionNotice, Value);
}
