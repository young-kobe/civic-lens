using System.Text.RegularExpressions;

namespace CivicLens.Application.Collection.FederalRegister;

public static partial class FederalRegisterDocumentNumber
{
    public static bool IsValid(string? value) => value is not null && value.Length <= 32 && Pattern().IsMatch(value);

    public static string Require(string? value) =>
        IsValid(value) ? value! : throw new InvalidDataException("A Federal Register document number is invalid.");

    [GeneratedRegex(@"^[A-Z0-9]{1,4}(-[A-Z0-9]{1,8}){1,2}$")]
    private static partial Regex Pattern();
}
