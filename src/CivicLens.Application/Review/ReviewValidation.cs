using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CivicLens.Application.Paging;
using CivicLens.Application.Publication;
using CivicLens.Core.Documents;
using CivicLens.Core.Review;
using CivicLens.Core;

namespace CivicLens.Application.Review;

internal static class ReviewAuthorization
{
    public static void RequireReviewer(ReviewActor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (!ReviewAuthor.IsValidSubject(actor.Subject) || !actor.CanReview || !Enum.IsDefined(actor.Role) ||
            ReviewAuthor.IsReserved(actor.Subject))
            throw new UnauthorizedAccessException("The authenticated subject is not authorized for editorial review.");
    }

    public static void RequireOwner(ReviewActor actor, bool allowCode = false)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var reserved = ReviewAuthor.IsAnalysis(actor.Subject) || (ReviewAuthor.IsCode(actor.Subject) && !allowCode);
        if (!ReviewAuthor.IsValidSubject(actor.Subject) || actor.Role != ReviewRole.Owner || reserved)
            throw new UnauthorizedAccessException("The authenticated subject is not the owner.");
    }
}

internal static class ReviewValidation
{
    private const int MaximumIdempotencyKeyLength = 256;
    private const int MaximumFilterIdLength = 128;

    public static void ValidateIdempotencyKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaximumIdempotencyKeyLength)
            throw new ArgumentException($"Idempotency key must contain 1 to {MaximumIdempotencyKeyLength} characters.", nameof(value));
    }

    public static string ValidateHash(string value, string parameterName)
    {
        if (!LowercaseHex.Is(value, 64))
            throw new ArgumentException("Identifier must be a lowercase SHA-256 value.", parameterName);
        return value;
    }

    public static T RequireSuggestion<T>(T? suggestion) where T : class =>
        suggestion ?? throw new ArgumentException("This revision has no suggestion.");

    public static string ValidateCandidateId(string value) => ValidateHash(value, nameof(value));

    public static string ValidateDraftId(string value) => DocumentChangeDraftRevision.ValidateDraftId(value);

    public static string ValidateRunId(string value)
    {
        if (!LowercaseHex.Is(value, 32))
            throw new ArgumentException("Run ID is invalid.", nameof(value));
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

    public static ImmutableArray<SourceSpan> ValidateCitations(ImmutableArray<SourceSpan> citations)
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

    public static SourceSpan? ValidateChangeDateEvidence(DateOnly? changeDate,
        SourceSpan? evidence, ImmutableArray<SourceSpan> citations)
    {
        if ((changeDate is null) != (evidence is null))
            throw new ArgumentException("An actual change date requires an exact evidence citation.");
        if (evidence is not null && !citations.Contains(evidence))
            throw new ArgumentException("Change-date evidence must also be included in the public citations.");
        return evidence;
    }

    public static ReviewDecision CreateDecision(ReviewActor actor, string recordId, int expectedRevisionNumber,
        int expectedReviewStateVersion, ReviewDecisionKind kind, string? note, ImmutableArray<string> resolvedDecisionIds,
        string? correctionNote = null)
    {
        if (expectedRevisionNumber < 1) throw new ArgumentOutOfRangeException(nameof(expectedRevisionNumber));
        if (expectedReviewStateVersion < 0) throw new ArgumentOutOfRangeException(nameof(expectedReviewStateVersion));
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        var resolutions = ValidateDecisionIds(resolvedDecisionIds);
        ValidateText(note, 4_000, nameof(note), allowEmpty: true);
        if (correctionNote is not null && kind != ReviewDecisionKind.Approve)
            throw new ArgumentException("Only an approval takes a correction note.", nameof(correctionNote));
        return new ReviewDecision(Guid.NewGuid().ToString("N"), recordId, expectedRevisionNumber,
            expectedReviewStateVersion + 1, kind, actor.Subject, note, resolutions, DateTimeOffset.UtcNow,
            correctionNote is null ? null : CorrectionNoteText.Validate(correctionNote, nameof(correctionNote)));
    }

    public static ImmutableArray<string> ValidateDecisionIds(ImmutableArray<string> ids)
    {
        if (ids.IsDefault || ids.Length > 64) throw new ArgumentException("At most 64 decision IDs may be supplied.", nameof(ids));
        var result = ImmutableArray.CreateBuilder<string>(ids.Length);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            if (!LowercaseHex.Is(id, 32) || !seen.Add(id))
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

    public static void ValidateListFilter(DraftListFilter filter)
    {
        if (!Enum.IsDefined(filter.Status)) throw new ArgumentOutOfRangeException(nameof(filter));
        if (filter.From > filter.To) throw new DraftListDateRangeException();
        foreach (var id in new[] { filter.IssueId, filter.OfficialId })
        {
            if (id is not null && (string.IsNullOrWhiteSpace(id) || id.Length > MaximumFilterIdLength))
                throw new ArgumentException("A filter ID must contain 1 to 128 characters.", nameof(filter));
        }
    }
}
