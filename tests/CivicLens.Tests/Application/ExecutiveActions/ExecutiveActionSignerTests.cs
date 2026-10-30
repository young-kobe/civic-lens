using CivicLens.Application.ExecutiveActions;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Application.ExecutiveActions;

public sealed class ExecutiveActionSignerTests
{
    private static readonly CivicLens.Application.Collection.CollectionConfiguration Configuration = FederalRegisterFixtures.Configuration();

    [Fact]
    public void ThePresidentInOfficeOnTheSigningDateIsTheSigner()
    {
        var order = FederalRegisterFixtures.Document(FederalRegisterFixtures.TrumpOrder);
        var proclamation = FederalRegisterFixtures.Document(FederalRegisterFixtures.BidenProclamation);

        Assert.Equal("donald-trump", ExecutiveActionSigner.PersonIdOf(Configuration, order));
        Assert.Equal("joe-biden", ExecutiveActionSigner.PersonIdOf(Configuration, proclamation));
    }

    [Fact]
    public void OnTheDayOfTheHandoverTheCitedPresidentPicksBetweenTheTwoOffices()
    {
        var biden = FederalRegisterFixtures.Document(FederalRegisterFixtures.BidenProclamation
            .Replace("\"signing_date\":\"2024-11-08\"", "\"signing_date\":\"2025-01-20\""));

        Assert.Equal("joe-biden", ExecutiveActionSigner.PersonIdOf(Configuration, biden));
    }

    [Fact]
    public void ACitedPresidentWhoIsNotInOfficeOnTheSigningDateIsInvalidSoNoRecordNamesTheWrongPerson()
    {
        var mismatch = FederalRegisterFixtures.Document(FederalRegisterFixtures.TrumpOrder
            .Replace("\"signing_date\":\"2025-01-20\"", "\"signing_date\":\"2024-12-01\""));

        Assert.Throws<InvalidDataException>(() => ExecutiveActionSigner.PersonIdOf(Configuration, mismatch));
    }

    [Fact]
    public void ADateWithNoConfiguredPresidentIsInvalid()
    {
        var early = FederalRegisterFixtures.Document(FederalRegisterFixtures.BidenProclamation
            .Replace("\"signing_date\":\"2024-11-08\"", "\"signing_date\":\"2020-11-08\""));

        Assert.Throws<InvalidDataException>(() => ExecutiveActionSigner.PersonIdOf(Configuration, early));
    }
}
