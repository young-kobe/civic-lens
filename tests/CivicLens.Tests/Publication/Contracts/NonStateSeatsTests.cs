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
        var source = Path.Combine(RepositoryRoot.FullName, "src");
        var owner = Path.Combine(source, "CivicLens.Publication.Contracts", "NonStateSeats.cs");
        var pattern = new Regex("\"Resident Commissioner\"|\"Northern Mariana Islands\"|\"Guam\"");
        string[] extensions = [".cs", ".razor", ".js"];

        var copies = Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)
            .Where(file => extensions.Contains(Path.GetExtension(file)) && file != owner)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(file => pattern.IsMatch(File.ReadAllText(file)));

        Assert.Empty(copies);
    }
}
