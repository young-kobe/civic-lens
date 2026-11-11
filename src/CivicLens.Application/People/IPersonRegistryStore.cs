namespace CivicLens.Application.People;

public interface IPersonRegistryStore
{
    /// <summary>
    /// Adds one setting revision of a known person. The change maps the newest setting to the new one, in the same transaction.
    /// Throws <see cref="ArgumentException"/> for an unknown person.
    /// </summary>
    Task<PersonSettingRevision> AppendSettingAsync(string personId, Func<PersonSetting, PersonSetting> change, string note,
        string actorSubject, CancellationToken cancellationToken);

    /// <summary>Reads the newest setting of each named person in one query. A person with no revision is absent.</summary>
    Task<IReadOnlyDictionary<string, PersonSetting>> ReadNewestSettingsAsync(IReadOnlyCollection<string> personIds,
        CancellationToken cancellationToken);
}
