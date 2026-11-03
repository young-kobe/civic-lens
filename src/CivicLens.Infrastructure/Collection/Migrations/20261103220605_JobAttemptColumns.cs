using CivicLens.Collection.Contracts;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    /// <inheritdoc />
    public partial class JobAttemptColumns : Migration
    {
        private const long DotNetEpochMicroseconds = 62_135_596_800_000_000;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("LOCK TABLE collection_attempts, collection_job_attempts IN SHARE ROW EXCLUSIVE MODE");

            migrationBuilder.Sql("""
                ALTER TABLE collection_attempts ADD COLUMN request_count integer, ADD COLUMN bytes_received bigint;
                UPDATE collection_attempts a
                SET request_count = (j.resolution_json::jsonb #>> '{receipt,requestCount}')::integer,
                    bytes_received = (j.resolution_json::jsonb #>> '{receipt,bytesReceived}')::bigint
                FROM collection_job_attempts j
                WHERE j.attempt_id = a.attempt_id AND jsonb_typeof(j.resolution_json::jsonb -> 'receipt') = 'object';
                ALTER TABLE collection_attempts ADD CONSTRAINT ck_collection_attempts_transfer CHECK (
                    (request_count IS NULL) = (bytes_received IS NULL) AND (request_count IS NULL OR request_count >= 0 AND bytes_received >= 0 AND (bytes_received = 0 OR request_count > 0)));
                """);

            migrationBuilder.Sql($$"""
                ALTER TABLE collection_job_attempts
                    ADD COLUMN request_version integer, ADD COLUMN request_job_id text, ADD COLUMN request_source_id text,
                    ADD COLUMN request_url text, ADD COLUMN request_allowed_origin text, ADD COLUMN request_allowed_path_prefix text,
                    ADD COLUMN request_artifact_directory text, ADD COLUMN request_mode character varying(16),
                    ADD COLUMN request_max_candidates integer, ADD COLUMN request_max_requests integer, ADD COLUMN request_max_bytes bigint,
                    ADD COLUMN request_timeout_seconds integer, ADD COLUMN request_min_delay_milliseconds integer,
                    ADD COLUMN request_etag text, ADD COLUMN request_last_modified_utc_ticks bigint, ADD COLUMN request_credential text,
                    ADD COLUMN outcome character varying(16), ADD COLUMN error_code text, ADD COLUMN retry_delay_ticks bigint,
                    ADD COLUMN robots_crawl_delay_milliseconds bigint;
                UPDATE collection_job_attempts t SET
                    request_version = (s.req ->> 'version')::integer,
                    request_job_id = s.req ->> 'jobId',
                    request_source_id = s.req ->> 'sourceId',
                    request_url = s.req ->> 'url',
                    request_allowed_origin = s.req ->> 'allowedOrigin',
                    request_allowed_path_prefix = s.req ->> 'allowedPathPrefix',
                    request_artifact_directory = s.req ->> 'artifactDirectory',
                    request_mode = CASE s.req ->> 'mode' WHEN 'page' THEN 'Page' WHEN 'feed' THEN 'Feed' WHEN 'html' THEN 'Html' WHEN 'api' THEN 'Api' END,
                    request_max_candidates = (s.req ->> 'maxCandidates')::integer,
                    request_max_requests = (s.req ->> 'maxRequests')::integer,
                    request_max_bytes = (s.req ->> 'maxBytes')::bigint,
                    request_timeout_seconds = (s.req ->> 'timeoutSeconds')::integer,
                    request_min_delay_milliseconds = (s.req ->> 'minDelayMilliseconds')::integer,
                    request_etag = s.req ->> 'eTag',
                    request_last_modified_utc_ticks = (round(extract(epoch FROM (s.req ->> 'lastModified')::timestamptz)::numeric * 1000000)::bigint
                        + {{DotNetEpochMicroseconds}}) * 10,
                    request_credential = s.req ->> 'credential',
                    outcome = CASE s.res ->> 'outcome' WHEN '0' THEN 'Succeeded' WHEN '1' THEN 'RetryableFailure'
                        WHEN '2' THEN 'PermanentFailure' WHEN '3' THEN 'Interrupted' END,
                    error_code = s.res ->> 'errorCode',
                    retry_delay_ticks = (SELECT coalesce(m[1], '0')::bigint * 864000000000 + m[2]::bigint * 36000000000 + m[3]::bigint * 600000000
                            + m[4]::bigint * 10000000 + rpad(coalesce(m[5], ''), 7, '0')::bigint
                        FROM regexp_match(s.res ->> 'retryDelay', '^(?:([0-9]+)\.)?([0-9]{2}):([0-9]{2}):([0-9]{2})(?:\.([0-9]{7}))?$') AS m),
                    robots_crawl_delay_milliseconds = (s.res ->> 'robotsCrawlDelayMilliseconds')::bigint
                FROM (SELECT attempt_id, request_json::jsonb AS req, resolution_json::jsonb AS res FROM collection_job_attempts) s
                WHERE t.attempt_id = s.attempt_id;
                """);

            migrationBuilder.Sql("""
                DO $check$
                DECLARE lost text;
                BEGIN
                    SELECT attempt_id INTO lost FROM collection_job_attempts t, LATERAL
                        (SELECT request_json::jsonb AS req, resolution_json::jsonb AS res) s
                    WHERE (s.res ->> 'retryDelay' IS NOT NULL AND t.retry_delay_ticks IS NULL)
                        OR (s.req ->> 'lastModified' IS NOT NULL AND t.request_last_modified_utc_ticks IS NULL)
                        OR (s.req ->> 'credential' IS NOT NULL AND t.request_credential IS NULL)
                    ORDER BY attempt_id LIMIT 1;
                    IF lost IS NOT NULL THEN
                        RAISE EXCEPTION 'Cannot drop request_json and resolution_json: a value of attempt % did not reach its column', lost;
                    END IF;
                END
                $check$
                """);

            migrationBuilder.Sql($$"""
                ALTER TABLE collection_job_attempts
                    ALTER COLUMN request_version SET NOT NULL, ALTER COLUMN request_job_id SET NOT NULL,
                    ALTER COLUMN request_source_id SET NOT NULL, ALTER COLUMN request_url SET NOT NULL,
                    ALTER COLUMN request_allowed_origin SET NOT NULL, ALTER COLUMN request_allowed_path_prefix SET NOT NULL,
                    ALTER COLUMN request_artifact_directory SET NOT NULL, ALTER COLUMN request_mode SET NOT NULL,
                    ALTER COLUMN request_max_candidates SET NOT NULL, ALTER COLUMN request_max_requests SET NOT NULL,
                    ALTER COLUMN request_max_bytes SET NOT NULL, ALTER COLUMN request_timeout_seconds SET NOT NULL,
                    ALTER COLUMN request_min_delay_milliseconds SET NOT NULL;
                DROP INDEX "IX_collection_job_attempts_job_id";
                ALTER TABLE collection_job_attempts DROP CONSTRAINT ck_job_attempt_resolution;
                ALTER TABLE collection_job_attempts DROP COLUMN request_json, DROP COLUMN resolution_json;
                CREATE UNIQUE INDEX "IX_collection_job_attempts_job_id" ON collection_job_attempts (job_id) WHERE completed_at IS NULL;
                ALTER TABLE collection_job_attempts
                    ADD CONSTRAINT ck_job_attempt_request_identity CHECK (request_version > 0 AND length(btrim(request_job_id)) > 0 AND length(btrim(request_source_id)) > 0 AND length(btrim(request_url)) > 0),
                    ADD CONSTRAINT ck_job_attempt_request_mode CHECK (request_mode IN ('Page','Feed','Html','Api')),
                    ADD CONSTRAINT ck_job_attempt_request_budgets CHECK (request_max_candidates BETWEEN 1 AND 1000 AND request_max_requests BETWEEN 2 AND 20 AND request_max_bytes BETWEEN 1 AND 100000000 AND request_timeout_seconds BETWEEN 1 AND 300 AND request_min_delay_milliseconds BETWEEN 0 AND 60000),
                    ADD CONSTRAINT ck_job_attempt_request_last_modified_utc_ticks CHECK (request_last_modified_utc_ticks IS NULL OR request_last_modified_utc_ticks BETWEEN 0 AND 3155378975999999999),
                    ADD CONSTRAINT ck_job_attempt_request_credential CHECK (request_credential IS NULL OR request_mode = 'Api'),
                    ADD CONSTRAINT ck_job_attempt_resolution CHECK ((outcome IS NULL) = (completed_at IS NULL) AND (completed_at IS NOT NULL OR error_code IS NULL AND retry_delay_ticks IS NULL AND robots_crawl_delay_milliseconds IS NULL)),
                    ADD CONSTRAINT ck_job_attempt_outcome CHECK (outcome IS NULL OR outcome IN ('Succeeded','RetryableFailure','PermanentFailure','Interrupted')),
                    ADD CONSTRAINT ck_job_attempt_retry_ticks CHECK (retry_delay_ticks IS NULL OR retry_delay_ticks >= 0),
                    ADD CONSTRAINT ck_job_attempt_robots_delay CHECK (robots_crawl_delay_milliseconds IS NULL OR (robots_crawl_delay_milliseconds BETWEEN 0 AND {{CollectionProtocol.MaximumCrawlDelayMilliseconds}} AND outcome <> 'Interrupted'));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($$"""
                LOCK TABLE collection_attempts, collection_job_attempts IN SHARE ROW EXCLUSIVE MODE;
                ALTER TABLE collection_job_attempts ADD COLUMN request_json text, ADD COLUMN resolution_json text;
                UPDATE collection_job_attempts t SET
                    request_json = (jsonb_build_object(
                        'version', t.request_version, 'jobId', t.request_job_id, 'sourceId', t.request_source_id, 'url', t.request_url,
                        'allowedOrigin', t.request_allowed_origin, 'allowedPathPrefix', t.request_allowed_path_prefix,
                        'artifactDirectory', t.request_artifact_directory, 'mode', lower(t.request_mode),
                        'maxCandidates', t.request_max_candidates, 'maxRequests', t.request_max_requests, 'maxBytes', t.request_max_bytes,
                        'timeoutSeconds', t.request_timeout_seconds, 'minDelayMilliseconds', t.request_min_delay_milliseconds,
                        'eTag', t.request_etag,
                        'lastModified', to_char(timestamp '0001-01-01' + (t.request_last_modified_utc_ticks / 864000000000) * interval '1 day'
                            + (t.request_last_modified_utc_ticks % 864000000000 / 10) * interval '1 microsecond', 'YYYY-MM-DD"T"HH24:MI:SS')
                            || '.' || lpad((t.request_last_modified_utc_ticks % 10000000)::text, 7, '0') || '+00:00')
                        || CASE WHEN t.request_credential IS NULL THEN '{}'::jsonb ELSE jsonb_build_object('credential', t.request_credential) END)::text,
                    resolution_json = CASE WHEN t.outcome IS NULL THEN NULL ELSE jsonb_build_object(
                        'outcome', CASE t.outcome WHEN 'Succeeded' THEN 0 WHEN 'RetryableFailure' THEN 1 WHEN 'PermanentFailure' THEN 2 ELSE 3 END,
                        'receipt', NULL, 'errorCode', t.error_code,
                        'retryDelay', CASE WHEN t.retry_delay_ticks IS NULL THEN NULL ELSE
                            CASE WHEN t.retry_delay_ticks >= 864000000000 THEN (t.retry_delay_ticks / 864000000000) || '.' ELSE '' END
                            || lpad((t.retry_delay_ticks % 864000000000 / 36000000000)::text, 2, '0')
                            || ':' || lpad((t.retry_delay_ticks % 36000000000 / 600000000)::text, 2, '0')
                            || ':' || lpad((t.retry_delay_ticks % 600000000 / 10000000)::text, 2, '0')
                            || CASE WHEN t.retry_delay_ticks % 10000000 > 0 THEN '.' || lpad((t.retry_delay_ticks % 10000000)::text, 7, '0') ELSE '' END END,
                        'robotsCrawlDelayMilliseconds', t.robots_crawl_delay_milliseconds)::text END;
                ALTER TABLE collection_job_attempts ALTER COLUMN request_json SET NOT NULL;
                DROP INDEX "IX_collection_job_attempts_job_id";
                ALTER TABLE collection_job_attempts
                    DROP COLUMN request_version, DROP COLUMN request_job_id, DROP COLUMN request_source_id, DROP COLUMN request_url,
                    DROP COLUMN request_allowed_origin, DROP COLUMN request_allowed_path_prefix, DROP COLUMN request_artifact_directory,
                    DROP COLUMN request_mode, DROP COLUMN request_max_candidates, DROP COLUMN request_max_requests, DROP COLUMN request_max_bytes,
                    DROP COLUMN request_timeout_seconds, DROP COLUMN request_min_delay_milliseconds, DROP COLUMN request_etag,
                    DROP COLUMN request_last_modified_utc_ticks, DROP COLUMN request_credential, DROP COLUMN outcome, DROP COLUMN error_code,
                    DROP COLUMN retry_delay_ticks, DROP COLUMN robots_crawl_delay_milliseconds;
                CREATE UNIQUE INDEX "IX_collection_job_attempts_job_id" ON collection_job_attempts (job_id) WHERE resolution_json IS NULL;
                ALTER TABLE collection_job_attempts ADD CONSTRAINT ck_job_attempt_resolution CHECK ((resolution_json IS NULL) = (completed_at IS NULL));
                ALTER TABLE collection_attempts DROP CONSTRAINT ck_collection_attempts_transfer;
                ALTER TABLE collection_attempts DROP COLUMN request_count, DROP COLUMN bytes_received;
                """);
        }
    }
}
