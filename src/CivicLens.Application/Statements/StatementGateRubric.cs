using System.Collections.Immutable;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using CivicLens.Application.Issues;

namespace CivicLens.Application.Statements;

public sealed record StatementGateRubric(string RevisionId, ImmutableArray<TrackedIssueConfiguration> Issues,
    ImmutableArray<PolicyAreaConfiguration> PolicyAreas)
{
    private static readonly JsonSerializerOptions CompactJson = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static StatementGateRubric Create(string revisionId, IssueConfiguration configuration, DateOnly capturedOn)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(revisionId);
        ArgumentNullException.ThrowIfNull(configuration);
        return new(revisionId,
            [.. configuration.Issues.Where(issue => IsActiveOn(issue, capturedOn))],
            [.. configuration.PolicyAreas]);
    }

    public object ToDocument() => new
    {
        trackedIssues = Issues.Select(TrackedIssuePromptDocument.Of),
        policyAreas = PolicyAreas.Select(area => new { id = area.Id, name = area.Name })
    };

    public string ToContextJson() => JsonSerializer.Serialize(new { issueRevisionId = RevisionId, rubric = ToDocument() }, CompactJson);

    public static string Serialize(object value) => JsonSerializer.Serialize(value, CompactJson);

    private static bool IsActiveOn(TrackedIssueConfiguration issue, DateOnly date) =>
        (issue.StartsOn is null || issue.StartsOn <= date) && (issue.EndsBefore is null || date < issue.EndsBefore);
}
