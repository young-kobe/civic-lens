using CivicLens.Application.Statements;
using CivicLens.Core.Documents;
using CivicLens.Core.Statements;
using CivicLens.Tests.Fixtures;
using static CivicLens.Tests.Fixtures.StatementDraftFixtures;

namespace CivicLens.Tests.Application.Statements;

public sealed class StatementSourceTextTests
{
    [Fact]
    public void AReleaseOfExactlyTheWordLimitIsShortAndOneMoreWordIsNotBecauseTheLimitCountsTheSavedText()
    {
        var atLimit = "Warren backs a rent cap\n" + Words(StatementSourceText.MaximumWords - 5);

        Assert.NotNull(StatementSourceText.Read(StatementGateFixtures.Evidence(atLimit).Extraction));
        Assert.Null(StatementSourceText.Read(StatementGateFixtures.Evidence(atLimit + " more").Extraction));
    }

    [Fact]
    public void TheFirstLineIsTheHeadlineAndEachQuotedPassageIsADirectQuotation()
    {
        var extraction = StatementGateFixtures.Evidence(ShortRelease).Extraction;

        var source = StatementSourceText.Read(extraction)!;

        Assert.Equal("Warren backs new tenant protections", source.Headline);
        var quotation = Assert.Single(source.Quotations);
        Assert.Equal(StatementQuotationKind.DirectQuotation, quotation.Kind);
        Assert.Equal("This cap protects families,", ShortRelease.Substring(quotation.Span.Start, quotation.Span.Length));
    }

    [Fact]
    public void AReleaseWithNoQuotedPassageCitesItsBodyAsReleaseTextBecauseARevisionNeedsAQuotation()
    {
        const string text = "Warren backs a rent cap\n\nSenator Warren announced a rent cap for tenants.\n";
        var extraction = StatementGateFixtures.Evidence(text).Extraction;

        var quotation = Assert.Single(StatementSourceText.Read(extraction)!.Quotations);

        Assert.Equal(StatementQuotationKind.ReleaseText, quotation.Kind);
        Assert.Equal("Senator Warren announced a rent cap for tenants.", text.Substring(quotation.Span.Start, quotation.Span.Length));
    }

    [Fact]
    public void AFeedItemKeepsTheModelPathBecauseItsFirstLineIsAFeedLineAndNotItsTitle()
    {
        var page = StatementGateFixtures.Evidence(ShortRelease).Extraction;
        var feedItem = new DocumentExtraction(page.SourceAttempt, "feed-item-statement-v1+html-text-v1", ShortRelease, part: 0);

        Assert.Null(StatementSourceText.Read(feedItem));
    }

    [Fact]
    public void AFirstLineLongerThanAHeadlineKeepsTheModelPath()
    {
        var text = new string('x', StatementDraftRevision.MaximumHeadlineLength + 1) + "\nSenator Warren announced a rent cap.";

        Assert.Null(StatementSourceText.Read(StatementGateFixtures.Evidence(text).Extraction));
    }

    [Fact]
    public void TheCodeRevisionCopiesTheSourceTextAndHasTheCodeAuthorAndNoDate()
    {
        var evidence = DraftEvidence(ShortRelease);

        var revision = StatementSourceText.Read(evidence.Article.Extraction)!.Revision(evidence, Officials(Warren)[0], DateTimeOffset.UnixEpoch);

        Assert.Equal((ShortRelease, "civic-lens:code", (DateOnly?)null), (revision.Summary, revision.AuthorSubject, revision.StatementDate));
        Assert.Equal(evidence.CarryOver.IssueIds, revision.IssueIds);
        revision.ValidateAsWorkerRevision();
        revision.ValidateAgainst(evidence.Article.Extraction);
        Assert.Throws<ArgumentException>(() => (revision with { Summary = "A shorter text." }).ValidateAgainst(evidence.Article.Extraction));
    }

    [Theory]
    [InlineData("\"This cap protects families,\" said Senator Warren.")]
    [InlineData("\"This cap protects families,\" Warren said.")]
    [InlineData("Senator Elizabeth Warren said, \"This cap protects families.\"")]
    [InlineData("\"This cap,\" said Warren, \"protects families.\"")]
    [InlineData("Senator Warren announced a rent cap for tenants.")]
    public void TheCodeRevisionNamesTheOfficialWhenCodeAttributesEveryQuotedPassageToThatOfficial(string body)
    {
        Assert.Equal([Warren], CodeSpeakers("Warren backs a rent cap\n" + body));
    }

    [Theory]
    [InlineData("\"This cap protects families,\" said Jo Rivera.")]
    [InlineData("\"This cap protects families,\" said Jo Rivera and Senator Warren.")]
    [InlineData("\"This cap protects families,\" said Jo Rivera, an ally of Warren.")]
    [InlineData("Senator Warren announced a rent cap. \"This cap protects families.\"")]
    [InlineData("\"This cap protects families,\" said Senator Warren. \"It is unfair,\" said Jo Rivera.")]
    [InlineData("\"This cap protects families,\" Warren said, quoting a tenant: \"We need it.\"")]
    [InlineData("\"This cap protects families,\" said Senator Tim Warren.")]
    [InlineData("\"This cap protects families,\" Jo Warren said.")]
    public void TheCodeRevisionHasNoSpeakerWhenAQuotedPassageMayBeAnotherPersonBecauseCodeCannotNameOtherSpeakers(string body)
    {
        Assert.Empty(CodeSpeakers("Warren backs a rent cap\n" + body));
    }

    private static string[] CodeSpeakers(string text)
    {
        var evidence = DraftEvidence(text);
        return [.. StatementSourceText.Read(evidence.Article.Extraction)!.Revision(evidence, Officials(Warren)[0], DateTimeOffset.UnixEpoch)
            .SpeakerOfficialIds];
    }

    private static string Words(int count) => string.Join(' ', Enumerable.Repeat("word", count));
}
