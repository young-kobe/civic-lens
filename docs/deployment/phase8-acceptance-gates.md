# Phase 8 acceptance gates — verification commands

Run these on the box once the recompute queue drains, before starting the
Phase 11 cutover runbook. All four must pass. Every command is paste-ready
from `/opt/civic-lens`.

`PSQL` below is shorthand for the container psql invocation:

```bash
PSQL='docker compose exec -T postgres psql -U civic -d civic_lens'
```

## Gate 1 — queue drained, failure rate <5% per task

```bash
$PSQL -c "
SELECT task,
       count(*) FILTER (WHERE status = 'pending')     AS pending,
       count(*) FILTER (WHERE status = 'in_progress') AS in_progress,
       count(*) FILTER (WHERE status = 'done')        AS done,
       count(*) FILTER (WHERE status = 'failed')      AS failed,
       round(100.0 * count(*) FILTER (WHERE status = 'failed')
             / nullif(count(*), 0), 2)                AS failed_pct
FROM ops.task_queue
GROUP BY task
ORDER BY task;"
```

Pass: `pending = 0`, `in_progress = 0`, and `failed_pct < 5` on every row.

A non-zero `pending` with a live `analyze-pg` process just means the run is
still going. A non-zero `in_progress` with no live process means stale
claims — they reset to `pending` at the next pipeline start, not by hand.

## Gate 2 — 20-doc traceability chain

Doc -> `is_current` run -> typed result row -> prompt version -> raw hash.

```bash
$PSQL -c "
WITH sample AS (
    SELECT doc_id, source_type, raw_hash
    FROM corpus.documents
    ORDER BY random()
    LIMIT 20
)
SELECT s.doc_id,
       s.source_type,
       r.task,
       r.status,
       r.inference_method,
       pv.prompt_version,
       (r.confidence IS NOT NULL) AS has_confidence,
       CASE r.task
           WHEN 'bot'        THEN EXISTS (SELECT 1 FROM analysis.bot_signals        t WHERE t.run_id = r.run_id)
           WHEN 'text'       THEN EXISTS (SELECT 1 FROM analysis.sentiment_results  t WHERE t.run_id = r.run_id)
           WHEN 'targets'    THEN EXISTS (SELECT 1 FROM analysis.target_mentions    t WHERE t.run_id = r.run_id)
           WHEN 'propaganda' THEN EXISTS (SELECT 1 FROM analysis.propaganda_results t WHERE t.run_id = r.run_id)
           WHEN 'claims'     THEN EXISTS (SELECT 1 FROM analysis.claims             t WHERE t.run_id = r.run_id)
           WHEN 'citations'  THEN EXISTS (SELECT 1 FROM analysis.citations          t WHERE t.run_id = r.run_id)
       END AS has_rows,
       s.raw_hash
FROM sample s
JOIN analysis.runs r
  ON r.doc_id = s.doc_id AND r.is_current
LEFT JOIN analysis.prompt_versions pv
  ON pv.prompt_version_id = r.prompt_version_id
ORDER BY s.doc_id, r.task;"
```

Pass criteria, per row:

- `has_rows = false` is a **failure only for `bot`, and for `propaganda`
  runs whose `inference_method` is `llm`**. A `deterministic` propaganda run
  is the trivial-content gate declining to judge (added 2026-07-25, matching
  `text`/`targets`/`claims`) and correctly has no result row. `text`,
  `targets`, `claims`, and `citations` may legitimately be empty — no
  sentiment row for trivial content, no targets named, no claims extracted,
  no links present.
- A `propaganda` run with `inference_method = 'deterministic'` **and** a
  result row is a pre-2026-07-25 artifact: the retired loaded-language
  keyword pre-filter wrote `density = 0.0` without ever calling the model.
  Those rows are only corrected by a propaganda re-run.
- `prompt_version` NULL is expected for `inference_method = 'deterministic'`
  (citations, lean derivation) and a failure for `llm` runs.
- `account_tier` never appears here — it is author-scoped, not doc-scoped.

## Gate 3 — whole-corpus contract violations (every count must be 0)

```bash
$PSQL -c "
SELECT 'llm done run missing prompt_version' AS violation, count(*) AS n
  FROM analysis.runs
 WHERE is_current AND inference_method = 'llm' AND status = 'done'
   AND prompt_version_id IS NULL
UNION ALL
SELECT 'llm done run missing confidence', count(*)
  FROM analysis.runs
 WHERE is_current AND inference_method = 'llm' AND status = 'done'
   AND confidence IS NULL
UNION ALL
SELECT 'llm done run missing raw_response', count(*)
  FROM analysis.runs
 WHERE is_current AND inference_method = 'llm' AND status = 'done'
   AND raw_response IS NULL
UNION ALL
SELECT 'done bot run with no bot_signals row', count(*)
  FROM analysis.runs r
 WHERE r.is_current AND r.task = 'bot' AND r.status = 'done'
   AND NOT EXISTS (SELECT 1 FROM analysis.bot_signals b WHERE b.run_id = r.run_id)
UNION ALL
SELECT 'done propaganda run with no propaganda_results row', count(*)
  FROM analysis.runs r
 WHERE r.is_current AND r.task = 'propaganda' AND r.status = 'done'
   AND NOT EXISTS (SELECT 1 FROM analysis.propaganda_results p WHERE p.run_id = r.run_id)
UNION ALL
SELECT 'failed run with no error text', count(*)
  FROM analysis.runs
 WHERE status = 'failed' AND (error IS NULL OR error = '');"
```

This is the machine-checkable form of the AI-output contract in `CLAUDE.md`
(every LLM row carries confidence, model_id, prompt_version) plus the
schema's own `runs.error` / `runs.raw_response` separation.

**`llm done run missing confidence` needs a second look before you treat it
as a failure.** A run whose confidence is the mean over extracted items has
nothing to average when the model legitimately extracts nothing — a
`targets` run on a doc that takes no evaluative position, or a `claims` run
that found no checkable claim. NULL is the honest value there; 0.0 would
assert a confidence nobody computed. The 2026-07-25 run reported 617 such
rows, which is expected rather than a defect.

The real violation is a run that produced result rows but no confidence.
Split them:

```bash
$PSQL -c "
SELECT r.task,
       count(*) FILTER (WHERE has_rows)     AS with_rows_no_conf,
       count(*) FILTER (WHERE NOT has_rows) AS empty_result_no_conf
FROM (
    SELECT r.task, r.run_id,
           CASE r.task
               WHEN 'targets' THEN EXISTS (SELECT 1 FROM analysis.target_mentions t WHERE t.run_id = r.run_id)
               WHEN 'claims'  THEN EXISTS (SELECT 1 FROM analysis.claims          t WHERE t.run_id = r.run_id)
               WHEN 'text'    THEN EXISTS (SELECT 1 FROM analysis.sentiment_results t WHERE t.run_id = r.run_id)
               ELSE true
           END AS has_rows
    FROM analysis.runs r
    WHERE r.is_current AND r.inference_method = 'llm'
      AND r.status = 'done' AND r.confidence IS NULL
) r
GROUP BY r.task ORDER BY r.task;"
```

Pass: `with_rows_no_conf` is 0 on every row. `empty_result_no_conf` may be
any value — that column is the honest-NULL case.

## Gate 4 — every raw_hash resolves to a file on disk

The raw store is sharded by the first two hex characters, and the extension
varies by source type (`.html` for news), so the check globs `<hash>.*`
rather than assuming one suffix.

```bash
RAW=/var/lib/civic-lens/data/raw/sha256
docker compose exec -T postgres psql -U civic -d civic_lens -At -c \
  "SELECT DISTINCT raw_hash FROM corpus.documents WHERE raw_hash <> '';" \
  > /var/tmp/raw_hashes.txt

missing=0; total=0
while read -r h; do
  total=$((total + 1))
  compgen -G "$RAW/${h:0:2}/$h.*" > /dev/null || { echo "MISSING $h"; missing=$((missing + 1)); }
done < /var/tmp/raw_hashes.txt
echo "total=$total missing=$missing"
```

Pass: `missing=0`.

If misses cluster on one source type, check which before assuming
corruption — group them by source before drawing a conclusion:

```bash
$PSQL -c "
SELECT source_type, count(*) AS docs, count(DISTINCT raw_hash) AS hashes
FROM corpus.documents
GROUP BY source_type ORDER BY source_type;"
```

Delete `/var/tmp/raw_hashes.txt` afterwards along with the other
`/var/tmp/*.log` files that contain the DSN.

## Corpus composition — check this before trusting any of the above

**All four gates passed on 2026-07-25 against a corpus containing zero news
and zero Reddit documents.** The composition query returned only
`x_post`: 2,377 `sampled` and 450 `official_record`, 2,827 total. The gates
verify that analysis is internally consistent; they say nothing about whether
the corpus is what you think it is. Always run this first:

```bash
$PSQL -c "
SELECT source_type, admission_class, count(*) AS docs,
       min(published_at)::date AS earliest,
       max(published_at)::date AS latest
FROM corpus.documents
GROUP BY source_type, admission_class
ORDER BY source_type, admission_class;"
```

### Why news was empty — working-directory mismatch

`settings.raw_store_dir` defaults to `data/raw/sha256`, resolved against the
**current working directory**. Only the news loader reads it:
`load_new_documents()` passes `root` to `_load_news()` alone, while
`_load_reddit()` and `_load_x()` take their payloads from `raw.*` tables in
Postgres and never touch disk. So a wrong raw-store path silently zeroes news
while X and Reddit load normally — exactly the observed shape.

The two run paths resolve it differently:

- **Containers** (`docker compose run --rm analyze`) set
  `working_dir: /var/lib/civic-lens` and `env_file: /etc/civic-lens.env`, so
  the relative path lands on `/var/lib/civic-lens/data/raw/sha256`. Correct.
- **`./run.sh analyze-pg` from `/opt/civic-lens`** loads
  `/opt/civic-lens/.env` (not `/etc/civic-lens.env`) and never changes
  directory, so the same relative path lands on
  `/opt/civic-lens/data/raw/sha256`. Every news candidate fails extraction.

Confirm before fixing:

```bash
# Is the news HTML actually on disk? (well above the admitted-doc hash count)
find /var/lib/civic-lens/data/raw/sha256 -type f | wc -l
# Did the crawler capture news at all?
$PSQL -c "SELECT count(*) FROM raw.articles;"
$PSQL -c "SELECT count(*) FROM raw.pages;"
# What did the last ETL actually reject?
grep -h 'ETL \[' /var/tmp/recompute*.log | tail -5
```

A `rejections={'extraction_failed': N}` tally with a large N confirms it. The
fix is an **absolute** path in `/opt/civic-lens/.env`, which is correct under
both run paths:

```
CIVIC_RAW_STORE_DIR=/var/lib/civic-lens/data/raw/sha256
```

Then re-run the ETL stage and re-check composition before re-running any
analysis stage.

## After all four pass

Owner UI pass: every tab against the live API, plus the side-by-side
against the old stack. Expect explainable diffs rather than identical
numbers — pooled net tone, no-sentiment-row for trivial content, and the
tightened politics filter all move counts legitimately. Then Phase 11.
