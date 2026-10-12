using System.Text.Json;
using CivicLens.Application.Statements;
using static CivicLens.Tests.Fixtures.StatementDraftFixtures;

namespace CivicLens.Tests.Application.Statements;

public sealed class StatementDraftResolverTests
{
    private static readonly (string Date, string Quote) StatedDate = ("2026-10-03", "WASHINGTON, October 3, 2026");

    [Fact]
    public void AValidAnswerBecomesRevisionOneByTheAnalysisAuthorWithTheGateIdsAndNoConfidence()
    {
        var resolution = Resolve(Output(date: StatedDate));

        var revision = resolution.Revision!;
        Assert.Empty(resolution.Errors);
        Assert.Equal((1, "civic-lens:analysis"), (revision.RevisionNumber, revision.AuthorSubject));
        Assert.Equal(["rent-affordability"], revision.IssueIds.ToArray());
        Assert.Equal(new DateOnly(2026, 10, 3), revision.StatementDate);
        revision.ValidateAsFirstAnalysisRevision();
    }

    [Theory]
    [InlineData("2026-13-40", "WASHINGTON, October 3, 2026", "yyyy-MM-dd form")]
    [InlineData("October 3, 2026", "WASHINGTON, October 3, 2026", "yyyy-MM-dd form")]
    [InlineData("2026-10-04", "WASHINGTON, October 3, 2026", "does not state 2026-10-04")]
    [InlineData("2026-10-03", "October 3, 2025", "does not occur in the article text")]
    public void ADateThatIsMalformedOrNotStatedByItsExactQuoteIsRejectedAsACitationError(string date, string quote, string problem)
    {
        var resolution = Resolve(Output(date: (date, quote)));

        Assert.Null(resolution.Revision);
        Assert.True(resolution.HasCitationErrors);
        Assert.Contains(resolution.Errors, error => error.Contains(problem, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("1.5")]
    [InlineData("-0.1")]
    public void AConfidenceThatIsNotFiniteOrOutsideZeroToOneIsRejectedAsAFieldError(string confidence)
    {
        var json = Output().Replace("\"confidence\":0.8}", $"\"confidence\":{(confidence == "NaN" ? "\"NaN\"" : confidence)}}}", StringComparison.Ordinal);

        var resolution = Resolve(json);

        Assert.Null(resolution.Revision);
        Assert.False(resolution.HasCitationErrors);
        Assert.Contains("confidence must be a number from 0 to 1.", resolution.Errors);
    }

    [Fact]
    public void AConfidenceInsideAFieldIsCheckedToo()
    {
        var resolution = Resolve(Output(quotes: ["a rent cap for tenants"]).Replace("\"confidence\":0.6", "\"confidence\":7", StringComparison.Ordinal));

        Assert.Contains(resolution.Errors, error => error.StartsWith("quotations[0] confidence", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Nobody Named")]
    [InlineData(" Jo Rivera")]
    public void AnotherSpeakerMustBeAPlainNameThatTheArticleWrites(string name) =>
        Assert.Null(Resolve(Output(otherSpeakers: [name])).Revision);

    [Fact]
    public void ARepeatedPassageAndAnEmptyListOfQuotationsAreRejected()
    {
        Assert.Contains(Resolve(Output(quotes: ["a rent cap", "a rent cap"])).Errors, error => error.Contains("repeats an earlier", StringComparison.Ordinal));
        Assert.Contains("Give 1 to 32 quotations.", Resolve(Output(quotes: [])).Errors);
    }

    [Fact]
    public void SeveralOfficialsNeedAtLeastOneChosenSpeaker() =>
        Assert.Contains("speakerOfficialIds needs at least one listed official.", Resolve(Output(speakers: []), Warren, Markey).Errors);

    [Fact]
    public void SeveralOfficialsRequireTheSpeakerFieldAndRejectARepeatedSpeaker()
    {
        Assert.Contains("speakerOfficialIds is required.", Resolve(Output(), Warren, Markey).Errors);
        Assert.Contains(Resolve(Output(speakers: [Warren, Warren]), Warren, Markey).Errors, error => error.Contains("repeats", StringComparison.Ordinal));
    }

    [Fact]
    public void AnUnknownFieldAndMalformedJsonAreRejectedWithoutRepair()
    {
        var withExtra = JsonSerializer.Serialize(new Dictionary<string, object?>(Fields(null, null, ["a rent cap"], [], 0.8, "h")) { ["extra"] = 1 });

        Assert.Null(Resolve(withExtra).Revision);
        Assert.Contains("The output is not valid JSON for the statement schema.", Resolve("{").Errors);
    }

    private static CivicLens.Application.Statements.StatementDraftResolution Resolve(string json, params string[] officials) =>
        StatementDraftResolver.Resolve(json, DraftEvidence(), Officials(officials.Length == 0 ? [Warren] : officials), DateTimeOffset.UnixEpoch);
}
