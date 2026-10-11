using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CivicLens.Application.Paging;
using CivicLens.Core.Review;

namespace CivicLens.Application.Review;

internal static class ReviewAuthorization
{
    public static void RequireReviewer(ReviewActor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (string.IsNullOrWhiteSpace(actor.Subject) || actor.Subject.Length > 256 || !actor.CanReview || !Enum.IsDefined(actor.Role) ||
            ReviewAuthor.IsAnalysis(actor.Subject))
            throw new UnauthorizedAccessException("The authenticated subject is not authorized for editorial review.");
    }

    public static void RequireOwner(ReviewActor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (string.IsNullOrWhiteSpace(actor.Subject) || actor.Subject.Length > 256 || actor.Role != ReviewRole.Owner ||
            ReviewAuthor.IsAnalysis(actor.Subject))
            throw new UnauthorizedAccessException("The authenticated subject is not authorized to publish.");
    }
}

internal static class ReviewValidation
{
    public static void ValidateIdempotencyKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 256)
            throw new ArgumentException("Idempotency key must contain 1 to 256 characters.", nameof(value));
    }

    public static string ValidateHash(string value, string parameterName)
    {
        if (value is not { Length: 64 } || value.Any(c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw new ArgumentException("Identifier must be a lowercase SHA-256 value.", parameterName);
        return value;
    }

    public static string ValidateDraftId(string value)
    {
        if (value is not { Length: 32 } || value.Any(c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw new ArgumentException("Draft ID is invalid.", nameof(value));
        return value;
    }

    public static ImmutableArray<string> ValidateSelections(ImmutableArray<string> values,
        ImmutableHashSet<string> allowed, string name)
    {
        if (values.IsDefault || values.Length > DocumentChangeDraftRevision.MaximumSelections)
            throw new ArgumentException($"Selection must contain at most {DocumentChangeDraftRevision.MaximumSelections} values.", name);
        var result = ImmutableArray.CreateBuilder<string>(values.Length);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 128 || !allowed.Contains(value) || !seen.Add(value))
                throw new ArgumentException("Selection contains an unknown, duplicate, or invalid value.", name);
            result.Add(value);
        }
        return result.ToImmutable();
    }

    public static ImmutableArray<DocumentChangeCitation> ValidateCitations(ImmutableArray<DocumentChangeCitation> citations)
    {
        if (citations.IsDefault || citations.Length > DocumentChangeDraftRevision.MaximumCitations)
            throw new ArgumentException($"At most {DocumentChangeDraftRevision.MaximumCitations} citations may be supplied.", nameof(citations));
        foreach (var citation in citations)
        {
            ArgumentNullException.ThrowIfNull(citation);
            ValidateHash(citation.ExtractionId, nameof(citations));
            if (citation.Start < 0 || citation.Length <= 0 || citation.Length > DocumentChangeDraftRevision.MaximumCitationLength)
                throw new ArgumentException("Citation range is invalid or exceeds the changed-text limit.", nameof(citations));
        }
        return citations;
    }

    public static DocumentChangeCitation? ValidateChangeDateEvidence(DateOnly? changeDate,
        DocumentChangeCitation? evidence, ImmutableArray<DocumentChangeCitation> citations)
    {
        if ((changeDate is null) != (evidence is null))
            throw new ArgumentException("An actual change date requires an exact evidence citation.");
        if (evidence is not null && !citations.Contains(evidence))
            throw new ArgumentException("Change-date evidence must also be included in the public citations.");
        return evidence;
    }

    public static ImmutableArray<string> ValidateDecisionIds(ImmutableArray<string> ids)
    {
        if (ids.IsDefault || ids.Length > 64) throw new ArgumentException("At most 64 decision IDs may be supplied.", nameof(ids));
        var result = ImmutableArray.CreateBuilder<string>(ids.Length);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            if (id is not { Length: 32 } || id.Any(c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')) || !seen.Add(id))
                throw new ArgumentException("Decision ID is invalid or duplicated.", nameof(ids));
            result.Add(id);
        }
        return result.ToImmutable();
    }

    public static void ValidateText(string? value, int maximumLength, string name, bool allowEmpty)
    {
        if (value is null)
        {
            if (allowEmpty) return;
            throw new ArgumentNullException(name);
        }
        if (value.Length > maximumLength || value.Contains('\0') || (!allowEmpty && string.IsNullOrWhiteSpace(value)))
            throw new ArgumentException($"Text must be {(allowEmpty ? "at most" : "1 to")} {maximumLength} characters and contain no NUL.", name);
    }

    public static string HashPayload<T>(T payload) => Convert.ToHexStringLower(
        SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload))));

    public static PageCursor? ValidatePage(string? cursor, int limit)
    {
        PageLimit.Validate(limit);
        return PageCursor.Parse(cursor);
    }
}
