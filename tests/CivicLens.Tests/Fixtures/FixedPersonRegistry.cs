using CivicLens.Application.People;

namespace CivicLens.Tests.Fixtures;

internal sealed class FixedPersonRegistry(IReadOnlyDictionary<string, PersonSetting>? settings = null, Action? onSettingsRead = null,
    IReadOnlyList<PersonKey>? keys = null) : IPersonRegistryStore
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

    public Task<IReadOnlyList<PersonKey>> ReadKeysAsync(CancellationToken cancellationToken) =>
        Task.FromResult(keys ?? []);
}
