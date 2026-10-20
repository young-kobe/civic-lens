using System.Text.Json;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Publication.Contracts;

public sealed class PublishedOfficialsTests
{
    [Fact]
    public void ASchemaFiveReleaseKeepsItsOfficialsAndCatalogThroughAJsonRoundTrip()
    {
        var officials = new PublishedOfficials
        {
            Officials =
            [
                new()
                {
                    Id = "mayor",
                    Name = "Mayor Example",
                    WikidataId = "Q1",
                    Terms = [new() { Office = "sen", State = "WA", District = null, Party = "Democrat", Start = new(2025, 1, 3), End = new(2031, 1, 3) }]
                }
            ]
        };
        var catalog = new PublishedCatalog { Issues = [new() { Id = "housing", Name = "Housing" }], PolicyAreas = [] };

        var officialsBack = JsonSerializer.Deserialize<PublishedOfficials>(
            JsonSerializer.Serialize(officials, PublicationProtocol.JsonOptions), PublicationProtocol.JsonOptions)!;
        var catalogBack = JsonSerializer.Deserialize<PublishedCatalog>(
            JsonSerializer.Serialize(catalog, PublicationProtocol.JsonOptions), PublicationProtocol.JsonOptions)!;
        officialsBack.Validate();
        catalogBack.Validate();

        Assert.Equal(new DateOnly(2025, 1, 3), officialsBack.Officials[0].Terms[0].Start);
        Assert.Equal("Housing", catalogBack.Issues[0].Name);
    }

    [Fact]
    public void TheOfficialsAndCatalogJsonMatchPinnedSchemaVersion()
    {
        var officials = new PublishedOfficials
        {
            Officials =
            [
                new()
                {
                    Id = "mayor",
                    Name = "Mayor Example",
                    Terms = [new() { Office = "sen", State = "WA", Party = "Democrat", Start = new(2025, 1, 3), End = new(2031, 1, 3) }]
                }
            ]
        };
        var catalog = new PublishedCatalog { Issues = [new() { Id = "housing", Name = "Housing" }], PolicyAreas = [] };

        Assert.Equal(5, PublicationProtocol.SchemaVersion);
        Assert.Equal("""{"officials":[{"id":"mayor","name":"Mayor Example","wikidataId":null,"terms":[{"office":"sen","state":"WA","district":null,"party":"Democrat","start":"2025-01-03","end":"2031-01-03"}]}]}""",
            JsonSerializer.Serialize(officials, PublicationProtocol.JsonOptions));
        Assert.Equal("""{"issues":[{"id":"housing","name":"Housing"}],"policyAreas":[]}""",
            JsonSerializer.Serialize(catalog, PublicationProtocol.JsonOptions));
    }

    [Fact]
    public void AConfiguredOfficeHasNoPartyAndNoEndSoTheJsonShowsNullAndTheTermIsOngoing()
    {
        var officials = new PublishedOfficials
        {
            Officials = [new() { Id = "sec", Name = "Secretary Example", Terms = [new() { Office = "Secretary of State", Start = new(2025, 1, 21) }] }]
        };

        officials.Validate();

        Assert.Equal("""{"officials":[{"id":"sec","name":"Secretary Example","wikidataId":null,"terms":[{"office":"Secretary of State","state":null,"district":null,"party":null,"start":"2025-01-21","end":null}]}]}""",
            JsonSerializer.Serialize(officials, PublicationProtocol.JsonOptions));
    }

    [Fact]
    public void TwoOfficialsWithOneIdAreRefusedBecauseAProfilePageNeedsOnePerson()
    {
        var official = new PublishedOfficialProfile { Id = "mayor", Name = "Mayor Example" };

        Assert.Throws<InvalidDataException>(new PublishedOfficials { Officials = [official, official] }.Validate);
    }

    [Fact]
    public void ATermThatEndsBeforeItStartsIsRefused()
    {
        var term = new PublishedOfficeTerm { Office = "prez", Party = "Democrat", Start = new(2025, 1, 20), End = new(2021, 1, 20) };

        Assert.Throws<InvalidDataException>(new PublishedOfficialProfile { Id = "a", Name = "A", Terms = [term] }.Validate);
    }

    [Fact]
    public void TheFilesHaveSizeCapsSoTheirSizeStaysBounded()
    {
        var many = Enumerable.Range(0, PublishedOfficials.MaximumOfficials + 1)
            .Select(index => new PublishedOfficialProfile { Id = $"p{index}", Name = "A" }).ToArray();
        var longName = new PublishedOfficialProfile { Id = "a", Name = new string('n', 257) };
        var tooManyTerms = new PublishedOfficialProfile
        {
            Id = "a",
            Name = "A",
            Terms = [.. Enumerable.Repeat(new PublishedOfficeTerm { Office = "rep", Party = "D", Start = new(2025, 1, 3), End = new(2027, 1, 3) },
                PublishedOfficials.MaximumTerms + 1)]
        };

        Assert.Throws<InvalidDataException>(new PublishedOfficials { Officials = many }.Validate);
        Assert.Throws<InvalidDataException>(new PublishedOfficials { Officials = [longName] }.Validate);
        Assert.Throws<InvalidDataException>(new PublishedOfficials { Officials = [tooManyTerms] }.Validate);
        Assert.Throws<InvalidDataException>(new PublishedCatalog
        {
            Issues = [.. Enumerable.Range(0, PublishedCatalog.MaximumEntries + 1).Select(index => new PublishedCatalogEntry { Id = $"i{index}", Name = "A" })],
            PolicyAreas = []
        }.Validate);
    }

    [Fact]
    public void ACatalogWithADuplicateIdOrABlankNameIsRefused()
    {
        var entry = new PublishedCatalogEntry { Id = "housing", Name = "Housing" };

        Assert.Throws<InvalidDataException>(new PublishedCatalog { Issues = [entry, entry], PolicyAreas = [] }.Validate);
        Assert.Throws<InvalidDataException>(new PublishedCatalog { Issues = [entry with { Name = " " }], PolicyAreas = [] }.Validate);
    }
}
