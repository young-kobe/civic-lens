using CivicLens.Application.Collection;
using CivicLens.Application.People;
using CivicLens.Core.Review;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Application.People;

public sealed class ChangePersonSettingTests
{
    private static readonly ReviewActor Owner = new("owner-subject", ReviewRole.Owner);
    private readonly RecordingStore store = new();

    [Fact]
    public async Task TheOwnerWithholdsAndRestoresThePhotoAndEachChangeIsOneStoreWrite()
    {
        var handler = new ChangePersonSetting(store);

        await handler.WithholdPhotoAsync(Owner, "mayor", "Takedown.", default);
        await handler.RestorePhotoAsync(Owner, "mayor", "Back.", default);

        Assert.Equal([("mayor", new PersonSetting(true, null), "Takedown.", "owner-subject"),
            ("mayor", new PersonSetting(false, null), "Back.", "owner-subject")], store.Writes);
    }

    [Fact]
    public async Task AWebsiteSetThenClearedKeepsThePhotoChoice()
    {
        var handler = new ChangePersonSetting(store);
        store.Current = new PersonSetting(true, null);

        await handler.SetWebsiteAsync(Owner, "mayor", "https://mayor.example.gov/", "Site.", default);
        await handler.ClearWebsiteAsync(Owner, "mayor", "No site.", default);

        Assert.Equal([new PersonSetting(true, "https://mayor.example.gov/"), new PersonSetting(true, null)], store.Writes.Select(write => write.Setting));
    }

    [Theory]
    [MemberData(nameof(NonOwners))]
    public async Task ASubjectThatIsNotTheOwnerIsRefusedAndNothingIsWritten(ReviewActor actor)
    {
        var handler = new ChangePersonSetting(store);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => handler.WithholdPhotoAsync(actor, "mayor", "Takedown.", default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => handler.SetWebsiteAsync(actor, "mayor", "https://mayor.example.gov/", "Site.", default));

        Assert.Empty(store.Writes);
    }

    public static TheoryData<ReviewActor> NonOwners() => new()
    {
        new ReviewActor("reviewer-subject", ReviewRole.Reviewer),
        new ReviewActor("owner-subject", ReviewRole.Reviewer),
        new ReviewActor("", ReviewRole.Owner),
        new ReviewActor(ReviewAuthor.AnalysisSubject, ReviewRole.Owner)
    };

    [Theory]
    [InlineData("http://example.gov")]
    [InlineData("https://user@example.gov")]
    [InlineData("/relative")]
    [InlineData("")]
    [InlineData("https:example.gov")]
    [InlineData("https://example.gov ")]
    public async Task AWebsiteThatBreaksTheRuleOfThePublishedProfileIsRefusedAndNothingIsWritten(string website)
    {
        var handler = new ChangePersonSetting(store);

        await Assert.ThrowsAsync<ArgumentException>(() => handler.SetWebsiteAsync(Owner, "mayor", website, "Site.", default));

        Assert.Empty(store.Writes);
    }

    [Fact]
    public async Task AWebsiteOverTheLengthCapIsRefused()
    {
        var handler = new ChangePersonSetting(store);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.SetWebsiteAsync(Owner, "mayor", "https://example.gov/" + new string('a', PublishedWebsite.MaximumLength), "Site.", default));

        Assert.Empty(store.Writes);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task ABlankNoteOrPersonIdIsRefused(string text)
    {
        var handler = new ChangePersonSetting(store);

        await Assert.ThrowsAsync<ArgumentException>(() => handler.WithholdPhotoAsync(Owner, "mayor", text, default));
        await Assert.ThrowsAsync<ArgumentException>(() => handler.WithholdPhotoAsync(Owner, text, "Takedown.", default));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.WithholdPhotoAsync(Owner, "mayor", new string('n', PersonRegistryLimits.Note + 1), default));

        Assert.Empty(store.Writes);
    }

    private sealed class RecordingStore : IPersonRegistryStore
    {
        public PersonSetting Current { get; set; } = PersonSetting.None;
        public List<(string PersonId, PersonSetting Setting, string Note, string Actor)> Writes { get; } = [];

        public Task<PersonSettingRevision> AppendSettingAsync(string personId, Func<PersonSetting, PersonSetting> change, string note,
            string actorSubject, CancellationToken cancellationToken)
        {
            Current = change(Current);
            Writes.Add((personId, Current, note, actorSubject));
            return Task.FromResult(new PersonSettingRevision(personId, Writes.Count, Current.WithholdPhoto, Current.Website));
        }

        public Task<IReadOnlyDictionary<string, PersonSetting>> ReadNewestSettingsAsync(IReadOnlyCollection<string> personIds,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyDictionary<string, string>> ReadIdsByBioguideAsync(IReadOnlyCollection<string> bioguideIds,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
