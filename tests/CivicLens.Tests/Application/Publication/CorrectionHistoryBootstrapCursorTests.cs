using CivicLens.Application.Publication;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Application.Publication;

public sealed class CorrectionHistoryBootstrapCursorTests
{
    [Fact]
    public void EncodesAndParsesAKeysetPositionWithTheSameTextKindOrderAsTheQuery()
    {
        var cursor = new CorrectionHistoryBootstrapCursor(42, PublishedRecordKind.Statement, new string('a', 64));

        Assert.Equal(cursor, CorrectionHistoryBootstrapCursor.Parse(cursor.Encode()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-base64")]
    [InlineData("MS4w")]
    public void RejectsMalformedAndNonCanonicalCursors(string value)
    {
        Assert.Throws<ArgumentException>(() => CorrectionHistoryBootstrapCursor.Parse(value));
    }

    [Fact]
    public void RejectsARecordIdThatDoesNotMatchItsKind()
    {
        var value = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"1|1|statement|{new string('a', 32)}"))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        Assert.Throws<ArgumentException>(() => CorrectionHistoryBootstrapCursor.Parse(value));
    }
}
