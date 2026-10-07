using System.Globalization;
using System.Text.Json;
using CivicLens.Application.Collection;
using CivicLens.Application.Documents;
using CivicLens.Collection.Contracts;

namespace CivicLens.Host.Documents;

internal static class DocumentCommand
{
    public static bool Matches(string[] args) => args is ["documents", "extract", _, _] or ["documents", "extract", _, _, _] or
        ["documents", "get", _] or ["documents", "cite", _, _, _];

    public static async Task<int> ExecuteAsync(string[] args, ICollectionAttemptStore attempts,
        IDocumentTextExtractor extractor, IDocumentExtractionStore extractions, CancellationToken cancellationToken,
        CollectionConfiguration? configuration = null)
    {
        try
        {
            if (args is ["documents", "extract", _, _] or ["documents", "extract", _, _, _])
            {
                var handler = new ExtractDocument(attempts, extractor, extractions);
                var attemptId = args[^2];
                var root = Path.GetFullPath(args[^1]);
                var extraction = args.Length == 5
                    ? await handler.ExecuteConfiguredAsync(configuration ?? throw new ArgumentException("Configuration is required."),
                        attemptId, root, cancellationToken)
                    : await handler.ExecuteAsync(attemptId, root, cancellationToken);
                Write(new
                {
                    extraction.ExtractionId,
                    AttemptId = extraction.SourceAttempt.AttemptId,
                    extraction.ParserVersion,
                    extraction.NormalizationVersion,
                    extraction.TextSha256,
                    TextLength = extraction.Text.Length,
                    ProfileId = extraction.Profile?.Id,
                    ProfileRevisionId = extraction.Profile?.RevisionId
                });
                return 0;
            }

            if (args is ["documents", "get", var extractionId])
            {
                var extraction = await extractions.GetAsync(extractionId, cancellationToken);
                if (extraction is null) { Console.Error.WriteLine("Extraction was not found."); return 2; }
                Write(extraction);
                return 0;
            }

            if (args is ["documents", "cite", var id, var startText, var lengthText] &&
                int.TryParse(startText, NumberStyles.None, CultureInfo.InvariantCulture, out var start) &&
                int.TryParse(lengthText, NumberStyles.None, CultureInfo.InvariantCulture, out var length))
            {
                Write(await new GetDocumentCitation(extractions).ExecuteAsync(id, start, length, cancellationToken));
                return 0;
            }
            Console.Error.WriteLine("Citation offsets must be nonnegative UTF-16 integers with positive length.");
            return 2;
        }
        catch (ArgumentException)
        {
            Console.Error.WriteLine("Invalid document input. Check the captured attempt, extraction ID, artifact path, or citation span.");
            return 2;
        }
        catch (Exception exception) when (exception is InvalidDataException or NotSupportedException)
        {
            Console.Error.WriteLine("Capture text could not be extracted. Check byte integrity, supported content, encoding, resource limits, and whether the profile selects one content region without excluding it.");
            return 1;
        }
    }

    private static void Write<T>(T value) => Console.WriteLine(JsonSerializer.Serialize(value, CollectionProtocol.JsonOptions));
}
