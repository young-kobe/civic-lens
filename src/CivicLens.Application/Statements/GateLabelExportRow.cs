using System.Collections.Immutable;

namespace CivicLens.Application.Statements;

public sealed record GateLabelExportRow(string CandidateId, string ExtractionId, bool HeldOut, ImmutableArray<string> IssueIds,
    bool NamesBillOrVote, string Text);
