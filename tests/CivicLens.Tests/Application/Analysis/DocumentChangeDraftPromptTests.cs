using System.Text;
using System.Text.Json;
using CivicLens.Application.Analysis;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Application.Analysis;

public sealed class DocumentChangeDraftPromptTests
{
    private static readonly DocumentChangeDraftCatalog Catalog = new([new("warren", "Elizabeth Warren")], ["housing"]);

    [Fact]
    public void TheSameEvidenceRebuildsTheSameRequestAndHash()
    {
        var evidence = FakeDocumentChangeDraftModel.Evidence("Deadline: October 15.\n", "Deadline: October 30.\n");
        var first = DocumentChangeDraftPrompt.Build(evidence, Catalog)!;
        var second = DocumentChangeDraftPrompt.Build(evidence, Catalog)!;
        Assert.Equal(first, second);
        Assert.Matches("^[0-9a-f]{64}$", first.InputHash);
        Assert.Equal(DocumentChangeDraftTask.Model, first.Model);

        var retry = DocumentChangeDraftPrompt.Build(evidence, Catalog, ["Citation 0: the quote does not occur."])!;
        Assert.NotEqual(first.InputHash, retry.InputHash);
        Assert.Contains("Citation 0: the quote does not occur.", retry.UserPrompt, StringComparison.Ordinal);
        Assert.NotEqual(first.InputHash, DocumentChangeDraftPrompt.Build(evidence, Catalog with { IssueIds = ["energy"] })!.InputHash);
    }

    [Fact]
    public void TheReservationIsAnUpperBoundOfPromptBytesPlusTheOutputAllowance()
    {
        var evidence = FakeDocumentChangeDraftModel.Evidence("Fee: 10 dollars.\n", "Fee: éé 12 dollars.\n");
        var request = DocumentChangeDraftPrompt.Build(evidence, Catalog)!;
        var bytes = Encoding.UTF8.GetByteCount(request.SystemPrompt) + Encoding.UTF8.GetByteCount(request.UserPrompt) +
            Encoding.UTF8.GetByteCount(request.SchemaJson);
        Assert.Equal(bytes + DocumentChangeDraftTask.MaximumOutputTokens, request.ReservedTokens);
    }

    [Fact]
    public void TheSchemaLimitsHunkIdsAndSelectionsToTheEvidenceAndCatalog()
    {
        var evidence = FakeDocumentChangeDraftModel.Evidence("A.\nkeep\nB.\n", "A2.\nkeep\nB2.\n");
        using var schema = JsonDocument.Parse(DocumentChangeDraftPrompt.Build(evidence, Catalog)!.SchemaJson);
        var properties = schema.RootElement.GetProperty("properties");
        var hunkIds = properties.GetProperty("citations").GetProperty("items").GetProperty("properties").GetProperty("hunkId")
            .GetProperty("enum").EnumerateArray().Select(item => item.GetString()!).ToArray();
        Assert.Equal(["h1", "h2"], hunkIds);
        var official = properties.GetProperty("officialIds").GetProperty("items");
        Assert.Equal(["warren"], official.GetProperty("properties").GetProperty("id").GetProperty("enum")
            .EnumerateArray().Select(item => item.GetString()!));
        Assert.Equal("number", official.GetProperty("properties").GetProperty("confidence").GetProperty("type").GetString());
        Assert.Equal("number", properties.GetProperty("confidence").GetProperty("type").GetString());
    }

    [Fact]
    public void InputOverTheHunkOrByteBoundIsRefusedInsteadOfTruncated()
    {
        var manyHunks = string.Concat(Enumerable.Range(0, 65).Select(index => $"line {index}\nsame {index}\n"));
        var changed = string.Concat(Enumerable.Range(0, 65).Select(index => $"edit {index}\nsame {index}\n"));
        Assert.Null(DocumentChangeDraftPrompt.Build(FakeDocumentChangeDraftModel.Evidence(manyHunks, changed), Catalog));

        var large = FakeDocumentChangeDraftModel.Evidence("Short.\n", new string('x', 6_000) + "\n" + string.Concat(
            Enumerable.Range(0, 20).Select(index => $"{index}\n{new string('y', 6_000)}\n")));
        Assert.Null(DocumentChangeDraftPrompt.Build(large, Catalog));
    }
}
