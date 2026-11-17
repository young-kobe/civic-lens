using System.Text.Json;
using System.Text.Json.Nodes;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Publication.Contracts;

public sealed class PublishedOfficialsTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void AReleaseKeepsItsOfficialsAndTheirSenateClassThroughAJsonRoundTrip(int senateClass)
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
                    Terms = [new() { Office = "sen", Kind = PublishedOfficeKind.Senator, State = "WA", District = null, Party = "Democrat", Start = new(2025, 1, 3), End = new(2031, 1, 3), SenateClass = senateClass }]
                }
            ]
        };

        var officialsBack = JsonSerializer.Deserialize<PublishedOfficials>(
            JsonSerializer.Serialize(officials, PublicationProtocol.JsonOptions), PublicationProtocol.JsonOptions)!;
        officialsBack.Validate();

        Assert.Equal(new DateOnly(2025, 1, 3), officialsBack.Officials[0].Terms[0].Start);
        Assert.Equal(senateClass, officialsBack.Officials[0].Terms[0].SenateClass);
    }

    [Theory]
    [InlineData("http://example.gov")]
    [InlineData("https://user:pw@example.gov")]
    [InlineData("example.gov")]
    [InlineData("https:example.gov")]
    [InlineData(" https://example.gov")]
    [InlineData("https://example.gov/a b")]
    public void AProfileWebsiteThatBreaksTheRuleIsRefusedSoTheReaderNeverLinksAnUnsafeUrl(string website)
    {
        var profile = new PublishedOfficialProfile { Id = "mayor", Name = "Mayor Example", Website = website, Terms = [] };

        Assert.Throws<InvalidDataException>(profile.Validate);
    }

    [Fact]
    public void AProfileWebsiteOverTheLengthCapIsRefused()
    {
        var profile = new PublishedOfficialProfile { Id = "mayor", Name = "Mayor Example", Website = "https://example.gov/" + new string('a', 600), Terms = [] };

        Assert.Throws<InvalidDataException>(profile.Validate);
    }

    [Fact]
    public void AnHttpsProfileWebsiteKeepsItsValueThroughAJsonRoundTrip()
    {
        var profile = new PublishedOfficialProfile { Id = "mayor", Name = "Mayor Example", Website = "https://example.gov/", Terms = [] };

        var back = JsonSerializer.Deserialize<PublishedOfficialProfile>(JsonSerializer.Serialize(profile, PublicationProtocol.JsonOptions), PublicationProtocol.JsonOptions)!;
        back.Validate();

        Assert.Equal("https://example.gov/", back.Website);
    }

    [Fact]
    public void TheOfficialsJsonMatchesPinnedSchemaVersion()
    {
        var officials = new PublishedOfficials
        {
            Officials =
            [
                new()
                {
                    Id = "mayor",
                    Name = "Mayor Example",
                    Terms = [new() { Office = "sen", Kind = PublishedOfficeKind.Senator, State = "WA", Party = "Democrat", Start = new(2025, 1, 3), End = new(2031, 1, 3), SenateClass = 1 }]
                }
            ]
        };

        Assert.Equal(1, PublicationProtocol.SchemaVersion);
        Assert.Equal("""{"officials":[{"id":"mayor","name":"Mayor Example","wikidataId":null,"website":null,"photo":null,"terms":[{"office":"sen","kind":"senator","state":"WA","district":null,"party":"Democrat","caucus":null,"start":"2025-01-03","end":"2031-01-03","senateClass":1}]}]}""",
            JsonSerializer.Serialize(officials, PublicationProtocol.JsonOptions));
    }

    [Fact]
    public void AReleasedSenateTermWithoutClassKeepsItUnknown()
    {
        var term = JsonSerializer.Deserialize<PublishedOfficeTerm>(
            """{"office":"sen","kind":"senator","state":"WA","start":"2025-01-03","end":"2031-01-03"}""", PublicationProtocol.JsonOptions)!;

        term.Validate();

        Assert.Null(term.SenateClass);
    }

    [Theory]
    [InlineData(PublishedOfficeKind.Senator, 0)]
    [InlineData(PublishedOfficeKind.Senator, 4)]
    [InlineData(PublishedOfficeKind.Representative, 1)]
    [InlineData(PublishedOfficeKind.Executive, 1)]
    public void OnlySenateTermsMayCarryAClassFromOneToThree(PublishedOfficeKind kind, int senateClass)
    {
        var term = new PublishedOfficeTerm { Office = "Office", Kind = kind, Start = new(2025, 1, 3), SenateClass = senateClass };

        Assert.Throws<InvalidDataException>(term.Validate);
    }

    [Fact]
    public void AConfiguredOfficeHasNoPartyAndNoEndSoTheJsonShowsNullAndTheTermIsOngoing()
    {
        var officials = new PublishedOfficials
        {
            Officials = [new() { Id = "sec", Name = "Secretary Example", Terms = [new() { Office = "Secretary of State", Kind = PublishedOfficeKind.Executive, Start = new(2025, 1, 21) }] }]
        };

        officials.Validate();

        Assert.Equal("""{"officials":[{"id":"sec","name":"Secretary Example","wikidataId":null,"website":null,"photo":null,"terms":[{"office":"Secretary of State","kind":"executive","state":null,"district":null,"party":null,"caucus":null,"start":"2025-01-21","end":null}]}]}""",
            JsonSerializer.Serialize(officials, PublicationProtocol.JsonOptions));
    }

    [Fact]
    public void TwoOfficialsWithOneIdAreRefusedBecauseAProfilePageNeedsOnePerson()
    {
        var official = new PublishedOfficialProfile { Id = "mayor", Name = "Mayor Example", Terms = [] };

        Assert.Throws<InvalidDataException>(new PublishedOfficials { Officials = [official, official] }.Validate);
    }

    [Fact]
    public void AProfileWithoutTermsIsRefusedSoAPreviousReleaseIsNeverReadAsHavingNoTerms()
    {
        var json = JsonNode.Parse(JsonSerializer.Serialize(
            new PublishedOfficialProfile { Id = "mayor", Name = "Mayor Example", Terms = [] }, PublicationProtocol.JsonOptions))!.AsObject();
        json.Remove("terms");

        Assert.ThrowsAny<JsonException>(() => JsonSerializer.Deserialize<PublishedOfficialProfile>(json.ToJsonString(), PublicationProtocol.JsonOptions));
    }

    [Fact]
    public void ATermThatEndsBeforeItStartsIsRefused()
    {
        var term = new PublishedOfficeTerm { Office = "prez", Kind = PublishedOfficeKind.Executive, Party = "Democrat", Start = new(2025, 1, 20), End = new(2021, 1, 20) };

        Assert.Throws<InvalidDataException>(new PublishedOfficialProfile { Id = "a", Name = "A", Terms = [term] }.Validate);
    }

    [Fact]
    public void ATermWithAnUndefinedKindIsRefusedBecauseTheListCouldNotPlaceTheOfficial()
    {
        var term = new PublishedOfficeTerm { Office = "prez", Kind = (PublishedOfficeKind)99, Start = new(2025, 1, 20) };

        Assert.Throws<InvalidDataException>(new PublishedOfficialProfile { Id = "a", Name = "A", Terms = [term] }.Validate);
    }

    [Fact]
    public void TheFilesHaveSizeCapsSoTheirSizeStaysBounded()
    {
        var many = Enumerable.Range(0, PublishedOfficials.MaximumOfficials + 1)
            .Select(index => new PublishedOfficialProfile { Id = $"p{index}", Name = "A", Terms = [] }).ToArray();
        var longName = new PublishedOfficialProfile { Id = "a", Name = new string('n', 257), Terms = [] };
        var tooManyTerms = new PublishedOfficialProfile
        {
            Id = "a",
            Name = "A",
            Terms = [.. Enumerable.Repeat(new PublishedOfficeTerm { Office = "rep", Kind = PublishedOfficeKind.Representative, Party = "D", Start = new(2025, 1, 3), End = new(2027, 1, 3) },
                PublishedOfficials.MaximumTerms + 1)]
        };

        Assert.Throws<InvalidDataException>(new PublishedOfficials { Officials = many }.Validate);
        Assert.Throws<InvalidDataException>(new PublishedOfficials { Officials = [longName] }.Validate);
        Assert.Throws<InvalidDataException>(new PublishedOfficials { Officials = [tooManyTerms] }.Validate);
    }

    private static readonly string PhotoHash = new('a', 64);

    private static PublishedPhoto Photo(Func<PublishedPhoto, PublishedPhoto>? change = null)
    {
        var photo = new PublishedPhoto
        {
            Path = PublicationProtocol.PhotoPath(PhotoHash),
            Credit = "Official portrait. Public domain.",
            SourceUrl = "https://example.gov/images/mayor.jpg",
            Width = 225,
            Height = 275
        };
        return change?.Invoke(photo) ?? photo;
    }

    [Fact]
    public void AProfilePhotoKeepsItsFieldsThroughAJsonRoundTrip()
    {
        var profile = new PublishedOfficialProfile { Id = "mayor", Name = "Mayor Example", Photo = Photo(), Terms = [] };

        var back = JsonSerializer.Deserialize<PublishedOfficialProfile>(JsonSerializer.Serialize(profile, PublicationProtocol.JsonOptions), PublicationProtocol.JsonOptions)!;
        back.Validate();

        Assert.Equal(Photo(), back.Photo);
    }

    [Fact]
    public void AProfileWithNoPhotoIsValidSoAReleaseBeforePhotosStillReads()
    {
        var back = JsonSerializer.Deserialize<PublishedOfficialProfile>(
            """{"id":"mayor","name":"Mayor Example","terms":[]}""", PublicationProtocol.JsonOptions)!;

        back.Validate();

        Assert.Null(back.Photo);
    }

    [Theory]
    [InlineData("photos/Aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.jpg")]
    [InlineData("photos/aaaa.jpg")]
    [InlineData("../photos/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.jpg")]
    [InlineData("photos/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.png")]
    [InlineData("records/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.jpg")]
    public void APhotoPathThatIsNotAHashedFileInThePhotosFolderIsRefusedSoAProfileCannotNameAnotherFile(string path)
    {
        Assert.Throws<InvalidDataException>(Photo(photo => photo with { Path = path }).Validate);
    }

    [Theory]
    [InlineData("http://example.gov/a.jpg")]
    [InlineData("https://user:pw@example.gov/a.jpg")]
    public void APhotoSourceUrlThatIsNotAPlainHttpsUrlIsRefused(string url)
    {
        Assert.Throws<InvalidDataException>(Photo(photo => photo with { SourceUrl = url }).Validate);
    }

    [Theory]
    [InlineData(0, 275)]
    [InlineData(225, 0)]
    [InlineData(PublishedPhoto.MaximumDimension + 1, 275)]
    [InlineData(225, PublishedPhoto.MaximumDimension + 1)]
    public void APhotoSizeThatIsNotPositiveAndBoundedIsRefused(int width, int height)
    {
        Assert.Throws<InvalidDataException>(Photo(photo => photo with { Width = width, Height = height }).Validate);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankPhotoCreditIsRefusedSoEveryPhotoNamesItsSource(string credit)
    {
        Assert.Throws<InvalidDataException>(Photo(photo => photo with { Credit = credit }).Validate);
    }

    [Fact]
    public void AnInvalidPhotoMakesTheProfileInvalid()
    {
        var profile = new PublishedOfficialProfile { Id = "mayor", Name = "Mayor Example", Photo = Photo(photo => photo with { Width = 0 }), Terms = [] };

        Assert.Throws<InvalidDataException>(profile.Validate);
    }
}
