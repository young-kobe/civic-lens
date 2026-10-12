using System.Collections.Immutable;
using CivicLens.Application.Issues;

namespace CivicLens.Application.Statements;

public sealed class StatementReviewContextReader(IStatementDraftStore evidence, IssueConfiguration issues)
{
    private readonly StatementSpeakerRoster roster = new();
    private readonly ImmutableHashSet<string> issueIds = [.. issues.Issues.Select(issue => issue.Id)];
    private readonly ImmutableHashSet<string> policyAreaIds = [.. issues.PolicyAreas.Select(area => area.Id)];

    public async Task<StatementReviewContext> ReadAsync(string candidateId, CancellationToken cancellationToken)
    {
        var read = await evidence.ReadEvidenceAsync(candidateId, cancellationToken)
            ?? throw new KeyNotFoundException("Statement candidate was not found.");
        var officials = roster.OfficialsOn(read.ConfigurationRevision, read.Article.Extraction.SourceAttempt.SourceId,
            read.Article.FirstCapturedOn);
        return new(read.Article, new([.. officials.Select(official => official.Id)], issueIds, policyAreaIds));
    }
}
