using CivicLens.Application.People;

namespace CivicLens.Tests.Fixtures;

internal sealed class FixedPersonRegistry(IReadOnlyDictionary<string, PersonSetting>? settings = null, Action? onSettingsRead = null,
    IReadOnlyDictionary<string, string>? idsByBioguide = null) : IPersonRegistryStore
{
    public Task<PersonSettingRevision> AppendSettingAsync(string personId, Func<PersonSetting, PersonSetting> change, string note,
        string actorSubject, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyDictionary<string, PersonSetting>> ReadNewestSettingsAsync(IReadOnlyCollection<string> personIds,
        CancellationToken cancellationToken)
    {
        onSettingsRead?.Invoke();
        return Task.FromResult<IReadOnlyDictionary<string, PersonSetting>>(
            (settings ?? new Dictionary<string, PersonSetting>()).Where(pair => personIds.Contains(pair.Key)).ToDictionary());
    }

    public Task<IReadOnlyDictionary<string, string>> ReadIdsByBioguideAsync(IReadOnlyCollection<string> bioguideIds,
        CancellationToken cancellationToken) => Task.FromResult<IReadOnlyDictionary<string, string>>(
            (idsByBioguide ?? new Dictionary<string, string>()).Where(pair => bioguideIds.Contains(pair.Key)).ToDictionary());
}
