using CivicLens.Application.Statements;
using static CivicLens.Tests.Fixtures.StatementGateFixtures;

namespace CivicLens.Tests.Application.Statements;

public sealed class StatementGateResolverTests
{
    private const string Article = "Rent caps help tenants. Rent caps also help landlords plan.";

    [Fact]
    public void AUniqueQuoteIsLocatedByCodeAndMatchesTheArticleText()
    {
        var evidence = Evidence(Article);

        var result = StatementGateResolver.Resolve(Output([HousingArea], 0.5, (RentIssue, "help tenants")), evidence, Rubric());

        Assert.Equal(StatementGateReasons.Passed, result.Reason);
        var quote = Assert.Single(result.Quotes);
        Assert.Equal((evidence.Extraction.ExtractionId, Article.IndexOf("help tenants", StringComparison.Ordinal), 12),
            (quote.ExtractionId, quote.Start, quote.Length));
    }

    [Theory]
    [InlineData("rent cap freeze", "does not occur")]
    [InlineData("Rent caps", "more than once")]
    public void AQuoteThatIsMissingOrAmbiguousRejectsTheWholeAnswerAsACitationError(string quote, string reason)
    {
        var result = StatementGateResolver.Resolve(Output([], 0.5, (RentIssue, quote)), Evidence(Article), Rubric());

        Assert.False(result.IsValid);
        Assert.True(result.HasCitationErrors);
        Assert.Contains(result.Errors, error => error.Contains(reason, StringComparison.Ordinal));
    }

    [Fact]
    public void AnIssueOutsideTheCaptureDateRangeIsNotListedSoItsIdIsRejected()
    {
        var result = StatementGateResolver.Resolve(Output([], 0.5, (RetiredIssue, "help tenants")), Evidence(Article), Rubric());

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("not a listed ID", StringComparison.Ordinal));
    }

    [Fact]
    public void AnUnlistedPolicyAreaAndAnOutOfRangeConfidenceAreRejected()
    {
        var result = StatementGateResolver.Resolve(Output(["made-up"], 1.5), Evidence(Article), Rubric());

        Assert.False(result.IsValid);
        Assert.False(result.HasCitationErrors);
        Assert.Equal(2, result.Errors.Length);
    }

    [Fact]
    public void ARepeatedIssueAndMalformedJsonAreRejected()
    {
        var repeated = StatementGateResolver.Resolve(Output([], 0.5, (RentIssue, "help tenants"), (RentIssue, "plan")), Evidence(Article), Rubric());
        var malformed = StatementGateResolver.Resolve("{\"trackedIssues\":1}", Evidence(Article), Rubric());

        Assert.Contains(repeated.Errors, error => error.Contains("repeats", StringComparison.Ordinal));
        Assert.False(malformed.IsValid);
    }

    [Fact]
    public void AnAnswerWithNoTrackedIssueIsASkipEvenWhenPolicyAreasMatch()
    {
        var result = StatementGateResolver.Resolve(Output([HealthArea]), Evidence(Article), Rubric());

        Assert.Equal(StatementGateReasons.NoTrackedIssue, result.Reason);
        Assert.Empty(result.Quotes);
    }
}
