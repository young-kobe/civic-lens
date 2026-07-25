# 2026-07-25 — Sentiment panel topic is LLM-only; unclassified reads as "General"

`GET /api/v1/sentiment`'s `byTopic` split (`analysis/src/api/queries/sentiment.py`) no longer guesses a topic from the document title. A doc's topic is either the dominant resolved `analysis.target_mentions.topic` for that `doc_id`, or the literal `"General"` — there is no third path.

## What shipped

- Deleted the `TOPIC_KEYWORDS` dict (five hardcoded topic -> substring-list entries, ported from the legacy `reporting/aggregators/constants.py` list at Phase 9 sentiment build time).
- Rewrote `_topic_for_row(doc_id, doc_topics)`: looks up `doc_topics.get(doc_id, "General")` and returns. The `title` parameter is gone — it was only ever read to run the keyword scan, and the sole call site (`_aggregate_rows`) no longer passes it.
- `_fetch_doc_topics` (unchanged logic) still excludes `target_mentions.topic = 'Other'` rows from the resolved map, so an LLM-classified "Other" doc lands in the same `"General"` bucket as a doc with no `target_mentions` row at all — both mean "the LLM did not put this in a named topic," and the panel does not distinguish "the LLM said Other" from "no targets run produced a topic for this doc" (see Judgment calls).
- Test coverage:
  - `analysis/tests/test_api_queries_sentiment.py::TopicForRowTests` — pure test now asserts a resolved topic is used, and a doc with no entry in `doc_topics` returns `"General"`.
  - `analysis/tests/test_api_queries_sentiment.py::SentimentPanelIntegrationTests::test_doc_with_no_resolved_topic_is_general_not_keyword_guessed` (new, gated) — seeds a doc titled `"new tariff plan announced"` (a string the deleted keyword map would have matched to `"Economy"`) with no `target_mentions` row, and asserts the panel's `by_topic` carries a `"General"` entry and no `"Economy"` entry. This is the regression guard: a title that reads as economic content must not resurrect a keyword guess just because it looks classifiable.
  - `analysis/tests/contract/snapshots/sentiment_panel_basic.json` — the seeded contract fixture's one document is titled `"A tariff plan"` with no `target_mentions` row; its `byTopic[0].topic` changes from `"Economy"` (keyword-guessed) to `"General"` (honest). No other field in the snapshot changed.

## Why

Topic classification is supposed to be an LLM judgment call, made once, by `analysis/src/engine/target_extractor.py`'s targets task and written to `analysis.target_mentions` with a `confidence` and `model_id` under the standard `analysis.runs` traceability contract. The keyword map bypassed that: it ran a plain substring match over `corpus.documents.title` at query time, in the API layer, with no run row, no confidence, and no model attribution. Its output was structurally indistinguishable from a real LLM topic in the response JSON — `TopicSentiment.topic` is just a string — so a UI consumer (or a future engineer reading the panel) had no way to tell "the LLM classified this" from "five hardcoded English substrings happened to match this title." Per the owner's rule for this workstream, unclassified must mean unclassified: the keyword branch is deleted rather than kept as a documented fallback, because a labeled fallback would still be a second classifier the docs would need to keep in sync with the schema's actual 15-value topic list (`analysis/src/llm/prompts.py:141`) — simpler and more honest to have exactly one path.

## Judgment calls

- **`"General"`, not `"Other"`, is the sentinel for "no resolved topic."** Two conventions already exist in this codebase and they answer different questions:
  - The LLM's own targets-task schema (`analysis/src/llm/prompts.py:141`) includes `"Other"` as one of its 15 topic values — the model's considered answer when a mention doesn't fit any named topic. That is itself a resolved classification, not an absence of one, and `_fetch_doc_topics` already treats it as excluded from `doc_topics` (see `_DOC_TOPICS_SQL`'s `m.topic <> 'Other'` filter, unchanged by this PR) precisely so it doesn't crowd out a real topic when a doc has mixed mentions.
  - `"General"` is this panel's pre-existing convention for "nothing to report here" — `_build_response`'s `by_topic` sort already special-cases it (`key=lambda t: (t.topic != "General", -t.volume)`, pinning it first regardless of volume), and the same `"General"`-as-honest-fallback pattern is independently established in the legacy `reporting/aggregators/sentiment/aggregator.py` (its own no-keyword-match path also returns `"General"`, not `"Other"`). Reusing it here keeps one sentinel per meaning: `"Other"` is an LLM answer, `"General"` is the panel's own "not enough signal to bucket" bucket. Introducing `"Other"` for the no-topic case would collide the two.
- **No new schema or column.** The fix is a deletion plus a two-line lookup; nothing about `analysis.target_mentions` or the targets-task prompt changed.

## Follow-ups

None — this closes the gap identified against the Phase 9 sentiment build (`docs/audit-trail/api/2026-07-24-phase9-sentiment-entities.md`, item 7 of "What shipped," which named the keyword map as a fallback at the time).
