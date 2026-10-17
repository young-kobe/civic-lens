using System.Security.Cryptography;
using System.Text;
using CivicLens.Core.Statements;

namespace CivicLens.Tests.Core.Statements;

public sealed class GateLabelSplitTests
{
    [Fact]
    public void TheSameCandidateAlwaysLandsOnTheSameSideSoALaterRunCannotLeakATestReleaseIntoTraining()
    {
        var candidates = Enumerable.Range(0, 200).Select(CandidateId).ToArray();

        var first = candidates.Select(GateLabelSplit.IsHeldOut).ToArray();
        var second = candidates.Select(GateLabelSplit.IsHeldOut).ToArray();

        Assert.Equal(first, second);
    }

    [Fact]
    public void TheSplitIsPinnedForKnownCandidatesSoAChangeToTheRuleFailsHere()
    {
        var heldOut = Enumerable.Range(0, 20).Where(index => GateLabelSplit.IsHeldOut(CandidateId(index))).ToArray();

        Assert.Equal([0, 4, 5, 10, 13, 14], heldOut);
    }

    [Fact]
    public void AboutThirtyPercentOfCandidatesAreHeldOutSoTheTestSetIsLargeEnoughToMeasure()
    {
        var candidates = Enumerable.Range(0, 5000).Select(CandidateId).ToArray();

        var share = candidates.Count(GateLabelSplit.IsHeldOut) / (double)candidates.Length;

        Assert.InRange(share, 0.27, 0.33);
    }

    private static string CandidateId(int index) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("candidate-" + index)));
}
