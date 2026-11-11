using System.Globalization;

namespace CivicLens.Application.Publication;

public sealed record ReleaseBillCountVerificationCursor(string SelectionFingerprint, int ReleaseNumber)
{
    private const int FingerprintLength = 64;

    public string Encode() => $"{SelectionFingerprint}:{ReleaseNumber.ToString(CultureInfo.InvariantCulture)}";

    public static ReleaseBillCountVerificationCursor? Parse(string? value)
    {
        if (value is null) return null;
        var parts = value.Split(':');
        if (parts.Length != 2 || parts[0].Length != FingerprintLength || parts[0].Any(character => !char.IsAsciiHexDigitLower(character)) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var release) || release < 1)
            throw Invalid();
        var cursor = new ReleaseBillCountVerificationCursor(parts[0], release);
        return cursor.Encode() == value ? cursor : throw Invalid();
    }

    private static ArgumentException Invalid() => new("Bill count verification cursor is not valid.", "cursor");
}
