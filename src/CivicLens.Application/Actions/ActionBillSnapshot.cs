using System.Collections.Immutable;
using CivicLens.Application.Collection.Bills;
using CivicLens.Core.Actions;
using CivicLens.Core.Statements;

namespace CivicLens.Application.Actions;

public sealed record ActionBillSnapshot(CongressBill Bill, ImmutableArray<ActionSponsorship> Sponsorships, string Fingerprint,
    CongressBillSummaryText? NewestSummary, string? SummaryVersion, StatementSpan? SummaryCitation, StatementSpan? PolicyAreaCitation,
    ImmutableDictionary<string, string> OfficialNames, ImmutableArray<string> ExcludedNames);
