using System.Collections.Immutable;
using CivicLens.Core.Statements;

namespace CivicLens.Application.Statements;

public sealed record SaveStatementDraftRequest(string CandidateId, int ExpectedRevisionNumber, string Headline, string Summary,
    DateOnly? StatementDate, StatementSpan? StatementDateEvidence, ImmutableArray<string> SpeakerOfficialIds,
    ImmutableArray<string> OtherSpeakers, ImmutableArray<string> IssueIds, ImmutableArray<string> PolicyAreaIds,
    ImmutableArray<StatementSpan> Quotations, string IdempotencyKey, bool StatementDateChecked = false);
