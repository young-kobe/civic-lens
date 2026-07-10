# 2026-07-10 — Topic filter matches backend attribution exactly

The Overall Tone topic filter now consumes the backend's per-sample
`topic` field instead of re-implementing the Python keyword lists in the
client. Analysis-layer counterpart:
`docs/audit-trail/analysis/2026-07-10-topic-attribution-wiring.md`.

## What shipped

- `services/topics.ts` is presentation-only (labels, slugs, icons): the
  duplicated `keywords` lists and the `matchesTopic` helper are deleted —
  the drift risk its own docstring warned about is gone. `General` is a
  real taxonomy entry.
- `types.ts::ClassificationSample.topic`; the profile modal
  (`PublicSentiment.tsx`) filters samples by exact `s.topic` match.
  Samples from pre-topic cached snapshots have no field and simply don't
  match until the next snapshot run.
- `TopicTabBar`: `General` renders as a real tab (the honest unclassified
  bucket — hiding it overstated topic coverage), so the "All Topics" count
  now covers every scored post; the "topic-matched" caveat copy is gone.
- `pickDefaultTopic` excludes `General` — it usually has the largest
  volume but is never a substantive landing tab.

## Why

- The topic strings shown to users and the topic strings the analysis
  layer persists are now the same values by construction, not by two
  hand-synced keyword lists.
