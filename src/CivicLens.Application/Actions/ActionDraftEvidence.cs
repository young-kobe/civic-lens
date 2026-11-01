using CivicLens.Application.Collection;
using CivicLens.Core.Actions;

namespace CivicLens.Application.Actions;

public sealed record ActionDraftEvidence(ActionDraftCandidate Candidate, CollectionConfigurationRevision ConfigurationRevision,
    IReadOnlyList<ActionDraftPage> Pages, ActionDraftRevision? Current, bool HasNewerCandidate, bool HasOlderPromptDraft)
{
    public int SummaryPageCount => Pages.Count(page => page.Resource == ApiResource.BillSummaries);
}
