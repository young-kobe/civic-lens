using System.Text.Json;
using CivicLens.Application.Votes;

namespace CivicLens.Host.Collection;

/// <summary>Reads a JSON array of roll call IDs such as house-119-1-100. It refuses an empty list, a repeated ID, and a bad ID.</summary>
internal static class VoteRollsFile
{
    public static List<VoteMeasureRoll> Read(string path)
    {
        var ids = JsonSerializer.Deserialize<string[]>(File.ReadAllText(path))
            ?? throw new ArgumentException("The votes file must hold a JSON array of roll call IDs such as house-119-1-100.");
        if (ids.Length == 0 || ids.Distinct(StringComparer.Ordinal).Count() != ids.Length)
            throw new ArgumentException("The votes file must list each roll call once, and it must list at least one roll call.");
        return [.. ids.Select(id => VoteMeasureRoll.TryParse(id) ?? throw new ArgumentException($"The roll call ID {id} must look like house-119-1-100."))];
    }
}
