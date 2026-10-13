using CivicLens.Core.Documents;
using CivicLens.Core.Statements;

namespace CivicLens.Application.Statements;

public sealed record PublishableStatement(StatementReview Review, string Url, DocumentExtraction Extraction);
