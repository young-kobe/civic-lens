using System.Collections.Immutable;
using CivicLens.Application.Analysis;
using CivicLens.Application.Issues;
using CivicLens.Core.Review;
using CivicLens.Core.Votes;
using static CivicLens.Application.Analysis.AnalysisAnswerRules;

namespace CivicLens.Application.Votes;

public static class VoteDraftResolver
{
    /// <summary>Checks the issue choice of the model. Code writes the wording, so the revision holds code text and the chosen issues.</summary>
    public static AnalysisResolution<VoteDraftRevision> ResolveIssueChoice(string outputJson, VoteDraftContext context, DateTimeOffset createdAtUtc)
    {
        var output = IssueChoiceOutput.Parse(outputJson);
        if (output is null)
            return AnalysisResolution<VoteDraftRevision>.Reject(["The output is not valid JSON for the issue schema."], false);
        var errors = new List<string>();
        CheckConfidence(output.Confidence, "confidence", errors);
        var issueIds = context.Issues.Check(output.IssueIds, errors);
        if (errors.Count > 0) return AnalysisResolution<VoteDraftRevision>.Reject(Bound(errors), false);
        return AnalysisResolution<VoteDraftRevision>.Accept(WriteByCode(context, createdAtUtc, issueIds));
    }

    public static VoteDraftRevision WriteByCode(VoteDraftContext context, DateTimeOffset createdAtUtc, ImmutableArray<string> trackedIssueIds = default)
    {
        var candidate = context.Evidence.Candidate;
        return Assemble(context, createdAtUtc, ReviewAuthor.CodeSubject, VoteCodeText.Of(context.Snapshot, candidate.BillType, candidate.BillNumber),
            trackedIssueIds.IsDefault ? [] : trackedIssueIds);
    }

    public static VoteDraftRevision CopyText(VoteDraftContext context, DateTimeOffset createdAtUtc)
    {
        var current = context.Evidence.Current ?? throw new ArgumentException("Copied text needs a current revision.", nameof(context));
        var author = current.AuthorKind == ReviewAuthorKind.Code ? ReviewAuthor.CodeSubject : ReviewAuthor.AnalysisSubject;
        return Assemble(context, createdAtUtc, author, ReviewText.CopyOf(current), [.. current.TrackedIssueIds.Where(context.Issues.CandidateIds.Contains)]);
    }

    private static VoteDraftRevision Assemble(VoteDraftContext context, DateTimeOffset createdAtUtc, string author, ReviewText text,
        ImmutableArray<string> trackedIssueIds)
    {
        var (evidence, snapshot, issues, issueRevisionId) = context;
        var current = evidence.Current;
        var candidate = evidence.Candidate;
        var revision = new VoteDraftRevision(candidate.RecordId, (current?.RevisionNumber ?? 0) + 1, author, createdAtUtc,
            text.Headline, text.Summary, text.Origin, text.Suggestion, candidate.SourceId, snapshot.Source, candidate.Chamber, candidate.Congress, candidate.SessionNumber, candidate.RollCallNumber, candidate.BillType,
            candidate.BillNumber, candidate.CandidateId, candidate.UpdateDate, snapshot.Facts, snapshot.Day, snapshot.VotedOnCitation, snapshot.Kind, snapshot.KindCitation, snapshot.Members, trackedIssueIds, issues.CandidateIds, issueRevisionId, issues.SubjectsHash);
        revision.ValidateAsWorkerRevision(current);
        return revision;
    }
}
