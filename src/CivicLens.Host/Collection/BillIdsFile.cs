using System.Text.Json;
using System.Text.RegularExpressions;
using CivicLens.Application.Actions;
using CivicLens.Application.Collection.Bills;

namespace CivicLens.Host.Collection;

/// <summary>Reads a JSON array of bill IDs such as 119-hr-1943, or of Federal Register document numbers such as 2025-01953.
/// It refuses an empty list, a repeated ID, and a bad ID.</summary>
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

    public static List<string> ReadDocumentNumbers(string path)
    {
        var numbers = JsonSerializer.Deserialize<string[]>(File.ReadAllText(path))
            ?? throw new ArgumentException("The documents file must hold a JSON array of document numbers such as 2025-01953.");
        if (numbers.Length == 0 || numbers.Distinct(StringComparer.Ordinal).Count() != numbers.Length)
            throw new ArgumentException("The documents file must list each document once, and it must list at least one document.");
        if (numbers.Any(number => !DocumentNumber.IsMatch(number)))
            throw new ArgumentException("A document number must look like 2025-01953.");
        return [.. numbers];
    }

    private static readonly Regex DocumentNumber = new(@"^[A-Z0-9]{1,4}-[0-9]{3,6}$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    private static CongressBillListItem ReadBill(string id) =>
        ActionTextMeasure.TryParseId(id) ?? throw new ArgumentException($"The bill ID {id} must look like 119-hr-1943.");
}
