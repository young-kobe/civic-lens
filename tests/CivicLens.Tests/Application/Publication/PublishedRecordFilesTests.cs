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

        // A statement record has one Source field and no source maximum constant, so one source stands for it.
        foreach (var (kind, sources) in new[] { ("action", PublishedAction.MaximumSources), ("vote", PublishedVote.MaximumSources), ("statement", 1) })
            Assert.True(sources * (long)DocumentExtraction.MaximumTextLength * escapedBytesPerCharacter <= PublishedRecordFiles.MaximumCitedRecordBytes, kind);
    }
}
