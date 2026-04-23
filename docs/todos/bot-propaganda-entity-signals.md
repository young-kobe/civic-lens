# TODO — Feed news / political-entity signals into bot + propaganda calculations

**Status:** Tracked, not started. Companion to [ui-redesign-plan.md](./ui-redesign-plan.md).

**Why this doc exists:** Once the UI redesign builds curated registries for
major news outlets, verified officials, and major subreddits
(ui-redesign-plan.md Phase 2), those registries become a proprietary
signal we can feed *back into* the AI detectors to improve accuracy.
Right now the bot detector and propaganda detector treat every account /
domain as anonymous. That's leaving signal on the floor.

This doc is a reminder list of what to add and how, not a complete design.
When picking this up, promote each bullet into a proper walkthrough.

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
   (e.g. `followers=0`, `account_age=None`) the UI currently strips at
   display time (see walkthrough 052). The canonical fix belongs in the
   detector.

### What to add

- [ ] **Registry lookup as a negative signal.** If the author's handle
  matches `data/verified_officials.yaml` or `data/known_political_x_accounts.yaml`,
  cap the bot-likelihood at `low` regardless of behavioral signals.
  Store the registry match on the output so the audit trail shows
  "excluded by registry match" rather than silent overrides.
- [ ] **Registry lookup as a positive-exemption signal.** For Reddit
  moderator accounts of major political subreddits (listed in
  `major_subreddits.yaml` once built), also reduce bot-likelihood by
  one notch — these are operationally active human accounts.
- [ ] **Sanitize `whyFlagged` at source.** The UI currently filters out
  entries matching `=None`, `=0`, `=null`, `=undefined` before display
  (`sanitizeWhyFlagged` in `BotActivityProfiler.tsx`). Move that into
  the bot detector's signal generator so non-signal strings never
  reach the output. Once this lands, remove the UI workaround.
- [ ] **Per-entity amplification attribution.** When the detector
  identifies coordinated amplification of a narrative, surface *which
  registered entity (official / outlet / subreddit)* the amplification
  is targeting or originating from. Currently the amplification cards
  list raw targets; joining to the registry gives them editorial
  context.

### Where the code lives

- `analysis/src/engine/bot_detector.py` (and related files in `engine/`).
- Registry-match signal should be applied late in the scoring pipeline,
  after behavioral scores compute, so the audit trail retains the raw
  behavioral numbers that *would have* flagged the account.

### Walkthrough

When picked up: walkthrough should cover the behavioral-raw-scores vs
final-adjusted scores split, audit record changes, and any schema
addition on `ai_outputs` (e.g. a `registry_match` field) if needed.

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

- `analysis/src/engine/propaganda_*.py`
- Prompt: `analysis/src/engine/prompts.py` (bump prompt version if the
  prompt changes — mandatory, see CLAUDE.md AI output contract).

### Walkthrough

When picked up: walkthrough must include A/B evaluation results against
the golden set — especially per-lean-bucket flag rates, to verify the
detector isn't just learning "mark right-leaning outlets as propaganda"
or vice versa. This is a tripwire for the project's audit-first
invariant.

---

## Ordering

- Do **ui-redesign-plan.md Phase 2** first (the registries need to exist
  before detectors can consume them).
- Then bot detector work (lower risk, higher immediate value — kills
  false positives on named officials).
- Propaganda work last, and only with measurable eval evidence.

---

## Revisit cadence

Re-read this doc at the end of the UI redesign (after Phase 8 lands).
By then the registries exist and the three-way framing makes the case
for feeding entity context into the detectors self-evident.

- 2026-04-21 — Doc created.
