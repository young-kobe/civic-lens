using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using CivicLens.Core.Collection;

namespace CivicLens.Core.Documents;

public sealed class DocumentExtraction
{
    public const int MaximumTextLength = 2_000_000;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public DocumentExtraction(CapturedAttemptResult sourceAttempt, string parserVersion,
        string normalizationVersion, string text)
    {
        ArgumentNullException.ThrowIfNull(sourceAttempt);
        ValidateVersion(parserVersion, nameof(parserVersion));
        ValidateVersion(normalizationVersion, nameof(normalizationVersion));
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > MaximumTextLength)
            throw new ArgumentException("Extracted text exceeds the maximum length.", nameof(text));
        if (text.Contains('\0')) throw new ArgumentException("Extracted text cannot contain NUL.", nameof(text));

        byte[] textBytes;
        try { textBytes = StrictUtf8.GetBytes(text); }
        catch (EncoderFallbackException exception)
        {
            throw new ArgumentException("Extracted text contains an invalid surrogate sequence.", nameof(text), exception);
        }

        SourceAttempt = sourceAttempt;
        ParserVersion = parserVersion;
        NormalizationVersion = normalizationVersion;
        Text = text;
        TextSha256 = Convert.ToHexStringLower(SHA256.HashData(textBytes));
        ExtractionId = ComputeExtractionId(sourceAttempt.AttemptId, parserVersion, normalizationVersion);
    }

    public CapturedAttemptResult SourceAttempt { get; }
    public string ParserVersion { get; }
    public string NormalizationVersion { get; }
    public string Text { get; }
    public string TextSha256 { get; }
    public string ExtractionId { get; }

    private static void ValidateVersion(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128 || value.Contains('\0'))
            throw new ArgumentException("Version must contain 1 to 128 characters.", parameterName);
    }

    private static string ComputeExtractionId(string attemptId, string parserVersion, string normalizationVersion)
    {
        using var buffer = new MemoryStream();
        WriteFramed(buffer, attemptId);
        WriteFramed(buffer, parserVersion);
        WriteFramed(buffer, normalizationVersion);
        return Convert.ToHexStringLower(SHA256.HashData(buffer.ToArray()));
    }

    private static void WriteFramed(Stream stream, string value)
    {
        var bytes = StrictUtf8.GetBytes(value);
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
        stream.Write(length);
        stream.Write(bytes);
    }
}
