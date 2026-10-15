using CivicLens.Core.Documents;

namespace CivicLens.Core.Statements;

public sealed record StatementQuotation(SourceSpan Span, StatementQuotationKind Kind);
