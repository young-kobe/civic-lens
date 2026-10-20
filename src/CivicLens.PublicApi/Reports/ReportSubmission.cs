using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CivicLens.Publication.Contracts;
using Microsoft.AspNetCore.Http;

namespace CivicLens.PublicApi.Reports;

/// <summary>One report as a reader sent it, checked for shape. The record is not looked up yet.</summary>
public sealed record ReportSubmission(string RecordId, int Revision, string Category, string Text, string TurnstileToken)
{
    private const int MaximumRecordIdLength = 64;
    private const int MaximumTokenLength = 2048;

    /// <summary>Two reports with the same record, revision, category, and text have the same hash.</summary>
    public byte[] PayloadHash => SHA256.HashData(
        Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture, $"{RecordId}\0{Revision}\0{Category}\0{Text}")));

    /// <summary>Reads the form. An invalid value throws <see cref="ArgumentException"/> that names the field.</summary>
    public static ReportSubmission Parse(IFormCollection form)
    {
        var recordId = Required(form, ReaderReportProtocol.RecordIdField);
        if (recordId.Length > MaximumRecordIdLength || !recordId.All(char.IsAsciiHexDigitLower))
            throw new ArgumentException("The record ID is not valid.", ReaderReportProtocol.RecordIdField);
        if (!int.TryParse(Required(form, ReaderReportProtocol.RevisionField), NumberStyles.None, CultureInfo.InvariantCulture, out var revision) || revision < 1)
            throw new ArgumentException("The revision must be a whole number of 1 or more.", ReaderReportProtocol.RevisionField);
        var category = Required(form, ReaderReportProtocol.CategoryField);
        if (!ReaderReportProtocol.IsCategory(category))
            throw new ArgumentException("The category is not in the list.", ReaderReportProtocol.CategoryField);
        var text = Value(form, ReaderReportProtocol.TextField) ?? "";
        if (!ReaderReportProtocol.IsText(category, text))
            throw new ArgumentException(
                $"The text holds at most {ReaderReportProtocol.MaximumTextCharacters} characters, no NUL character, and is required for the category \"{ReaderReportProtocol.OtherCategory}\".",
                ReaderReportProtocol.TextField);
        var token = Required(form, ReaderReportProtocol.TurnstileField);
        if (token.Length > MaximumTokenLength)
            throw new ArgumentException("The check token is too long.", ReaderReportProtocol.TurnstileField);
        return new(recordId, revision, category, text, token);
    }

    private static string Required(IFormCollection form, string name) =>
        Value(form, name) is { Length: > 0 } value ? value : throw new ArgumentException($"{name} is required.", name);

    private static string? Value(IFormCollection form, string name)
    {
        if (!form.TryGetValue(name, out var values)) return null;
        return values.Count == 1 ? values[0] : throw new ArgumentException($"{name} must appear once.", name);
    }
}
