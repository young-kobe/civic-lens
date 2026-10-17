using CivicLens.Core.Documents;

namespace CivicLens.Application.Relationships;

public sealed record StatementPassage(SourceSpan Span, string Text);
