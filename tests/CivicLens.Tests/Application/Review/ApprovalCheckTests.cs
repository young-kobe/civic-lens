using CivicLens.Application.Review;

namespace CivicLens.Tests.Application.Review;

public sealed class ApprovalCheckTests
{
    [Fact]
    public void AFailureKeepsTheMessageButNotTheParameterSuffixBecauseAReviewerSeesItAsTheHeldReason()
    {
        var check = ApprovalCheck.Of(() => throw new ArgumentException("A headline is required.", "Headline"));

        Assert.Equal("A headline is required.", check.Failure);
    }

    [Fact]
    public void AFailureWithoutAParameterKeepsItsMessage()
    {
        Assert.Equal("The summary is missing.", ApprovalCheck.Of(() => throw new ArgumentException("The summary is missing.")).Failure);
    }

    [Fact]
    public async Task TheAsyncFormStripsTheSuffixToo()
    {
        var check = await ApprovalCheck.OfAsync(() => throw new ArgumentException("A headline is required.", "Headline"));

        Assert.Equal("A headline is required.", check.Failure);
    }
}
