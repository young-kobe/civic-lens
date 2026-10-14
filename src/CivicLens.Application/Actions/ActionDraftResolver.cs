using System.Collections.Immutable;
using System.Text.Json;
using CivicLens.Application.Analysis;
using CivicLens.Core.Actions;
using CivicLens.Core.Review;
using static CivicLens.Application.Analysis.AnalysisAnswerRules;

namespace CivicLens.Application.Actions;

public static class ActionDraftResolver
{
    public static AnalysisResolution<ActionDraftRevision> Resolve(string outputJson, ActionDraftEvidence evidence, ActionBillSnapshot snapshot,
        ImmutableArray<string> trackedIssueIds, string issueRevisionId, string runId, DateTimeOffset createdAtUtc)
    {
        var output = Parse(outputJson);
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
        return AnalysisResolution<ActionDraftRevision>.Accept(
            Assemble(output, evidence, snapshot, trackedIssueIds, issueRevisionId, runId, createdAtUtc));
    }

    private static ActionDraftRevision Assemble(ActionDraftOutput output, ActionDraftEvidence evidence, ActionBillSnapshot snapshot,
        ImmutableArray<string> trackedIssueIds, string issueRevisionId, string runId, DateTimeOffset createdAtUtc)
    {
        var current = evidence.Current;
        var candidate = evidence.Candidate;
        var keepsPersonText = current?.TextOrigin == ActionTextOrigin.Person;
        var revision = new ActionDraftRevision(candidate.RecordId, (current?.RevisionNumber ?? 0) + 1, ReviewAuthor.AnalysisSubject,
            createdAtUtc, keepsPersonText ? current!.Headline : output.Headline.Text, keepsPersonText ? current!.Summary : output.Summary.Text,
            keepsPersonText ? ActionTextOrigin.Person : ActionTextOrigin.Model,
            keepsPersonText ? new ActionTextSuggestion(output.Headline.Text, output.Summary.Text, runId, snapshot.Fingerprint) : null,
            candidate.SourceId, candidate.Congress, candidate.BillType, candidate.BillNumber, candidate.CandidateId, candidate.UpdateDate,
            candidate.IncludesSummaries,
            snapshot.Fingerprint, snapshot.SummaryVersion, snapshot.SummaryCitation, snapshot.Bill.PolicyArea, snapshot.PolicyAreaCitation,
            trackedIssueIds, issueRevisionId, snapshot.Sponsorships, snapshot.Bill.Title);
        revision.ValidateAsAnalysisRevision(current);
        return revision;
    }

    private static void CheckNames(string text, string field, ActionBillSnapshot snapshot, List<string> errors)
    {
        foreach (var name in snapshot.ExcludedNames.Where(name => !IsPartOfListedName(name, snapshot)))
            if (text.Contains(name, StringComparison.OrdinalIgnoreCase))
                errors.Add($"The {field} names \"{Shorten(name)}\", who is not an official in the facts. Name only the listed officials.");
    }

    private static bool IsPartOfListedName(string name, ActionBillSnapshot snapshot) =>
        snapshot.OfficialNames.Values.Any(listed => listed.Contains(name, StringComparison.OrdinalIgnoreCase));

    private static ActionDraftOutput? Parse(string outputJson)
    {
        try
        {
            var output = JsonSerializer.Deserialize<ActionDraftOutput>(outputJson, OutputJson);
            return output is { Headline: not null, Summary: not null } ? output : null;
        }
        catch (JsonException) { return null; }
    }
}
