using CivicLens.Application.Districts;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Application.Districts;

public sealed class ZctaDistrictParserTests
{
    [Fact]
    public void ParseKeepsOneRowForEachZctaAndDistrictAndDropsBlankZeroLandAndNoDistrictRows()
    {
        Assert.Equal(
        [
            new ZctaDistrict("36009", "AL", 1, 4938464),
            new ZctaDistrict("36009", "AL", 2, 412306983),
            new ZctaDistrict("99501", "AK", 0, 17227675),
            new ZctaDistrict("20001", "DC", 0, 5302364),
            new ZctaDistrict("94505", "CA", 14, 1679865)
        ], DistrictFixtures.Rows());
    }

    [Fact]
    public void StateAndDistrictMatchTheTermFormOfTheRosterSoALaterJoinFindsTheMember()
    {
        var terms = RosterFixtures.Members().SelectMany(member => member.Terms).Where(term => term.District is not null)
            .Select(term => (term.State, term.District)).ToHashSet();

        var rows = DistrictFixtures.Rows().Where(row => row.State is "DC" or "CA");

        Assert.All(rows, row => Assert.Contains((row.State, (int?)row.District), terms));
    }

    [Fact]
    public void AnAtLargeDistrictAndADelegateDistrictBothReadAsDistrictZeroLikeTheRoster()
    {
        var rows = DistrictFixtures.Rows();

        Assert.Equal(0, rows.Single(row => row.State == "AK").District);
        Assert.Equal(0, rows.Single(row => row.State == "DC").District);
    }

    [Fact]
    public void AHeaderThatDiffersFromTheCensusHeaderIsRejected()
    {
        var text = DistrictFixtures.Text.Replace("AREALAND_PART|AREAWATER_PART", "AREALAND_PART|AREAWATER", StringComparison.Ordinal);

        Assert.Throws<InvalidDataException>(() => ZctaDistrictParser.Parse(DistrictFixtures.Bytes(text)));
    }

    [Fact]
    public void AnUnknownStateFipsCodeIsRejectedEvenWhenTheRowIsOtherwiseValid()
    {
        var text = DistrictFixtures.Text + DistrictFixtures.Row("9901", "12345", 10) + "\n";

        Assert.Throws<InvalidDataException>(() => ZctaDistrictParser.Parse(DistrictFixtures.Bytes(text)));
    }

    [Theory]
    [InlineData("1|0101|too|few|columns")]
    [InlineData("1|0101|Congressional District|1|2|G5200|N|3|3600|ZCTA5 3600|4|5|G6350|B5|S|10|0")]
    [InlineData("1|0101|Congressional District|1|2|G5200|N|3|3601X|ZCTA5 3601X|4|5|G6350|B5|S|10|0")]
    [InlineData("1|0101|Congressional District|1|2|G5200|N|3|36012|ZCTA5 36012|4|5|G6350|B5|S|ten|0")]
    [InlineData("1|0101|Congressional District|1|2|G5200|N|3|36012|ZCTA5 36012|4|5|G6350|B5|S|-10|0")]
    [InlineData("1|01|Congressional District|1|2|G5200|N|3|36012|ZCTA5 36012|4|5|G6350|B5|S|10|0")]
    [InlineData("1|01AB|Congressional District|1|2|G5200|N|3|36012|ZCTA5 36012|4|5|G6350|B5|S|10|0")]
    public void AMalformedRowIsRejected(string row)
    {
        var text = DistrictFixtures.Text + row + "\n";

        Assert.Throws<InvalidDataException>(() => ZctaDistrictParser.Parse(DistrictFixtures.Bytes(text)));
    }

    [Fact]
    public void AZctaAndDistrictListedTwiceAreRejected()
    {
        var text = DistrictFixtures.Text + DistrictFixtures.Row("0101", "36009", 5) + "\n";

        Assert.Throws<InvalidDataException>(() => ZctaDistrictParser.Parse(DistrictFixtures.Bytes(text)));
    }

    [Fact]
    public void AFileWithOnlyDroppedRowsIsRejectedSoAnEmptySnapshotIsNeverStored()
    {
        var text = "﻿" + ZctaDistrictParser.Header + "\n" + DistrictFixtures.Row("0101", "36011", 0) + "\n";

        Assert.Throws<InvalidDataException>(() => ZctaDistrictParser.Parse(DistrictFixtures.Bytes(text)));
    }
}
