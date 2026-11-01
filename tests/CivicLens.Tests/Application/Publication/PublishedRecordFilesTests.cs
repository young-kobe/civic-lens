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

        // A statement record has one Source field and a document change has a before and an after text, so they have no source maximum constant. A brief holds no extraction text.
        foreach (var (kind, sources) in new[] { ("action", PublishedAction.MaximumSources), ("executive action", PublishedExecutiveAction.MaximumSources), ("vote", PublishedVote.MaximumSources), ("statement", 1), ("document change", 2) })
            Assert.True(sources * (long)DocumentExtraction.MaximumTextLength * escapedBytesPerCharacter <= PublishedRecordFiles.MaximumCitedRecordBytes, kind);
    }
}
