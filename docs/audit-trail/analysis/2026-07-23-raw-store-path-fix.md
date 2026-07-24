# 2026-07-23 — News ETL resolves the raw HTML store from settings, not the code tree

The Postgres ETL (`analysis/src/etl/documents.py`) now locates the
content-addressed raw HTML store via `settings.raw_store_dir`
(`CIVIC_RAW_STORE_DIR`, default `data/raw/sha256`, resolved against the
working directory like `db_path`/`cache_dir`), and it tallies news rows whose
raw file cannot be read or extracted under an `extraction_failed` key in
`DocLoadResult.rejections`. A missing store directory additionally logs a
warning naming the resolved path.

## What shipped

- `analysis/src/common/settings.py`: new `raw_store_dir` field (env
  `CIVIC_RAW_STORE_DIR`), cwd-relative like the other data paths.
- `analysis/src/etl/documents.py`: `load_new_documents()` defaults
  `raw_root` to `Path(get_settings().raw_store_dir)`; the old
  `_DEFAULT_RAW_ROOT = REPO_ROOT / "data" / "raw" / "sha256"` module constant
  is gone. `_gather_news_candidates` records `extraction_failed` in
  `rejections` (no named counter — it is not a policy rejection) instead of
  silently `continue`-ing. `_DEFAULT_SEEDS_PATH` stays repo-root-relative on
  purpose: committed YAMLs ship inside the analysis image.
- `analysis/src/etl/constants.py`: `EXTRACTION_FAILED` reason constant.
- `.env.example`: documents `CIVIC_RAW_STORE_DIR`.
- `analysis/tests/test_etl_documents.py`: two gated integration tests — the
  default store location honors `CIVIC_RAW_STORE_DIR` with no `raw_root`
  argument, and a missing raw file surfaces as `extraction_failed: 1`.

## Why

- Prod incident: the first Postgres-stack ETL run admitted 966 `x_post` docs
  and zero news despite ~16.5K migrated `raw.articles` rows. The analyze
  container's code tree lives at `/app` and `.dockerignore` excludes
  `data/raw`, so the old code-tree-relative default (`/app/data/raw/sha256`)
  matched no files while the real store sat at
  `/var/lib/civic-lens/data/raw/sha256` (compose bind mount, working_dir
  `/var/lib/civic-lens`). Every one of the ~13K articles that passed the
  deny/recency pretext gate then failed text extraction and was dropped by an
  uncounted `continue` — zero candidates reached the political filter, with
  no signal in the ETL summary. X/reddit were unaffected because their text
  is carried in the DB rows. The legacy SQLite loader never hit this because
  it derived the store from `db_path`'s parent (data-dir-relative).

## Follow-ups

- None. Prod needs no env change: the default resolves correctly from the
  container working directory.
