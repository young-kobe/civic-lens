using System.Text.Json;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Publication.Contracts;

public sealed class PublishedOfficialSummariesTests
{
    [Fact]
    public void ASummaryFileKeepsItsRowsThroughAJsonRoundTrip()
    {
        var file = new PublishedOfficialSummaries { Officials = [Row("mayor"), PublishedOfficialSummary.Empty("chair")] };

        var read = JsonSerializer.Deserialize<PublishedOfficialSummaries>(
            JsonSerializer.Serialize(file, PublicationProtocol.JsonOptions), PublicationProtocol.JsonOptions)!;
        read.Validate();

        Assert.Equal(119, read.Officials[0].Congress);
        Assert.Equal((12, 3), (read.Officials[0].PartyVotes[0].SplitVotesCast, read.Officials[0].PartyVotes[0].Breaks));
        Assert.Equal(new DateOnly(2025, 1, 3), read.Officials[0].Attendance[0].First);
        Assert.Null(read.Officials[1].Congress);
    }

    public static TheoryData<PublishedOfficialSummary> InvalidRows() => new()
    {
        Row("mayor") with { Id = " " },
        Row("mayor") with { Congress = 0 },
        Row("mayor") with { Congress = null },
        Row("mayor") with { CosponsoredBills = -1 },
        Row("mayor") with { CosponsoredBills = PublicationProtocol.MaximumRecordsPerRelease + 1 },
        Row("mayor") with { PartyVotes = [Party(PublishedChamber.House, "Democrat", 12, 13)] },
        Row("mayor") with { PartyVotes = [Party(PublishedChamber.House, "Independent", 12, 3)] },
        Row("mayor") with { PartyVotes = [Party(PublishedChamber.House, "Democrat", 12, 3), Party(PublishedChamber.House, "Democrat", 5, 1)] },
        Row("mayor") with { PartyVotes = [.. Enumerable.Repeat(Party(PublishedChamber.House, "Democrat", 1, 0), PublishedOfficialSummary.MaximumRows + 1)] },
        Row("mayor") with { Attendance = [Attendance(PublishedChamber.House, 10, 11)] },
        Row("mayor") with { Attendance = [Attendance(PublishedChamber.House, PublishedOfficialVotes.MaximumVotes + 1, 0)] },
        Row("mayor") with { Attendance = [Attendance(PublishedChamber.House, 10, 2) with { First = new(2025, 6, 1), Last = new(2025, 1, 1) }] },
        Row("mayor") with { Attendance = [Attendance(PublishedChamber.House, 10, 2), Attendance(PublishedChamber.House, 5, 1)] },
        Row("mayor") with { PartyVotes = null! }
    };

    [Theory]
    [MemberData(nameof(InvalidRows))]
    public void ARowIsBoundedAndConsistentSoARankingNeverReadsANumberThatCannotBeTrue(PublishedOfficialSummary row)
    {
        Assert.Throws<InvalidDataException>(new PublishedOfficialSummaries { Officials = [row] }.Validate);
    }

    [Fact]
    public void ARowWithNoCongressHoldsNoVoteRowsBecauseNoVoteFileWasRead()
    {
        PublishedOfficialSummary.Empty("mayor").Validate();
        Assert.Throws<InvalidDataException>((PublishedOfficialSummary.Empty("mayor") with { Attendance = [Attendance(PublishedChamber.House, 1, 0)] }).Validate);
    }

    [Fact]
    public void TheFileRefusesTwoRowsForOneOfficialAndMoreRowsThanTheOfficialsFileAllows()
    {
        Assert.Throws<InvalidDataException>(new PublishedOfficialSummaries { Officials = [Row("mayor"), Row("mayor")] }.Validate);
        Assert.Throws<InvalidDataException>(new PublishedOfficialSummaries
        {
            Officials = [.. Enumerable.Range(0, PublishedOfficials.MaximumOfficials + 1).Select(index => PublishedOfficialSummary.Empty($"person-{index}"))]
        }.Validate);
    }

    [Fact]
    public void AFileOfTheLargestAllowedSizeFitsItsByteLimit()
    {
        var file = new PublishedOfficialSummaries
        {
            Officials = [.. Enumerable.Range(0, PublishedOfficials.MaximumOfficials).Select(index => Row($"{index}".PadLeft(PublishedRecordEntry.MaximumFilterValueLength, 'p')) with
            {
                PartyVotes = [.. new[] { PublishedChamber.House, PublishedChamber.Senate }.SelectMany(chamber => new[] { "Democrat", "Republican" }.Select(party => Party(chamber, party, 5000, 5000)))],
                Attendance = [Attendance(PublishedChamber.House, 5000, 5000), Attendance(PublishedChamber.Senate, 5000, 5000)]
            })]
        };

        file.Validate();
        Assert.True(JsonSerializer.SerializeToUtf8Bytes(file, PublicationProtocol.JsonOptions).Length <= PublishedOfficialSummaries.MaximumBytes);
    }

    private static PublishedOfficialSummary Row(string id) => new()
    {
        Id = id,
        Congress = 119,
        CosponsoredBills = 4,
        PartyVotes = [Party(PublishedChamber.House, "Democrat", 12, 3)],
        Attendance = [Attendance(PublishedChamber.House, 40, 2)]
    };

    private static PublishedPartyVoteSummary Party(PublishedChamber chamber, string party, int cast, int breaks) =>
        new() { Chamber = chamber, Party = party, SplitVotesCast = cast, Breaks = breaks };

    private static PublishedAttendanceSummary Attendance(PublishedChamber chamber, int held, int missed) =>
        new() { Chamber = chamber, Held = held, Missed = missed, First = new(2025, 1, 3), Last = new(2026, 10, 1) };
}
