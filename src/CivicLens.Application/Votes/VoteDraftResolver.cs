using CivicLens.Application.Analysis;
using CivicLens.Core.Review;
using CivicLens.Core.Votes;
using static CivicLens.Application.Analysis.AnalysisAnswerRules;

namespace CivicLens.Application.Votes;

public static class VoteDraftResolver
{
    public static AnalysisResolution<VoteDraftRevision> Resolve(string outputJson, VoteDraftContext context, string runId,
        DateTimeOffset createdAtUtc)
    {
        var output = HeadlineSummaryOutput.Parse(outputJson);
        if (output is null)
            return AnalysisResolution<VoteDraftRevision>.Reject(["The output is not valid JSON for the vote schema."], false);
        var snapshot = context.Snapshot;
        var errors = new List<string>();
        CheckText(output.Headline.Text, VoteDraftRevision.MaximumHeadlineLength, "headline", required: true, errors);
        CheckText(output.Summary.Text, VoteDraftRevision.MaximumSummaryLength, "summary", required: true, errors);
        CheckConfidence(output.Headline.Confidence, "headline confidence", errors);
        CheckConfidence(output.Summary.Confidence, "summary confidence", errors);
        CheckNoNames(output.Headline.Text, "headline", snapshot, errors);
        CheckNoNames(output.Summary.Text, "summary", snapshot, errors);
        if (errors.Count > 0) return AnalysisResolution<VoteDraftRevision>.Reject(Bound(errors), false);
        return AnalysisResolution<VoteDraftRevision>.Accept(Assemble(context, createdAtUtc, ReviewText.AfterModelDraft(context.Evidence.Current, output.Headline.Text,
            output.Summary.Text, runId, snapshot.Fingerprint)));
    }

    public static VoteDraftRevision CopyText(VoteDraftContext context, DateTimeOffset createdAtUtc)
    {
        var current = context.Evidence.Current ?? throw new ArgumentException("Copied text needs a current revision.", nameof(context));
        return Assemble(context, createdAtUtc, ReviewText.CopyOf(current));
    }

    private static void CheckNoNames(string text, string field, VoteSnapshot snapshot, List<string> errors)
    {
        foreach (var name in snapshot.PersonNames.Where(name => text.Contains(name, StringComparison.OrdinalIgnoreCase)))
            errors.Add($"The {field} names \"{Shorten(name)}\". Code lists the tracked members and their votes, so name no person.");
    }

    private static VoteDraftRevision Assemble(VoteDraftContext context, DateTimeOffset createdAtUtc, ReviewText text)
    {
        var (evidence, snapshot, trackedIssueIds, issueRevisionId) = context;
        var current = evidence.Current;
        var candidate = evidence.Candidate;
        var revision = new VoteDraftRevision(candidate.RecordId, (current?.RevisionNumber ?? 0) + 1, ReviewAuthor.AnalysisSubject, createdAtUtc,
            text.Headline, text.Summary, text.Origin, text.Suggestion, candidate.SourceId, candidate.Chamber, candidate.Congress, candidate.SessionNumber, candidate.RollCallNumber, candidate.BillType,
            candidate.BillNumber, candidate.CandidateId, candidate.UpdateDate, snapshot.Facts, snapshot.Day, snapshot.VotedOnCitation, snapshot.Kind, snapshot.KindCitation, snapshot.Members, trackedIssueIds, issueRevisionId);
        revision.ValidateAsAnalysisRevision(current);
        return revision;
    }
}
