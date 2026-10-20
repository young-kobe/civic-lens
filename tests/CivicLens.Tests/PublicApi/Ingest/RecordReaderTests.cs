using System.Text.Json;
using CivicLens.Application.Publication;
using CivicLens.PublicApi.Ingest;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.PublicApi.Ingest;

public sealed class RecordReaderTests
{
    private static IndexedRecord Read(SampleRecord sample) =>
        RecordReader.Read(sample.Entry, JsonSerializer.SerializeToUtf8Bytes(sample.Body, PublicationProtocol.JsonOptions));

    [Fact]
    public void ADocumentChangeIndexesItsSummaryAsSummaryAndBothVersionsAsSourceTextWithTheNewVersionFirst()
    {
        var read = Read(PublishedSamples.DocumentChange("Rule changed", "The rule changed.", "Newer words.", "Older words."));

        Assert.Equal("The rule changed.", read.Summary);
        Assert.Equal("Newer words.\nOlder words.", read.Source);
    }

    [Fact]
    public void AStatementIndexesItsSummaryAndQuotationsAsSummaryAndItsSourceTextAsSource()
    {
        var read = Read(PublishedSamples.Statement('a', "Rent cap", summary: "A short account.", source: "Xq the source text."));

        Assert.Equal("A short account.\nXq", read.Summary);
        Assert.Equal("Xq the source text.", read.Source);
    }

    [Fact]
    public void AnActionIndexesItsSummaryAndBillTitleAsSummaryAndItsSourcesAsSource()
    {
        var read = Read(PublishedSamples.Action("Doe sponsors a bill", "Doe sponsored it.", "A housing bill", "Sponsor: D000001"));

        Assert.Equal("Doe sponsored it.\nA housing bill", read.Summary);
        Assert.Equal("Sponsor: D000001", read.Source);
    }

    [Fact]
    public void AVoteIndexesItsSummaryAsSummaryAndItsSourcesAsSource()
    {
        var read = Read(PublishedSamples.Vote("House votes", "The House voted.", "Result: Passed"));

        Assert.Equal("The House voted.", read.Summary);
        Assert.EndsWith("Result: Passed", read.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void ABriefIndexesItsParagraphsAndNoSourceText()
    {
        var read = Read(PublishedSamples.Brief("A week of housing", "Warren backed a rent cap."));

        Assert.Equal("Warren backed a rent cap.", read.Summary);
        Assert.Equal("", read.Source);
    }

    [Fact]
    public void ARelationshipIndexesItsReasonAndQuoteAndNoSourceText()
    {
        var read = Read(PublishedSamples.Relationship("Doe backs a cap", "Doe says the bill is hers.", "a rent cap"));

        Assert.Equal("Doe says the bill is hers.\na rent cap", read.Summary);
        Assert.Equal("", read.Source);
    }

    [Fact]
    public void SourceTextStopsAtTheLimitSoTheSearchVectorStaysUnderThePostgresSizeLimit()
    {
        var text = "Xq " + new string('w', IndexedRecord.SourceLimit);

        var read = Read(PublishedSamples.Statement('a', "Rent cap", source: text));

        Assert.Equal(IndexedRecord.SourceLimit, read.Source.Length);
        Assert.Equal(text[..IndexedRecord.SourceLimit], read.Source);
    }

    [Fact]
    public void TheSourceLimitDoesNotSplitASurrogatePairBecauseAHalfPairIsNotValidText()
    {
        var text = new string('w', IndexedRecord.SourceLimit - 1) + "\U0001F600";

        var read = Read(PublishedSamples.Statement('a', "Rent cap", source: text));

        Assert.Equal(IndexedRecord.SourceLimit - 1, read.Source.Length);
    }

    [Fact]
    public void ARecordWithANulCharacterIsRefusedBecausePostgresCannotStoreIt()
    {
        var sample = PublishedSamples.Statement('a', "Rent cap", summary: "A short\0account.");

        Assert.Throws<InvalidDataException>(() => Read(sample));
    }

    [Fact]
    public void ARecordThatBreaksItsContractIsRefusedByTheContractValidation()
    {
        var sample = PublishedSamples.Statement('a', "Rent cap");
        var json = JsonSerializer.Serialize(sample.Body, PublicationProtocol.JsonOptions).Replace("Xq the source text.", "Changed text.", StringComparison.Ordinal);

        Assert.Throws<InvalidDataException>(() => RecordReader.Read(sample.Entry, System.Text.Encoding.UTF8.GetBytes(json)));
    }

    [Fact]
    public void AFileWithAFieldTheContractDoesNotKnowIsRefusedAsAJsonError()
    {
        var sample = PublishedSamples.Statement('a', "Rent cap");
        var json = JsonSerializer.Serialize(sample.Body, PublicationProtocol.JsonOptions).Replace("{", "{\"extra\":1,", StringComparison.Ordinal);

        Assert.ThrowsAny<JsonException>(() => RecordReader.Read(sample.Entry, System.Text.Encoding.UTF8.GetBytes(json)));
    }

    [Fact]
    public void TheRecordFileCapHoldsTheLargestFileThatPublishCanWriteBecauseTheTwoLimitsHaveSeparateOwners()
    {
        Assert.True(ReleaseFeed.MaximumRecordBytes >= PublishedRecordFiles.MaximumCitedRecordBytes);
    }
}
