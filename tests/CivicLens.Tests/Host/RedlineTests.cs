using System.Collections.Immutable;
using CivicLens.Core.Documents;
using CivicLens.Host.Components.Evidence;
using CivicLens.PublicWeb.Components.Evidence;

namespace CivicLens.Tests.Host;

public sealed class RedlineTests
{
    private const string Before = "Visitors must book a tour at least seven days before the visit.";
    private const string After = "Visitors must book a tour at least three days before the visit.";
    private const int ContextStart = 9;

    private static DocumentComparisonHunk Hunk(ImmutableArray<DocumentWordEdit> edits)
    {
        var start = Before.IndexOf("seven", StringComparison.Ordinal);
        return new(start, 5, start, 5, "seven", "three", ContextStart, Before[ContextStart..], ContextStart, After[ContextStart..], edits);
    }

    private static ImmutableArray<DocumentWordEdit> SevenToThree() =>
        [new(Before.IndexOf("seven", StringComparison.Ordinal), 5, After.IndexOf("three", StringComparison.Ordinal), 5)];

    [Fact]
    public void AHunkWhoseContextWindowStartsInsideTheDocumentBuildsTheSameRedlineAsTheWholeText()
    {
        var segments = Redline.Build(Hunk(SevenToThree()));

        Assert.Equal([(RedlineKind.Context, "must book a tour at least "), (RedlineKind.Removed, "seven"), (RedlineKind.Added, "three"), (RedlineKind.Context, " days before the visit.")],
            segments.Select(segment => (segment.Kind, segment.Text)));
    }

    [Fact]
    public void AContextWindowThatDoesNotReachTheChangeAddsNoContext()
    {
        var start = Before.IndexOf("seven", StringComparison.Ordinal);
        var hunk = new DocumentComparisonHunk(start, 5, start, 5, "seven", "three", Before.Length - 10, Before[^10..], After.Length - 10, After[^10..], SevenToThree());

        Assert.Equal([(RedlineKind.Removed, "seven"), (RedlineKind.Added, "three")], Redline.Build(hunk).Select(segment => (segment.Kind, segment.Text)));
    }

    [Fact]
    public void AHunkWithoutWordEditsReplacesItsWholeChangedText()
    {
        var segments = Redline.Build(Hunk([]));

        Assert.Equal([RedlineKind.Context, RedlineKind.Removed, RedlineKind.Added, RedlineKind.Context], segments.Select(segment => segment.Kind));
    }

    [Fact]
    public void TheExcerptKeepsOnlyTheChangeAndNeverTheContext()
    {
        var segments = Redline.Excerpt(Hunk(SevenToThree()), 32);

        Assert.Equal([(RedlineKind.Removed, "seven"), (RedlineKind.Added, "three")], segments.Select(segment => (segment.Kind, segment.Text)));
    }
}
