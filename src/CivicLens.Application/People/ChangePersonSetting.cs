using CivicLens.Application.Review;
using CivicLens.Core.Review;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.People;

/// <summary>Owner changes of the photo and the website of a person. Each change adds one revision and never edits an older one.</summary>
public sealed class ChangePersonSetting(IPersonRegistryStore store)
{
    public Task<PersonSettingRevision> WithholdPhotoAsync(ReviewActor actor, string personId, string note, CancellationToken cancellationToken) =>
        ChangeAsync(actor, personId, note, setting => setting with { WithholdPhoto = true }, cancellationToken);

    public Task<PersonSettingRevision> RestorePhotoAsync(ReviewActor actor, string personId, string note, CancellationToken cancellationToken) =>
        ChangeAsync(actor, personId, note, setting => setting with { WithholdPhoto = false }, cancellationToken);

    public Task<PersonSettingRevision> SetWebsiteAsync(ReviewActor actor, string personId, string url, string note, CancellationToken cancellationToken)
    {
        if (!PublishedWebsite.IsValid(url))
            throw new ArgumentException($"The website must be an absolute https URL of at most {PublishedWebsite.MaximumLength} characters with no user info.", nameof(url));
        return ChangeAsync(actor, personId, note, setting => setting with { Website = url }, cancellationToken);
    }

    public Task<PersonSettingRevision> ClearWebsiteAsync(ReviewActor actor, string personId, string note, CancellationToken cancellationToken) =>
        ChangeAsync(actor, personId, note, setting => setting with { Website = null }, cancellationToken);

    private Task<PersonSettingRevision> ChangeAsync(ReviewActor actor, string personId, string note, Func<PersonSetting, PersonSetting> change,
        CancellationToken cancellationToken)
    {
        ReviewAuthorization.RequireOwner(actor);
        if (string.IsNullOrWhiteSpace(personId))
            throw new ArgumentException("The person ID is required.", nameof(personId));
        if (string.IsNullOrWhiteSpace(note) || note.Length > PersonRegistryLimits.Note)
            throw new ArgumentException($"The note must have 1 to {PersonRegistryLimits.Note} characters.", nameof(note));
        return store.AppendSettingAsync(personId, change, note, actor.Subject, cancellationToken);
    }
}
