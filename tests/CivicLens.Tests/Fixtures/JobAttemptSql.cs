using CivicLens.Collection.Contracts;

namespace CivicLens.Tests.Fixtures;

internal static class JobAttemptSql
{
    // The arguments attempt, job and sequence are SQL fragments, so a caller can pass a parameter name or a subquery.
    public static string Insert(string attempt, string job, string sequence, string startedAt, CollectionRequest? request = null,
        string? outcome = null, long? completedAt = null)
    {
        request ??= CollectionFixtures.Request();
        var resolution = completedAt is null ? "" : $", outcome, completed_at";
        var resolved = completedAt is null ? "" : $", '{outcome ?? "Succeeded"}', {completedAt}";
        return $"""
            INSERT INTO collection_job_attempts (attempt_id, job_id, sequence, request_version, request_job_id, request_source_id, request_url,
                request_allowed_origin, request_allowed_path_prefix, request_artifact_directory, request_mode, request_max_candidates,
                request_max_requests, request_max_bytes, request_timeout_seconds, request_min_delay_milliseconds, started_at{resolution})
            VALUES ({attempt}, {job}, {sequence}, {request.Version}, {Literal(request.JobId)}, {Literal(request.SourceId)}, {Literal(request.Url)},
                {Literal(request.AllowedOrigin)}, {Literal(request.AllowedPathPrefix)}, {Literal(request.ArtifactDirectory)}, '{request.Mode}',
                {request.MaxCandidates}, {request.MaxRequests}, {request.MaxBytes}, {request.TimeoutSeconds}, {request.MinDelayMilliseconds},
                {startedAt}{resolved})
            """;
    }

    private static string Literal(string value) => "'" + value.Replace("'", "''") + "'";
}
