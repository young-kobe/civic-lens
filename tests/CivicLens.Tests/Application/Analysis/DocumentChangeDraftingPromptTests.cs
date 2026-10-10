using System.Text;
using System.Text.Json;
using CivicLens.Application.Analysis;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Application.Analysis;

public sealed class DocumentChangeDraftingPromptTests
{
    private static readonly DocumentChangeAnalysisCatalog Catalog = new([new("warren", "Elizabeth Warren")], ["housing"]);

    [Fact]
    public void TheSameEvidenceRebuildsTheSameRequestAndHash()
    {
        var evidence = FakeDocumentChangeDraftingModel.Evidence("Deadline: October 15.\n", "Deadline: October 30.\n");
        var first = DocumentChangeDraftingPrompt.Build(evidence, Catalog)!;
        var second = DocumentChangeDraftingPrompt.Build(evidence, Catalog)!;
        Assert.Equal(first, second);
        Assert.Matches("^[0-9a-f]{64}$", first.InputHash);
        Assert.Equal(DocumentChangeDraftingTask.Model, first.Model);

        var retry = DocumentChangeDraftingPrompt.Build(evidence, Catalog, ["Citation 0: the quote does not occur."])!;
        Assert.NotEqual(first.InputHash, retry.InputHash);
        Assert.Contains("Citation 0: the quote does not occur.", retry.UserPrompt, StringComparison.Ordinal);
        Assert.NotEqual(first.InputHash, DocumentChangeDraftingPrompt.Build(evidence, Catalog with { IssueIds = ["energy"] })!.InputHash);
    }

    [Fact]
    public void TheReservationIsAnUpperBoundOfPromptBytesPlusTheOutputAllowance()
    {
        var evidence = FakeDocumentChangeDraftingModel.Evidence("Fee: 10 dollars.\n", "Fee: éé 12 dollars.\n");
        var request = DocumentChangeDraftingPrompt.Build(evidence, Catalog)!;
        var bytes = Encoding.UTF8.GetByteCount(request.SystemPrompt) + Encoding.UTF8.GetByteCount(request.UserPrompt) +
            Encoding.UTF8.GetByteCount(request.SchemaJson);
        Assert.Equal(bytes + DocumentChangeDraftingTask.MaximumOutputTokens, request.ReservedTokens);
    }

    [Fact]
    public void TheSchemaLimitsHunkIdsAndSelectionsToTheEvidenceAndCatalog()
    {
        var evidence = FakeDocumentChangeDraftingModel.Evidence("A.\nkeep\nB.\n", "A2.\nkeep\nB2.\n");
        using var schema = JsonDocument.Parse(DocumentChangeDraftingPrompt.Build(evidence, Catalog)!.SchemaJson);
        var properties = schema.RootElement.GetProperty("properties");
        var hunkIds = properties.GetProperty("citations").GetProperty("items").GetProperty("properties").GetProperty("hunkId")
            .GetProperty("enum").EnumerateArray().Select(item => item.GetString()!).ToArray();
        Assert.Equal(["h1", "h2"], hunkIds);
        Assert.Equal(["warren"], properties.GetProperty("officialIds").GetProperty("items").GetProperty("enum")
            .EnumerateArray().Select(item => item.GetString()!));
    }

    [Fact]
    public void InputOverTheHunkOrByteBoundIsRefusedInsteadOfTruncated()
    {
        var manyHunks = string.Concat(Enumerable.Range(0, 65).Select(index => $"line {index}\nsame {index}\n"));
        var changed = string.Concat(Enumerable.Range(0, 65).Select(index => $"edit {index}\nsame {index}\n"));
        Assert.Null(DocumentChangeDraftingPrompt.Build(FakeDocumentChangeDraftingModel.Evidence(manyHunks, changed), Catalog));

        var large = FakeDocumentChangeDraftingModel.Evidence("Short.\n", new string('x', 6_000) + "\n" + string.Concat(
            Enumerable.Range(0, 20).Select(index => $"{index}\n{new string('y', 6_000)}\n")));
        Assert.Null(DocumentChangeDraftingPrompt.Build(large, Catalog));
    }
}
