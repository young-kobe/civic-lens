using System.Text.Json;
using CivicLens.Application.Statements;
using static CivicLens.Tests.Fixtures.StatementDraftFixtures;

namespace CivicLens.Tests.Application.Statements;

public sealed class StatementDraftPromptTests
{
    [Fact]
    public void TheShapeIsChosenByHowManyOfficialsTheSourceCoversAndOnlyTheSeveralShapeAsksForASpeaker()
    {
        var single = Build(Warren);
        var several = Build(Warren, Markey);

        Assert.NotEqual(single.SystemPrompt, several.SystemPrompt);
        Assert.DoesNotContain("speakerOfficialIds", single.SystemPrompt + single.SchemaJson, StringComparison.Ordinal);
        Assert.Contains("speakerOfficialIds", several.SystemPrompt + several.SchemaJson, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSystemPromptHoldsNoValueOfTheArticleAndTheRequestHasNoCacheBreakpoint()
    {
        var first = Build(Warren);
        var second = StatementDraftPrompt.Build(DraftEvidence("Another release, with an unrelated text.\n"), Officials(Warren), 100_000)!;

        Assert.Equal(first.SystemPrompt, second.SystemPrompt);
        Assert.False(first.CacheSystemPrompt || second.CacheSystemPrompt);
    }

    [Fact]
    public void TheUserMessageHoldsTheArticleTheOfficialsAndTheCaptureDateAndMarksNothingTrustedAboutTheText()
    {
        var request = Build(Warren, Markey);

        Assert.Contains("Elizabeth Warren", request.UserPrompt, StringComparison.Ordinal);
        Assert.Contains("\"firstCapturedOn\": \"2026-06-01\"", request.UserPrompt, StringComparison.Ordinal);
        Assert.Contains("untrusted source material", request.SystemPrompt, StringComparison.Ordinal);
        using var schema = JsonDocument.Parse(request.SchemaJson);
        Assert.False(schema.RootElement.GetProperty("properties").TryGetProperty("issueIds", out _));
        Assert.False(schema.RootElement.GetProperty("properties").TryGetProperty("policyAreaIds", out _));
    }

    [Fact]
    public void ARetryAddsTheRejectionReasonsAndAnOverCapPromptIsRefused()
    {
        var retry = StatementDraftPrompt.Build(DraftEvidence(), Officials(Warren), 100_000, ["quotations[0] repeats an earlier quotation."])!;

        Assert.Contains("- quotations[0] repeats an earlier quotation.", retry.UserPrompt, StringComparison.Ordinal);
        Assert.Null(StatementDraftPrompt.Build(DraftEvidence(), Officials(Warren), 1_000));
    }

    [Fact]
    public void TheReservationIsTheByteCountPlusTheOutputAllowanceAndTheSettingsCapFollowsTheGateRule()
    {
        var request = Build(Warren);
        var bytes = System.Text.Encoding.UTF8.GetByteCount(request.SystemPrompt + request.UserPrompt + request.SchemaJson);

        Assert.Equal(bytes + (long)StatementDraftTask.MaximumOutputTokens, request.ReservedTokens);
        Assert.Equal(100_000, new StatementDraftSettings(10_000_000).PromptByteCap);
        Assert.Equal(34_000, new StatementDraftSettings(10_000_000, 50_000).PromptByteCap);
        Assert.Equal("claude-sonnet-5-5", request.Model);
    }

    private static CivicLens.Application.Analysis.AnalysisModelRequest Build(params string[] officials) =>
        StatementDraftPrompt.Build(DraftEvidence(), Officials(officials), 100_000)!;
}
