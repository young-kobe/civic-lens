using CivicLens.Application.Review;
using CivicLens.Core.Documents;
using CivicLens.Core.Relationships;

namespace CivicLens.Application.Relationships;

public sealed record SaveRelationshipDraftRequest(string RecordId, int ExpectedRevisionNumber, RelationshipType Type, string Reason,
    SourceSpan StatementQuote, string TargetFactId, string IdempotencyKey, ApprovalWithSave? Approval = null);
