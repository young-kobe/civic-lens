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

        var revision = StatementSourceText.Read(evidence.Article.Extraction)!.Revision(evidence, [Warren], DateTimeOffset.UnixEpoch);

        Assert.Equal((ShortRelease, "civic-lens:code", (DateOnly?)null), (revision.Summary, revision.AuthorSubject, revision.StatementDate));
        Assert.Equal(evidence.CarryOver.IssueIds, revision.IssueIds);
        revision.ValidateAsWorkerRevision();
        revision.ValidateAgainst(evidence.Article.Extraction);
        Assert.Throws<ArgumentException>(() => (revision with { Summary = "A shorter text." }).ValidateAgainst(evidence.Article.Extraction));
    }

    private static string Words(int count) => string.Join(' ', Enumerable.Repeat("word", count));
}
