using System.Collections.Immutable;
using CivicLens.Application.Review;
using CivicLens.Core.Documents;

namespace CivicLens.Application.Statements;

public sealed record SaveStatementDraftRequest(string CandidateId, int ExpectedRevisionNumber, string Headline, string Summary,
    DateOnly? StatementDate, SourceSpan? StatementDateEvidence, ImmutableArray<string> SpeakerOfficialIds,
    ImmutableArray<string> OtherSpeakers, ImmutableArray<string> IssueIds, ImmutableArray<string> PolicyAreaIds,
    ImmutableArray<SourceSpan> Quotations, string IdempotencyKey, bool StatementDateChecked = false, ApprovalWithSave? Approval = null);
