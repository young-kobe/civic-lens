using System.Collections.Immutable;
using CivicLens.Core.Review;

namespace CivicLens.Application.Review;

public sealed record SaveDocumentChangeDraftRequest(string DraftId, int ExpectedRevisionNumber,
    string Headline, string Summary, string? Significance, string? Limits, string Institution,
    DateOnly? ChangeDate, DocumentChangeCitation? ChangeDateEvidence,
    ImmutableArray<string> OfficialIds, ImmutableArray<string> IssueIds,
    ImmutableArray<DocumentChangeCitation> Citations, string IdempotencyKey);
