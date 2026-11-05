using System.Text.Json;
using CivicLens.Application.Actions;
using CivicLens.Application.Collection.Bills;

namespace CivicLens.Host.Collection;

/// <summary>Reads a JSON array of bill IDs such as 119-hr-1943. It refuses an empty list, a repeated ID, and a bad ID.</summary>
internal static class BillIdsFile
{
    public static List<CongressBillListItem> Read(string path)
    {
        var ids = JsonSerializer.Deserialize<string[]>(File.ReadAllText(path))
            ?? throw new ArgumentException("The bills file must hold a JSON array of bill IDs such as 119-hr-1943.");
        if (ids.Length == 0 || ids.Distinct(StringComparer.Ordinal).Count() != ids.Length)
            throw new ArgumentException("The bills file must list each bill once, and it must list at least one bill.");
        return [.. ids.Select(ReadBill)];
    }

    private static CongressBillListItem ReadBill(string id) =>
        ActionTextMeasure.TryParseId(id) ?? throw new ArgumentException($"The bill ID {id} must look like 119-hr-1943.");
}
