using CivicLens.Core.Documents;

namespace CivicLens.Core.Actions;

/// <summary>One sentence of an action revision that quotes the official bill text. The span holds the sentence itself.</summary>
public sealed record ActionTextCitation(ActionTextPart Part, SourceSpan Span);

public enum ActionTextPart { Summary, WhyItMatters }
