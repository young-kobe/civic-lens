using CivicLens.Application.Collection;
using CivicLens.Infrastructure.Collection;
using static CivicLens.Tests.Fixtures.CollectionFixtures;

namespace CivicLens.Tests.Infrastructure.Collection;

public sealed class FileCollectionReceiptHandoffStoreTests
{
    [Fact]
    public async Task SavesAtomicallyListsAndLoadsStrictVersionedEnvelope()
    {
        var root = TempDirectory();
        try
        {
            var store = new FileCollectionReceiptHandoffStore(root);
            var request = Request();
            var handoff = new PendingCollectionHandoff(1, "attempt-1", request, Receipt(request));
            await store.SaveAsync(handoff, CancellationToken.None);

            Assert.Equal(new[] { "attempt-1" }, await store.ListAsync(CancellationToken.None));
            var loaded = await store.LoadAsync("attempt-1", CancellationToken.None);
            Assert.Equal(handoff.Request, loaded.Request);
            Assert.Equal(
                System.Text.Json.JsonSerializer.Serialize(handoff, CivicLens.Collection.Contracts.CollectionProtocol.JsonOptions),
                System.Text.Json.JsonSerializer.Serialize(loaded, CivicLens.Collection.Contracts.CollectionProtocol.JsonOptions));
            Assert.Empty(Directory.GetFiles(Path.Combine(root, ".pending"), ".tmp-*"));
            await Assert.ThrowsAsync<IOException>(() => store.SaveAsync(handoff, CancellationToken.None));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ListsCorruptionWithoutParsingAndRetainsItForRecoveryReporting()
    {
        var root = TempDirectory();
        try
        {
            var store = new FileCollectionReceiptHandoffStore(root);
            var spool = Path.Combine(root, ".pending");
            await File.WriteAllTextAsync(Path.Combine(spool, "attempt-1.json"), "{");
            await File.WriteAllTextAsync(Path.Combine(spool, "bad id.json"), "unsafe");

            Assert.Equal(new[] { "attempt-1", "bad id" }, await store.ListAsync(CancellationToken.None));
            await Assert.ThrowsAsync<InvalidDataException>(() => store.LoadAsync("attempt-1", CancellationToken.None));
            await Assert.ThrowsAsync<InvalidDataException>(() => store.LoadAsync("bad id", CancellationToken.None));
            Assert.True(File.Exists(Path.Combine(spool, "attempt-1.json")));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task RejectsUnsupportedEnvelopeVersionAndSerializesRecoveryLeases()
    {
        var root = TempDirectory();
        try
        {
            var store = new FileCollectionReceiptHandoffStore(root);
            var request = Request();
            var handoff = new PendingCollectionHandoff(2, "attempt-1", request, Receipt(request));
            await File.WriteAllTextAsync(Path.Combine(root, ".pending", "attempt-1.json"),
                System.Text.Json.JsonSerializer.Serialize(handoff, CivicLens.Collection.Contracts.CollectionProtocol.JsonOptions));
            await Assert.ThrowsAsync<InvalidDataException>(() => store.LoadAsync("attempt-1", CancellationToken.None));

            await using var lease = await store.AcquireRecoveryLeaseAsync(CancellationToken.None);
            using var cancelled = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.AcquireRecoveryLeaseAsync(cancelled.Token).AsTask());
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task RejectsSymlinkedHandoffFile()
    {
        var root = TempDirectory();
        var outside = Path.Combine(TempDirectory(), "receipt.json");
        try
        {
            await File.WriteAllTextAsync(outside, "{}");
            var store = new FileCollectionReceiptHandoffStore(root);
            File.CreateSymbolicLink(Path.Combine(root, ".pending", "attempt-1.json"), outside);
            await Assert.ThrowsAsync<InvalidDataException>(() => store.LoadAsync("attempt-1", CancellationToken.None));
        }
        finally
        {
            Directory.Delete(root, true);
            Directory.Delete(Path.GetDirectoryName(outside)!, true);
        }
    }

    [Theory]
    [InlineData("missingVersion")]
    [InlineData("duplicateVersion")]
    [InlineData("nullRequest")]
    [InlineData("identityMismatch")]
    [InlineData("oversized")]
    public async Task RejectsInvalidEnvelopeWithoutRemovingIt(string invalidity)
    {
        var root = TempDirectory();
        try
        {
            var store = new FileCollectionReceiptHandoffStore(root);
            var request = Request();
            var handoff = new PendingCollectionHandoff(1, "attempt-1", request, Receipt(request));
            var json = System.Text.Json.JsonSerializer.Serialize(handoff,
                CivicLens.Collection.Contracts.CollectionProtocol.JsonOptions);
            var node = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
            switch (invalidity)
            {
                case "missingVersion": node.Remove("version"); json = node.ToJsonString(); break;
                case "duplicateVersion": json = json.Replace("\"version\":1", "\"version\":1,\"version\":1"); break;
                case "nullRequest": node["request"] = null; json = node.ToJsonString(); break;
                case "identityMismatch": node["attemptId"] = "different-attempt"; json = node.ToJsonString(); break;
                case "oversized": json = new string(' ', 1_048_577); break;
            }
            var path = Path.Combine(root, ".pending", "attempt-1.json");
            await File.WriteAllTextAsync(path, json);

            await Assert.ThrowsAsync<InvalidDataException>(() => store.LoadAsync("attempt-1", CancellationToken.None));
            Assert.True(File.Exists(path));
        }
        finally { Directory.Delete(root, true); }
    }

    private static string TempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "civic-handoff-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
