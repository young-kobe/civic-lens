using CivicLens.Core.Analysis;

namespace CivicLens.Tests.Core.Analysis;

public sealed class AnalysisRunTests
{
    [Fact]
    public void ARunCannotChargeLessThanTheTokensItUsed()
    {
        var run = Run(AnalysisRunOutcome.Drafted, "{}") with { Usage = new(100, 50, 0, 0), ChargedTokens = 149 };
        Assert.Throws<ArgumentException>(run.Validate);
        (run with { ChargedTokens = 150 }).Validate();
    }

    [Fact]
    public void RejectedRunsKeepTheirReasonsAndOtherRunsKeepNone()
    {
        Assert.Throws<ArgumentException>(Run(AnalysisRunOutcome.CitationRejected, "{}").Validate);
        (Run(AnalysisRunOutcome.CitationRejected, "{}") with { ValidationErrors = ["Citation 0: the quote does not occur."] })
            .Validate();
        Assert.Throws<ArgumentException>((Run(AnalysisRunOutcome.Drafted, "{}") with { ValidationErrors = ["unexpected"] }).Validate);
    }

    [Fact]
    public void OnlyAnInterruptedRunCanOmitTheContextThatRebuildsItsPrompt()
    {
        Assert.Throws<ArgumentException>((Run(AnalysisRunOutcome.Refused, null) with { ContextJson = null }).Validate);
        (Run(AnalysisRunOutcome.Interrupted, null) with { ContextJson = null }).Validate();
        Assert.Throws<ArgumentException>(Run(AnalysisRunOutcome.Interrupted, null).Validate);
    }

    [Fact]
    public void ADraftedRunMustKeepItsStructuredOutput()
    {
        Assert.Throws<ArgumentException>(Run(AnalysisRunOutcome.Drafted, null).Validate);
    }

    private static AnalysisRun Run(AnalysisRunOutcome outcome, string? output) => new(new string('a', 32), new string('b', 64),
        "draft", "task-v1", "claude-haiku-5-5", "prompt-v1", "schema-v1", new string('c', 64), null, outcome,
        AnalysisTokenUsage.None, 0, "end_turn", null, output, [], "{}", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
}
