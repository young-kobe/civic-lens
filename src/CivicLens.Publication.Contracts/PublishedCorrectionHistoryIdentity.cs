using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace CivicLens.Publication.Contracts;

/// <summary>Creates a stable identity for the ordered content of a published correction history.</summary>
public static class PublishedCorrectionHistoryIdentity
{
    public const int CurrentVersion = 1;

    private static readonly byte[] Magic = Encoding.UTF8.GetBytes("civic-lens-correction-history");
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static string Of(IReadOnlyList<PublishedCorrection> corrections, int version = CurrentVersion)
    {
        if (corrections is null) throw new InvalidDataException("A correction history cannot be null.");
        if (version != CurrentVersion) throw new InvalidDataException($"Correction history identity version {version} is not supported.");

        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, StrictUtf8, leaveOpen: true))
        {
            WriteInt32(writer, Magic.Length);
            writer.Write(Magic);
            WriteInt32(writer, version);
            WriteInt32(writer, corrections.Count);

            foreach (var correction in corrections)
            {
                if (correction is null) throw new InvalidDataException("A correction history cannot contain a null entry.");

                WriteInt32(writer, correction.ReplacedRevisionNumber);
                WriteInt64(writer, correction.PublishedAtUtc.UtcTicks);
                WriteInt16(writer, checked((short)correction.PublishedAtUtc.Offset.TotalMinutes));
                writer.Write(correction.Note is not null);
                if (correction.Note is not null)
                {
                    try
                    {
                        var noteBytes = StrictUtf8.GetBytes(correction.Note);
                        WriteInt32(writer, noteBytes.Length);
                        writer.Write(noteBytes);
                    }
                    catch (EncoderFallbackException exception)
                    {
                        throw new InvalidDataException("A correction note is not valid UTF-16.", exception);
                    }
                }
                writer.Write(correction.FactsUnchanged);
            }
        }

        return Convert.ToHexStringLower(SHA256.HashData(stream.ToArray()));
    }

    private static void WriteInt16(BinaryWriter writer, short value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(short)];
        BinaryPrimitives.WriteInt16LittleEndian(bytes, value);
        writer.Write(bytes);
    }

    private static void WriteInt32(BinaryWriter writer, int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        writer.Write(bytes);
    }

    private static void WriteInt64(BinaryWriter writer, long value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, value);
        writer.Write(bytes);
    }
}
