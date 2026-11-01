using System.Collections.Immutable;
using CivicLens.Application.Collection.Bills;
using CivicLens.Core.Actions;
using CivicLens.Core.Documents;

namespace CivicLens.Application.Actions;

public sealed record ActionBillSnapshot(CongressBill Bill, ImmutableArray<ActionSponsorship> Sponsorships, string Fingerprint,
    ImmutableArray<string> Subjects,
    CongressBillSummaryText? NewestSummary, string? SummaryVersion, SourceSpan? SummaryCitation, SourceSpan? PolicyAreaCitation,
    ImmutableDictionary<string, string> OfficialNames, ImmutableArray<string> ExcludedNames, SourceSpan BillCitation);
