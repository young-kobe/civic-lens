using System.Collections.Immutable;
using System.Text.Json.Nodes;
using CivicLens.Application.Analysis;
using CivicLens.Application.Collection.Bills;
using static CivicLens.Application.Analysis.AnalysisAnswerRules;

namespace CivicLens.Application.Issues;

public sealed class BillIssueChoice
{
    private readonly ImmutableArray<TrackedIssueConfiguration> candidates;

    private readonly ImmutableArray<string> subjects;

    private BillIssueChoice(ImmutableArray<TrackedIssueConfiguration> candidates, IEnumerable<string> subjects)
    {
        this.candidates = candidates;
        CandidateIds = [.. candidates.Select(issue => issue.Id)];
        this.subjects = candidates.IsEmpty ? [] : [.. subjects.Distinct(StringComparer.Ordinal)];
    }

    public ImmutableArray<string> CandidateIds { get; }

    public static BillIssueChoice For(string? policyArea, IEnumerable<string> subjects, DateOnly day, IssueConfiguration issues)
    {
        var ids = BillGate.TrackedIssueIdsFor(policyArea, day, issues);
        return new([.. ids.Select(id => issues.Issues.Single(issue => issue.Id == id))], subjects);
    }

    public string Instructions(string evidence) => $"""

        Issues:
        - issueIds: the ids of the candidate issues that the bill is about. Choose from the candidate issues only. List each id once. The policy area of the bill is a hint and does not make an issue fit. Read the definition, the includes, and the excludes of each candidate, and choose an issue only when {evidence} is about it. Use an empty list when no candidate fits.{SubjectInstructions}
        """;

    public IEnumerable<object> PromptSection() => candidates.Select(TrackedIssuePromptDocument.Of);

    public bool HasSubjects => !subjects.IsEmpty;

    public BillIssueChoice WithoutSubjects() => new(candidates, []);

    public IReadOnlyList<string>? SubjectsSection() => subjects.IsEmpty ? null : subjects;

    private string SubjectInstructions => subjects.IsEmpty
        ? ""
        : " The facts list subjects that Congress.gov gives the bill. A subject helps you see what the bill is about. A subject does not make an issue fit. Subjects only inform the issue choice. Do not use the wording of a subject in the headline or the summary.";

    public JsonObject? AnswerSchema() => candidates.IsEmpty ? null : new()
    {
        ["type"] = "array",
        ["items"] = AnalysisSchema.IdString(CandidateIds)
    };

    public ImmutableArray<string> Check(IReadOnlyList<string>? chosen, List<string> errors)
    {
        if (chosen is null) return CheckMissing(errors);
        foreach (var id in chosen.Where(id => !CandidateIds.Contains(id)).Distinct(StringComparer.Ordinal))
            errors.Add($"issueIds lists \"{Shorten(id)}\", which is not a candidate issue. Choose only from the candidate issues.");
        foreach (var id in chosen.GroupBy(id => id, StringComparer.Ordinal).Where(group => group.Count() > 1).Select(group => group.Key))
            errors.Add($"issueIds lists \"{Shorten(id)}\" more than once. List each issue once.");
        return [.. chosen.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
    }

    private ImmutableArray<string> CheckMissing(List<string> errors)
    {
        if (!candidates.IsEmpty) errors.Add("issueIds is required. Use an empty list when no candidate issue is about the bill.");
        return [];
    }
}
