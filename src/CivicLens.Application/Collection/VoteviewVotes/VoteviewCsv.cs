using System.Text;

namespace CivicLens.Application.Collection.VoteviewVotes;

internal sealed class VoteviewCsv
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private readonly Dictionary<string, int> columns;
    private readonly string name;

    private VoteviewCsv(string name, Dictionary<string, int> columns, List<string[]> rows)
    {
        this.name = name;
        this.columns = columns;
        Rows = rows;
    }

    public List<string[]> Rows { get; }

    public static VoteviewCsv Read(ReadOnlySpan<byte> body, string name, params string[] requiredColumns)
    {
        string text;
        try { text = StrictUtf8.GetString(body); }
        catch (DecoderFallbackException exception) { throw new InvalidDataException($"{name} is not valid UTF-8.", exception); }
        var records = Split(text, name);
        if (records.Count == 0) throw new InvalidDataException($"{name} has no header row.");
        var columns = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < records[0].Length; index++)
            if (!columns.TryAdd(records[0][index], index)) throw new InvalidDataException($"{name} repeats the column {records[0][index]}.");
        foreach (var column in requiredColumns)
            if (!columns.ContainsKey(column)) throw new InvalidDataException($"{name} has no {column} column.");
        var rows = records.Skip(1).ToList();
        if (rows.Any(row => row.Length != columns.Count)) throw new InvalidDataException($"{name} has a row with the wrong number of fields.");
        return new VoteviewCsv(name, columns, rows);
    }

    public string Required(string[] row, string column) =>
        row[columns[column]] is { Length: > 0 } value ? value : throw new InvalidDataException($"{name} has an empty {column} value.");

    public string? Optional(string[] row, string column) => row[columns[column]] is { Length: > 0 } value ? value : null;

    public int Number(string[] row, string column) =>
        ApiPageQuery.TryReadNumber(Required(row, column)) ?? throw new InvalidDataException($"{name} has an invalid {column} number.");

    private static List<string[]> Split(string text, string name)
    {
        var records = new List<string[]>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (quoted)
            {
                if (character != '"') field.Append(character);
                else if (index + 1 < text.Length && text[index + 1] == '"') { field.Append('"'); index++; }
                else quoted = false;
            }
            else if (character == '"' && field.Length == 0) quoted = true;
            else if (character == ',') EndField(fields, field);
            else if (character == '\n') EndRecord(records, fields, field);
            else if (character != '\r') field.Append(character);
        }
        if (quoted) throw new InvalidDataException($"{name} ends inside a quoted field.");
        if (field.Length > 0 || fields.Count > 0) EndRecord(records, fields, field);
        return records;
    }

    private static void EndField(List<string> fields, StringBuilder field)
    {
        fields.Add(field.ToString());
        field.Clear();
    }

    private static void EndRecord(List<string[]> records, List<string> fields, StringBuilder field)
    {
        EndField(fields, field);
        records.Add([.. fields]);
        fields.Clear();
    }
}
