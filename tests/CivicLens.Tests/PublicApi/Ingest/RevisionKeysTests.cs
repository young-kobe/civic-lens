using CivicLens.Publication.Contracts;
using CivicLens.PublicApi.Ingest;

namespace CivicLens.Tests.PublicApi.Ingest;

public sealed class RevisionKeysTests
{
    [Fact]
    public void OneSetHoldsAtMostTheBoundOfKeysForOneStatement()
    {
        ListedRecord[] Keys(int count) => [.. Enumerable.Range(0, count).Select(n => new ListedRecord(PublishedRecordKind.Action, n.ToString("x64"), 1))];

        _ = new RevisionKeys(Keys(RevisionKeys.MaxCount));

        Assert.Throws<ArgumentException>(() => new RevisionKeys(Keys(RevisionKeys.MaxCount + 1)));
    }
}
