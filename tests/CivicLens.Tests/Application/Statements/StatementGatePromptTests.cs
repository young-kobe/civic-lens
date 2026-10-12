using CivicLens.Application.Statements;
using static CivicLens.Tests.Fixtures.StatementGateFixtures;

namespace CivicLens.Tests.Application.Statements;

public sealed class StatementGatePromptTests
{
    private const int Cap = StatementGateTask.MaximumPromptBytes;

    [Fact]
    public void TwoArticlesWithTheSameCaptureDateIssueSetShareOneByteIdenticalCachedPrefix()
    {
        var first = StatementGatePrompt.Build(Evidence("Article one about rent.", Observed), Rubric(Observed), Cap)!;
        var second = StatementGatePrompt.Build(Evidence("A different, longer article two.", Observed.AddDays(10), "https://example.test/other"),
            Rubric(Observed.AddDays(10)), Cap)!;

        Assert.Equal(first.SystemPrompt, second.SystemPrompt);
        Assert.Equal(first.SchemaJson, second.SchemaJson);
        Assert.NotEqual(first.InputHash, second.InputHash);
        Assert.True(first.CacheSystemPrompt && second.CacheSystemPrompt);
        Assert.DoesNotContain("Article one", first.SystemPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("example.test", first.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains("Article one about rent.", first.UserPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyIssuesActiveOnTheCaptureDateAreInThePromptAndSchema()
    {
        var march = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);

        var early = StatementGatePrompt.Build(Evidence("Text.", march), Rubric(march), Cap)!;
        var mid = StatementGatePrompt.Build(Evidence("Text.", Observed), Rubric(Observed), Cap)!;
        var late = StatementGatePrompt.Build(Evidence("Text.", new DateTimeOffset(2026, 12, 2, 0, 0, 0, TimeSpan.Zero)),
            Rubric(new DateTimeOffset(2026, 12, 2, 0, 0, 0, TimeSpan.Zero)), Cap)!;

        Assert.Contains(RetiredIssue, early.SystemPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain(FutureIssue, early.SystemPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain(RetiredIssue, mid.SystemPrompt + mid.SchemaJson, StringComparison.Ordinal);
        Assert.DoesNotContain(FutureIssue, mid.SystemPrompt + mid.SchemaJson, StringComparison.Ordinal);
        Assert.Contains(FutureIssue, late.SchemaJson, StringComparison.Ordinal);
        Assert.DoesNotContain(RetiredIssue, late.SchemaJson, StringComparison.Ordinal);
    }

    [Fact]
    public void AnArticleOverTheByteCapGetsNoRequest()
    {
        Assert.Null(StatementGatePrompt.Build(Evidence(new string('x', 2_000)), Rubric(), 1_000));
    }

    [Fact]
    public void AChangedIssueDefinitionChangesTheRevisionAndThePrompt()
    {
        var changed = StatementGateRubric.Create(Revision("Rules on eviction.").Id, Configuration("Rules on eviction."), DateOnly.FromDateTime(Observed.UtcDateTime));

        Assert.NotEqual(Rubric().RevisionId, changed.RevisionId);
        Assert.NotEqual(StatementGatePrompt.Build(Evidence("Text."), Rubric(), Cap)!.SystemPrompt,
            StatementGatePrompt.Build(Evidence("Text."), changed, Cap)!.SystemPrompt);
    }
}
