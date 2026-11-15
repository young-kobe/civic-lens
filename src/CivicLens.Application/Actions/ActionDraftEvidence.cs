using CivicLens.Application.Collection;
using CivicLens.Core.Actions;

namespace CivicLens.Application.Actions;

public sealed record ActionDraftEvidence(ActionDraftCandidate Candidate, CollectionConfigurationRevision ConfigurationRevision,
    IReadOnlyList<ActionDraftPage> Pages, ActionDraftRevision? Current, bool HasNewerCandidate, bool HasStaleDraft, bool IsRejected,
    int ReviewStateVersion, ActionDraftBillText? BillText = null)
{
    /// <summary>Drops the summary pages when they pass the page cap, so the snapshot reads as if the bill had no readable summary.</summary>
    public ActionDraftEvidence WithReadableSummaries() => Pages.Count(page => page.Resource == ApiResource.BillSummaries) > ActionDraftTask.MaximumSummaryPages
        ? this with { Pages = [.. Pages.Where(page => page.Resource != ApiResource.BillSummaries)] }
        : this;
}
