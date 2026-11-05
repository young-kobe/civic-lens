using System.Text.RegularExpressions;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Publication.Contracts;

public sealed class NonStateSeatsTests
{
    [Fact]
    public void OnlyPuertoRicoHasAResidentCommissionerAndTheOtherFiveSendADelegate()
    {
        Assert.Equal("Resident Commissioner", Assert.Single(NonStateSeats.All, seat => seat.HouseOffice != "Delegate").HouseOffice);
        Assert.Equal("PR", Assert.Single(NonStateSeats.All, seat => seat.HouseOffice == "Resident Commissioner").Code);
        Assert.Equal(["DC", "PR", "GU", "VI", "AS", "MP"], NonStateSeats.All.Select(seat => seat.Code));
    }

    [Fact]
    public void NoOtherSourceFileKeepsItsOwnListOfNonStateNamesOrOffices()
    {
        var owners = new[] { "NonStateSeats.cs" };
        var pattern = new Regex("\"Resident Commissioner\"|\"Northern Mariana Islands\"|\"Guam\"");

        var copies = Directory.EnumerateFiles(Path.Combine(RepositoryRoot.FullName, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !owners.Contains(Path.GetFileName(file)))
            .Where(file => pattern.IsMatch(File.ReadAllText(file)));

        Assert.Empty(copies);
    }
}
