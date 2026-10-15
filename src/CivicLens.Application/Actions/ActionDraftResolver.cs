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

    public static ActionDraftRevision CopyText(ActionDraftEvidence evidence, ActionBillSnapshot snapshot,
        ImmutableArray<string> trackedIssueIds, string issueRevisionId, DateTimeOffset createdAtUtc)
    {
        var current = evidence.Current ?? throw new ArgumentException("Copied text needs a current revision.", nameof(evidence));
        return Assemble(evidence, snapshot, trackedIssueIds, issueRevisionId, createdAtUtc,
            (current.Headline, current.Summary, current.TextOrigin, current.Suggestion));
    }

    private static ActionDraftRevision Assemble(ActionDraftOutput output, ActionDraftEvidence evidence, ActionBillSnapshot snapshot,
        ImmutableArray<string> trackedIssueIds, string issueRevisionId, string runId, DateTimeOffset createdAtUtc)
    {
        var current = evidence.Current;
        var text = current?.TextOrigin == ActionTextOrigin.Person
            ? (current.Headline, current.Summary, ActionTextOrigin.Person,
                new ActionTextSuggestion(output.Headline.Text, output.Summary.Text, runId, snapshot.Fingerprint))
            : (output.Headline.Text, output.Summary.Text, ActionTextOrigin.Model, (ActionTextSuggestion?)null);
        return Assemble(evidence, snapshot, trackedIssueIds, issueRevisionId, createdAtUtc, text);
    }

    private static ActionDraftRevision Assemble(ActionDraftEvidence evidence, ActionBillSnapshot snapshot,
        ImmutableArray<string> trackedIssueIds, string issueRevisionId, DateTimeOffset createdAtUtc,
        (string Headline, string Summary, ActionTextOrigin Origin, ActionTextSuggestion? Suggestion) text)
    {
        var current = evidence.Current;
        var candidate = evidence.Candidate;
        var revision = new ActionDraftRevision(candidate.RecordId, (current?.RevisionNumber ?? 0) + 1, ReviewAuthor.AnalysisSubject,
            createdAtUtc, text.Headline, text.Summary, text.Origin, text.Suggestion,
            candidate.SourceId, candidate.Congress, candidate.BillType, candidate.BillNumber, candidate.CandidateId, candidate.UpdateDate,
            candidate.IncludesSummaries,
            snapshot.Fingerprint, snapshot.SummaryVersion, snapshot.SummaryCitation, snapshot.Bill.PolicyArea, snapshot.PolicyAreaCitation,
            trackedIssueIds, issueRevisionId, snapshot.Sponsorships, snapshot.Bill.Title);
        revision.ValidateAsAnalysisRevision(current);
        return revision;
    }

    private static void CheckNames(string text, string field, ActionBillSnapshot snapshot, List<string> errors)
    {
        foreach (var name in snapshot.ExcludedNames.Where(name => !IsPartOfListedName(name, snapshot.OfficialNames.Values)))
            if (text.Contains(name, StringComparison.OrdinalIgnoreCase))
                errors.Add($"The {field} names \"{Shorten(name)}\", who is not an official in the facts. Name only the listed officials.");
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

    private static bool IsPartOfListedName(string name, IEnumerable<string> listedNames) =>
        listedNames.Any(listed => listed.Contains(name, StringComparison.OrdinalIgnoreCase));

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
