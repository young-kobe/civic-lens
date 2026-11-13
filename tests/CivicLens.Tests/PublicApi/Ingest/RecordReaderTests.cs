using CivicLens.PublicApi.Ingest;

namespace CivicLens.Tests.PublicApi.Ingest;

/// <summary>The text bound and the NUL check that the typed reader applies to every record.</summary>
public sealed class RecordReaderTests
{
    private static readonly SampleRecord Sample = PublishedSamples.Statement('a', "Rent cap");

    [Fact]
    public void SourceTextStopsAtTheLimitSoTheSearchVectorStaysUnderThePostgresSizeLimit()
    {
        var text = "Xq " + new string('w', IndexedRecord.SourceLimit);

        var read = RecordReader.Create(Sample.Entry, [], [text]);

        Assert.Equal(text[..IndexedRecord.SourceLimit], read.Source);
    }

    [Fact]
    public void TheSourceLimitDoesNotSplitASurrogatePairBecauseAHalfPairIsNotValidText()
    {
        var text = new string('w', IndexedRecord.SourceLimit - 1) + "\U0001F600";

        var read = RecordReader.Create(Sample.Entry, [], [text]);

        Assert.Equal(text[..(IndexedRecord.SourceLimit - 1)], read.Source);
    }

    [Fact]
    public void ARecordWithANulCharacterIsRefusedBecausePostgresCannotStoreIt()
    {
        var read = RecordReader.Create(Sample.Entry, ["A short\0account."], []);

        Assert.Throws<InvalidDataException>(() => RecordReader.Finish(read, null));
    }
}
