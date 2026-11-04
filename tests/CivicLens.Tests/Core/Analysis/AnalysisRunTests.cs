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

    [Fact]
    public void ReadingALegacyRunNeedsNoInputBecauseOldRowsHoldNone()
    {
        Run(AnalysisRunOutcome.Refused, null).Validate();
        Run(AnalysisRunOutcome.Refused, null, Input).Validate();
    }

    [Theory]
    [InlineData(AnalysisRunOutcome.Drafted, true)]
    [InlineData(AnalysisRunOutcome.Refused, true)]
    [InlineData(AnalysisRunOutcome.RateLimited, true)]
    [InlineData(AnalysisRunOutcome.ProcessingFailed, true)]
    [InlineData(AnalysisRunOutcome.Bypassed, false)]
    [InlineData(AnalysisRunOutcome.Interrupted, false)]
    public void ANewRunHoldsItsInputOnlyWhenItCalledTheModelBecauseTheDatabaseCannotTellNewRunsFromOldOnes(AnalysisRunOutcome outcome, bool madeCall)
    {
        var output = outcome == AnalysisRunOutcome.Drafted ? "{}" : null;
        var context = outcome == AnalysisRunOutcome.Interrupted ? null : "{}";
        var withInput = Run(outcome, output) with { ContextJson = context, Input = Input };
        var withoutInput = withInput with { Input = null };

        Assert.Equal(madeCall, Throws(withoutInput));
        Assert.Equal(!madeCall, Throws(withInput));
        (madeCall ? withInput : withoutInput).ValidateForWrite();
    }

    [Theory]
    [InlineData("SystemPrompt")]
    [InlineData("UserMessage")]
    [InlineData("OutputSchema")]
    public void ANewRunRefusesANulCharacterInEachInputTextBecausePostgresTextCannotHoldIt(string field)
    {
        var input = field switch
        {
            "SystemPrompt" => Input with { SystemPrompt = "a\0b" },
            "UserMessage" => Input with { UserMessage = "a\0b" },
            _ => Input with { OutputSchema = "a\0b" }
        };

        Assert.Throws<ArgumentException>((Run(AnalysisRunOutcome.Drafted, "{}", input)).ValidateForWrite);
    }

    [Fact]
    public void ANewRunRefusesANulCharacterInAValidationError()
    {
        var run = Run(AnalysisRunOutcome.OutputRejected, null, Input) with { ValidationErrors = ["bad \0 id"] };

        Assert.Throws<ArgumentException>(run.ValidateForWrite);
        run.Validate();
    }

    [Theory]
    [InlineData("", "user", "schema", "medium", 10)]
    [InlineData("system", "", "schema", "medium", 10)]
    [InlineData("system", "user", "", "medium", 10)]
    [InlineData("system", "user", "schema", "", 10)]
    [InlineData("system", "user", "schema", "an effort name over sixteen characters", 10)]
    [InlineData("system", "user", "schema", "medium", 0)]
    public void ANewRunRefusesAnInputThatTheDatabaseChecksWouldRefuse(string system, string user, string schema, string effort, int maximumTokens)
    {
        var run = Run(AnalysisRunOutcome.Drafted, "{}", new AnalysisRunInput(system, user, schema, effort, maximumTokens));

        Assert.Throws<ArgumentException>(run.ValidateForWrite);
    }

    [Fact]
    public void ANewRunRefusesATextOverTheLimitOfTheTextTables()
    {
        var tooLong = new string('a', AnalysisRunInput.MaximumPromptBytes + 1);

        Assert.Throws<ArgumentException>(Run(AnalysisRunOutcome.Drafted, "{}", Input with { UserMessage = tooLong }).ValidateForWrite);
        Run(AnalysisRunOutcome.Drafted, "{}", Input with { UserMessage = tooLong[1..] }).ValidateForWrite();
    }

    private static readonly AnalysisRunInput Input = new("system", "user", "{}", "medium", 100);

    private static bool Throws(AnalysisRun run)
    {
        try { run.ValidateForWrite(); return false; }
        catch (ArgumentException) { return true; }
    }

    private static AnalysisRun Run(AnalysisRunOutcome outcome, string? output, AnalysisRunInput? input = null) => new(new string('a', 32), new string('b', 64),
        "draft", "task-v1", "claude-haiku-5-5", "prompt-v1", "schema-v1", new string('c', 64), null, outcome,
        AnalysisTokenUsage.None, 0, "end_turn", null, output, [], "{}", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, input);
}
