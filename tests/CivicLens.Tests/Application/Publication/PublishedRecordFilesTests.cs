using CivicLens.Application.Publication;
using CivicLens.Core.Documents;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Application.Publication;

public sealed class PublishedRecordFilesTests
{
    [Fact]
    public void TheCitedRecordCapHoldsTheLargestRecordThatTheWriteSideCanProduceBecauseTheLimitsHaveOneOwner()
    {
        const long escapedBytesPerCharacter = 6;
        var largestSourceCount = new[] { PublishedAction.MaximumSources, PublishedVote.MaximumSources, 1 }.Max();

        Assert.True(largestSourceCount * (long)DocumentExtraction.MaximumTextLength * escapedBytesPerCharacter <= PublishedRecordFiles.MaximumCitedRecordBytes);
        Assert.True(PublishedRecordFiles.MaximumCitedRecordBytes > 16 * 1024 * 1024);
    }
}
