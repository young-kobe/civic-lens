using System.Text.Json;
using CivicLens.Application.Collection;
using CivicLens.Collection.Contracts;
using static CivicLens.Tests.Fixtures.CollectionFixtures;

namespace CivicLens.Tests.Application.Collection;

public sealed class PendingCollectionHandoffTests
{
    [Fact]
    public void EnvelopeShapeMatchesPinnedVersionBecauseSavedReceiptsMustStayRecoverable()
    {
        var request = Request();
        var handoff = new PendingCollectionHandoff(PendingCollectionHandoff.CurrentVersion, "attempt-1", request, Receipt(request));

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(handoff, CollectionProtocol.JsonOptions));
        var names = string.Join(",", json.RootElement.EnumerateObject().Select(property => property.Name));

        Assert.Equal(1, PendingCollectionHandoff.CurrentVersion);
        Assert.True(names == "version,attemptId,request,receipt",
            "The receipt handoff envelope changed. Bump PendingCollectionHandoff.CurrentVersion, update the version " +
            "table in docs/architecture.md, and keep reading older envelopes. Actual: " + names);
    }

    [Fact]
    public void UnsupportedVersionIsRejectedSoOldSpoolFilesAreNeverMisread()
    {
        var request = Request();
        var handoff = new PendingCollectionHandoff(PendingCollectionHandoff.CurrentVersion + 1, "attempt-1", request, Receipt(request));

        Assert.Throws<InvalidDataException>(handoff.Validate);
    }
}
