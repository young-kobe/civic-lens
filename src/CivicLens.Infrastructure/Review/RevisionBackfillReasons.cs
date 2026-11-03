using System.Collections.Immutable;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CivicLens.Infrastructure.Review;

/// <summary>Owns the reasons that a backfill gives for a skipped revision, and the checks that give them, when every kind of revision can meet them. Operators read the reason strings.</summary>
internal static class RevisionBackfillReasons
{
    public const string UnreadableJson = "UnreadableJson";
    public const string InvalidShape = "InvalidShape";
    public const string DuplicateIssueId = "DuplicateIssueId";
    public const string RebuildMismatch = "RebuildMismatch";
    public const string DatabasePrefix = "database:";

    public static bool HasDuplicates(ImmutableArray<string> ids) => ids.Distinct(StringComparer.Ordinal).Count() != ids.Length;

    public static string? RefusalOf(Exception exception)
    {
        var fault = exception as PostgresException ?? (exception as DbUpdateException)?.InnerException as PostgresException;
        return fault is { SqlState: { } state } && (state.StartsWith("23", StringComparison.Ordinal) || state is PostgresErrorCodes.StringDataRightTruncation or PostgresErrorCodes.CharacterNotInRepertoire)
            ? fault.ConstraintName ?? state
            : null;
    }
}
