using CivicLens.Application.Collection;
using CivicLens.Application.People;

namespace CivicLens.Tests.Fixtures;

internal sealed class FixedPersonRegistry(IReadOnlyDictionary<string, PersonSetting>? settings = null, Action? onSettingsRead = null) : IPersonRegistryStore
{
    public Task<PeopleImport> ImportAsync(IReadOnlyList<PersonConfiguration> people, string actorSubject, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<PersonSettingRevision> AppendSettingAsync(string personId, Func<PersonSetting, PersonSetting> change, string note,
        string actorSubject, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyDictionary<string, PersonSetting>> ReadNewestSettingsAsync(IReadOnlyCollection<string> personIds,
        CancellationToken cancellationToken)
    {
        onSettingsRead?.Invoke();
        return Task.FromResult<IReadOnlyDictionary<string, PersonSetting>>(
            (settings ?? new Dictionary<string, PersonSetting>()).Where(pair => personIds.Contains(pair.Key)).ToDictionary());
    }
}
