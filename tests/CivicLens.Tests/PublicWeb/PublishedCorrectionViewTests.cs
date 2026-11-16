using CivicLens.Publication.Contracts;
using CivicLens.PublicWeb.Publication;

namespace CivicLens.Tests.PublicWeb;

public sealed class PublishedCorrectionViewTests
{
    private static readonly PublishedCorrection SourceUpdate = new()
    {
        ReplacedRevisionNumber = 2,
        PublishedAtUtc = new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero)
    };

    [Theory]
    [InlineData(PublishedTextRewrite.None, "Updated from the source")]
    [InlineData(PublishedTextRewrite.Model, "Summary rewritten by the AI model")]
    [InlineData(PublishedTextRewrite.Code, "Wording rewritten by code")]
    public void TheTitleOfACodeUpdateNamesWhoRewroteTheText(PublishedTextRewrite rewrite, string title)
    {
        Assert.Equal(title, PublishedCorrectionView.Title(SourceUpdate with { TextRewrite = rewrite }));
    }

    [Fact]
    public void APersonCorrectionIsACorrection()
    {
        Assert.Equal("Correction", PublishedCorrectionView.Title(SourceUpdate with { Note = "A person fixed the date." }));
    }

    [Theory]
    [InlineData(PublishedTextRewrite.None, ", 2 Nov 2026. Replaces revision 2.")]
    [InlineData(PublishedTextRewrite.Model, ", 2 Nov 2026. Replaces revision 2. The vote facts did not change.")]
    [InlineData(PublishedTextRewrite.Code, ", 2 Nov 2026. Replaces revision 2. The vote facts did not change.")]
    public void EveryTextRewriteOfAVoteSaysThatTheVoteFactsDidNotChange(PublishedTextRewrite rewrite, string afterTitle)
    {
        Assert.Equal(afterTitle, PublishedCorrectionView.AfterTitle(SourceUpdate with { TextRewrite = rewrite }, PublishedRecordKind.Vote));
    }

    [Fact]
    public void ACodeRewriteOfABillActionShowsTheTitleWithNoVoteFactsTail()
    {
        var rewrite = SourceUpdate with { TextRewrite = PublishedTextRewrite.Code };

        Assert.Equal("Wording rewritten by code", PublishedCorrectionView.Title(rewrite));
        Assert.Equal(", 2 Nov 2026. Replaces revision 2.", PublishedCorrectionView.AfterTitle(rewrite, PublishedRecordKind.Action));
    }
}
