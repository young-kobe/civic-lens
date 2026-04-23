# TODO — Feed news / political-entity signals into bot + propaganda calculations

**Status:** In progress. Bot-detector §"Sanitize whyFlagged at source" landed 2026-04-23; other bot items partially covered by `_enrich_x_metadata` pre-exclusion in `job_runner.py`. Propaganda items still deferred.

**Why this doc exists:** The UI redesign built curated registries for
major news outlets, verified officials, and major subreddits (see
`docs/audit-trail/ui/` entries). Those registries are a proprietary
signal we can feed *back into* the AI detectors to improve accuracy.
Currently the bot detector and propaganda detector treat every account /
domain as mostly-anonymous. That's leaving signal on the floor.

Each bullet below, when picked up, lands as a dated entry under
`docs/audit-trail/analysis/` — not a walkthrough (the walkthroughs
sequence was retired in favor of layered audit-trail buckets; see
`docs/audit-trail/README.md`).

---

## Bot detector — entity signals to incorporate

### Problem today

Bot-likelihood is computed from purely behavioral signals: account age,
follower / following ratios, posting cadence, text-repetition rate,
coordination timing. The detector has no notion of "is this account a
known human political actor".

That produces two failure modes:

1. **False positives on verified officials.** An official account that
   posts at off-hours (state visits abroad), publishes boilerplate
   press-release text, or is run by a staff team with mechanical posting
   rhythm gets flagged as "suspected automation" when the posts are
   legitimate communications from a named human officeholder.
2. **Missed context on flagged amplifiers.** Noise in `whyFlagged`
   (e.g. `followers=0`, `account_age=None`) originates in the LLM
   prompt (missing signal fields render as the string "None", which
   the LLM echoes back in its indicators list).

### What to add

- [ ] **Registry lookup as a negative signal** — needs rethinking.
  Partial support via `_enrich_x_metadata` in
  `analysis/src/scheduler/job_runner.py` pre-excludes X accounts whose
  `verified_type='government'` (White House / federal agency channels
  that are human-run by institutional necessity). A wider rule that
  pre-excluded any `tier in ("elected_official", "affiliated")` account
  was REVERTED on 2026-04-23: politicians and their staff legitimately
  use automation (scheduled cross-posting, party-coordinated messaging,
  platform-native scheduling tools), and blanket exclusion made the
  "Politicians & Officials" tier on the Bot Detector page permanently
  empty. Elected officials now flow through the normal bot pipeline.
  The future design question for this item is: is there a more surgical
  registry-based signal than a blanket cap? Possibilities: downweight
  (not zero) for registry matches, exclude only for specific
  high-verification institutional accounts, or always-run-but-separate
  calibration cohort.
- [ ] **Registry lookup as a positive-exemption signal.** For Reddit
  moderator accounts of major political subreddits (listed in
  `major_subreddits.yaml` once built), reduce bot-likelihood by one
  notch — these are operationally active human accounts.
- [x] **Sanitize `whyFlagged` at source** — landed 2026-04-23. Two-part
  fix in `analysis/src/engine/bot.py`:
    1. `_safe_prompt_value()` substitutes None / missing signal fields
       with "unknown" in the LLM prompt, so the model never sees a
       placeholder it might echo verbatim.
    2. `_sanitize_llm_indicators()` filters the returned indicators
       array, dropping any `=None` / `=0` / `=null` / `=undefined` /
       trailing-`=` entries that slip through.
  Unit tests in `analysis/tests/test_bot_indicator_sanitization.py`
  (12 cases).
- [ ] **Remove the UI-side transition-period noise filter.** After
  landing the backend sanitizer, production screenshots still showed
  `account_age=None days` on the Bot Detector page because the 24h /
  7d / 30d snapshot windows still held `ai_outputs` rows written by
  the pre-fix detector. On 2026-04-23 a slim `isNoiseLabel()` guard
  was re-added to `ui/src/pages/BotActivityProfiler.tsx` as a pure
  display-time filter: it drops noise entries from `whyFlagged`, picks
  the first non-noise narrative for the banner, and suppresses
  amplification cards whose narrative label itself is an artifact
  (e.g. `ACCOUNT_AGE=NONE DAYS`). Once every pre-fix row has aged out
  of the 30d window — which happens automatically after the scheduled
  `analyze` + `snapshots` pipeline rebuilds the cache past 2026-05-23
  (30 days after the sanitizer landed) — this filter becomes a
  no-op and should be removed along with the `isNoiseLabel()` helper.
  To accelerate: run `./run.ps1 analyze` (or `systemctl start
  civic-lens-analyze`) against a DB whose narratives/indicators
  frequency-maps have dropped below the noise threshold, then delete
  the filter in the same PR.
- [ ] **Per-entity amplification attribution.** When the detector
  identifies coordinated amplification of a narrative, surface *which
  registered entity (official / outlet / subreddit)* the amplification
  is targeting or originating from. Currently the amplification cards
  list raw targets; joining to the registry gives them editorial
  context.

### Where the code lives

- `analysis/src/engine/bot.py` (+ prompt/schema in `analysis/src/llm/`).
- `analysis/src/scheduler/job_runner.py::_enrich_x_metadata` already does
  the pre-exclusion pass; extending to consult `verified_officials.yaml`
  directly happens there.
- Registry-match signal is applied late in the scoring pipeline (pre-LLM
  exclusion or post-classification suppression) so the audit trail retains
  the raw behavioral numbers that *would have* flagged the account.

### Audit-trail entry

When picked up: entry under `docs/audit-trail/analysis/` covers the
behavioral-raw-scores vs final-adjusted scores split, audit record
changes, and any schema addition on `ai_outputs` (e.g. a
`registry_match` field) if needed.

---

## Propaganda detector — entity signals to incorporate

### Problem today

Per-doc LLM classification looking for six techniques. No context
about source. An opinion piece from a known partisan outlet and a
straight news wire from AP get the same prompt and the same prior.

### What to add (carefully)

Politically sensitive area — how we use entity lean labels as detector
input matters. Options, cheapest to most invasive:

- [ ] **Post-hoc calibration / audit (safest).** Track propaganda rate
  *by outlet* and *by official*. If one lean bucket gets
  disproportionately flagged, that's information — either about the
  outlet (plausible) or about detector bias (also plausible). Surface
  the distribution in an admin-only calibration view. No prompt change;
  entity data is used only for reporting.
- [ ] **Entity context in the prompt (careful).** Include a line in the
  classification prompt: "Source: {outlet} (lean: {AllSides rating})".
  May improve calibration — but risks encoding the bias source into
  the classifier. Must A/B against the no-context prompt on the
  golden set before adopting; ship only if evaluation shows clear
  improvement and no lean-based differential misclassification.
- [ ] **Entity-aware thresholding.** Same detector, but the flag
  threshold varies by source type. Opinion sections / explicitly
  editorial content might use a higher threshold (propaganda techniques
  appear in earnest editorial writing), while straight news wire copy
  uses a lower threshold. Requires `article_type` data on docs that we
  may not reliably have.
- [ ] **Cross-entity narrative propagation flagging.** If a narrative
  first appears on a low-verified-standing account and then gets
  picked up across multiple outlets within a short window, flag as
  potentially laundered. Requires the cross-tier narrative work in
  ui-redesign-plan.md Phase 6 to land first.

### Where the code lives

- `analysis/src/engine/propaganda_detector.py`.
- Prompt: `analysis/src/llm/prompts.py` (bump prompt version if the
  prompt changes — mandatory, see CLAUDE.md AI output contract).

### Audit-trail entry

When picked up: entry must include A/B evaluation results against the
golden set — especially per-lean-bucket flag rates, to verify the
detector isn't just learning "mark right-leaning outlets as propaganda"
or vice versa. This is a tripwire for the project's audit-first
invariant.

---

## Ordering

- Registries already exist (UI redesign Phase 2 completed).
- Bot-detector "sanitize whyFlagged" landed 2026-04-23.
- Remaining bot items next (lower risk, higher immediate value).
- Propaganda work last, and only with measurable eval evidence.

---

## Revisit cadence

- 2026-04-21 — Doc created.
- 2026-04-23 — Sanitize-at-source landed; registry de-bias partially
  covered; remaining items re-ordered by risk.
