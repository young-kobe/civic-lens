using System.Collections.Immutable;
using CivicLens.Core.Documents;

namespace CivicLens.Application.Review;

public sealed record SaveDocumentChangeDraftRequest(string DraftId, int ExpectedRevisionNumber,
    string Headline, string Summary, string? Significance, string? Limits, string Institution,
    DateOnly? ChangeDate, SourceSpan? ChangeDateEvidence,
    ImmutableArray<string> OfficialIds, ImmutableArray<string> IssueIds,
    ImmutableArray<SourceSpan> Citations, string IdempotencyKey,
    bool ChangeDateChecked = false, ApprovalWithSave? Approval = null);
