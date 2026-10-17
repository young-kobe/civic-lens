namespace CivicLens.Core.Statements;

public sealed record BillNumberMatch(string Type, int Number, int Start, int Length)
{
    public string Citation => $"{Type} {Number}";
}
