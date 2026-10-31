-- Gives civic_lens_readonly SELECT on operational columns that hold no review notes, reviewer identity, or credentials.
-- Run again after a migration, because a new table or column stays unreadable until then.
\set ON_ERROR_STOP on

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'civic_lens_readonly') THEN
    CREATE ROLE civic_lens_readonly LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT CONNECTION LIMIT 3;
  END IF;
END $$;

ALTER ROLE civic_lens_readonly SET default_transaction_read_only = on;
ALTER ROLE civic_lens_readonly SET statement_timeout = '30s';

DO $$
BEGIN
  EXECUTE format('GRANT CONNECT ON DATABASE %I TO civic_lens_readonly', current_database());
END $$;
GRANT USAGE ON SCHEMA public TO civic_lens_readonly;

DO $$
DECLARE
  hidden_column constant text := '(^|_)(note|body|secret|password|email|credential|reviewer|actor)($|_)'
    || '|(^|_)(subject|token|key)$|^resolved_by$'
    || '|^(revision|decision|label|request|definition|input|resolution|discovery|result)_json$';
  hidden_table constant text[] := ARRAY['document_change_review_idempotency'];
  listed record;
  allowed text;
BEGIN
  FOR listed IN
    SELECT c.oid, c.relname FROM pg_class c
    JOIN pg_namespace n ON n.oid = c.relnamespace
    WHERE n.nspname = 'public' AND c.relkind IN ('r', 'p')
  LOOP
    EXECUTE format('REVOKE ALL ON public.%I FROM civic_lens_readonly', listed.relname);
    CONTINUE WHEN listed.relname = ANY (hidden_table);
    SELECT string_agg(quote_ident(a.attname), ', ') INTO allowed
      FROM pg_attribute a
     WHERE a.attrelid = listed.oid AND a.attnum > 0 AND NOT a.attisdropped AND a.attname !~ hidden_column;
    IF allowed IS NOT NULL THEN
      EXECUTE format('GRANT SELECT (%s) ON public.%I TO civic_lens_readonly', allowed, listed.relname);
    END IF;
  END LOOP;
END $$;

CREATE SCHEMA IF NOT EXISTS readonly_access;
GRANT USAGE ON SCHEMA readonly_access TO civic_lens_readonly;

-- The body is plain text to Postgres, so this function never blocks a migration that changes or drops a revision column.
CREATE OR REPLACE FUNCTION readonly_access.revisions(table_name text)
RETURNS TABLE (row_data jsonb, revision jsonb)
LANGUAGE plpgsql STABLE SECURITY DEFINER
SET search_path = public, pg_temp
AS $$
BEGIN
  IF table_name !~ '^[a-z_]+_revisions$' THEN
    RAISE EXCEPTION 'Name a table that ends in _revisions.';
  END IF;
  RETURN QUERY EXECUTE format(
    'SELECT to_jsonb(t) - ''author_subject'' - ''revision_json'',
            t.revision_json::jsonb - ''authorSubject'' - ''AuthorSubject''
       FROM %I t', table_name);
END $$;

REVOKE ALL ON FUNCTION readonly_access.revisions(text) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION readonly_access.revisions(text) TO civic_lens_readonly;
