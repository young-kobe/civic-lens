using CivicLens.Application.Analysis;
using CivicLens.Core.Actions;
using CivicLens.Core.Review;
using static CivicLens.Application.Analysis.AnalysisAnswerRules;

namespace CivicLens.Application.Actions;

public static class ActionDraftResolver
{
    public static AnalysisResolution<ActionDraftRevision> Resolve(string outputJson, ActionDraftContext context, string runId,
        DateTimeOffset createdAtUtc)
    {
        var snapshot = context.Snapshot;
        var output = HeadlineSummaryOutput.Parse(outputJson);
        if (output is null)
            return AnalysisResolution<ActionDraftRevision>.Reject(["The output is not valid JSON for the action schema."], false);
        var errors = new List<string>();
        CheckText(output.Headline.Text, ActionDraftRevision.MaximumHeadlineLength, "headline", required: true, errors);
        CheckText(output.Summary.Text, ActionDraftRevision.MaximumSummaryLength, "summary", required: true, errors);
        CheckConfidence(output.Headline.Confidence, "headline confidence", errors);
        CheckConfidence(output.Summary.Confidence, "summary confidence", errors);
        CheckNames(output.Headline.Text, "headline", snapshot, errors);
        CheckNames(output.Summary.Text, "summary", snapshot, errors);
        if (errors.Count > 0) return AnalysisResolution<ActionDraftRevision>.Reject(Bound(errors), false);
        return AnalysisResolution<ActionDraftRevision>.Accept(Assemble(context, createdAtUtc, ReviewText.AfterModelDraft(context.Evidence.Current,
            output.Headline.Text, output.Summary.Text, runId, snapshot.Fingerprint)));
    }

    public static ActionDraftRevision CopyText(ActionDraftContext context, DateTimeOffset createdAtUtc)
    {
        var current = context.Evidence.Current ?? throw new ArgumentException("Copied text needs a current revision.", nameof(context));
        return Assemble(context, createdAtUtc, ReviewText.CopyOf(current));
    }

    private static ActionDraftRevision Assemble(ActionDraftContext context, DateTimeOffset createdAtUtc, ReviewText text)
    {
        var (evidence, snapshot, trackedIssueIds, policyAreaId, issueRevisionId) = context;
        var current = evidence.Current;
        var candidate = evidence.Candidate;
        var revision = new ActionDraftRevision(candidate.RecordId, (current?.RevisionNumber ?? 0) + 1, ReviewAuthor.AnalysisSubject,
            createdAtUtc, text.Headline, text.Summary, text.Origin, text.Suggestion,
            candidate.SourceId, candidate.Congress, candidate.BillType, candidate.BillNumber, candidate.CandidateId, candidate.UpdateDate,
            candidate.IncludesSummaries,
            snapshot.Fingerprint, snapshot.SummaryVersion, snapshot.SummaryCitation, snapshot.Bill.PolicyArea, snapshot.PolicyAreaCitation,
            policyAreaId, trackedIssueIds, issueRevisionId, snapshot.Sponsorships, snapshot.Bill.Title);
        revision.ValidateAsAnalysisRevision(current);
        return revision;
    }

    private static void CheckNames(string text, string field, ActionBillSnapshot snapshot, List<string> errors)
    {
        CheckNamesOutsideFacts(text, field, snapshot.ExcludedNames, snapshot.OfficialNames.Values, errors);
        foreach (var name in CosponsorNamesOf(snapshot).Where(name => !IsPartOfListedName(name, SponsorNamesOf(snapshot))))
            if (text.Contains(name, StringComparison.OrdinalIgnoreCase))
                errors.Add($"The {field} names \"{Shorten(name)}\", a cosponsor. Code lists the cosponsors, so name only the sponsors.");
    }

    private static IEnumerable<string> CosponsorNamesOf(ActionBillSnapshot snapshot)
    {
        var sponsors = SponsorIdsOf(snapshot);
        return snapshot.Sponsorships.Where(item => item.Role == ActionSponsorshipRole.Cosponsor && !sponsors.Contains(item.PersonId))
            .Select(item => snapshot.OfficialNames[item.PersonId]).Distinct(StringComparer.Ordinal);
    }

    private static IEnumerable<string> SponsorNamesOf(ActionBillSnapshot snapshot) =>
        SponsorIdsOf(snapshot).Select(id => snapshot.OfficialNames[id]);

    private static HashSet<string> SponsorIdsOf(ActionBillSnapshot snapshot) =>
        snapshot.Sponsorships.Where(item => item.Role == ActionSponsorshipRole.Sponsor).Select(item => item.PersonId).ToHashSet(StringComparer.Ordinal);
}
