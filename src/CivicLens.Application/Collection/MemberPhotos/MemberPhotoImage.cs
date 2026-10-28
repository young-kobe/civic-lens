namespace CivicLens.Application.Collection.MemberPhotos;

/// <summary>Checks that a captured body is a JPEG portrait of the one size the source publishes.</summary>
public static class MemberPhotoImage
{
    private const int Width = 225;
    private const int Height = 275;
    private const string MediaType = "image/jpeg";
    private const byte Marker = 0xFF;
    private const byte StartOfImage = 0xD8;
    private const byte StartOfScan = 0xDA;
    private const byte EndOfImage = 0xD9;

    public static bool IsValid(string? contentType, byte[] body) =>
        IsJpegMediaType(contentType) && HasJpegSignature(body) && ReadSize(body) == (Width, Height);

    private static bool IsJpegMediaType(string? contentType) =>
        contentType?.Split(';')[0].Trim().Equals(MediaType, StringComparison.OrdinalIgnoreCase) == true;

    private static bool HasJpegSignature(byte[] body) =>
        body.Length >= 3 && body[0] == Marker && body[1] == StartOfImage && body[2] == Marker;

    private static (int Width, int Height)? ReadSize(byte[] body)
    {
        var position = 2;
        while (position + 4 <= body.Length)
        {
            if (body[position] != Marker) return null;
            var marker = body[position + 1];
            if (marker == Marker) { position++; continue; }
            if (marker is StartOfScan or EndOfImage) return null;
            var length = body[position + 2] << 8 | body[position + 3];
            if (IsStartOfFrame(marker))
                return position + 9 <= body.Length
                    ? (body[position + 7] << 8 | body[position + 8], body[position + 5] << 8 | body[position + 6])
                    : null;
            position += 2 + length;
        }
        return null;
    }

    private static bool IsStartOfFrame(byte marker) => marker is >= 0xC0 and <= 0xCF and not (0xC4 or 0xC8 or 0xCC);
}
