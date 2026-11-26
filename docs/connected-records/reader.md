# Phase 3. Reader experience

## What a visitor should learn

Lead with one dated development and a short plain-English sentence explaining its consequence and institutional meaning. Every page teaches what a record is and why it matters without forcing a citizen through legislative jargon. On a vote, the first screen names the proposal, what a Yea vote operationally did (motion polarity and threshold), the outcome, and the next stage. On a linked statement and vote, the first screen shows the attributed quote beside the exact action and one clear guardrail stating what the vote can and cannot show. On a numeric outcome claim, the first screen shows the quoted claim beside the single-series measured change, the 12-month pre-period context, and what remains unknown.

## Three-layer progressive disclosure

Evidence Paths must stay engaging, scannable, and portfolio-grade for regular citizens while preserving full epistemic rigor. Do not dump digests, confidence bounds, match bases, null models, and sensitivity grids inline on every card. Organize every Evidence Path surface into three strict visual layers:

1. **Layer 1: Plain-English story and guardrail (always visible, scannable in 5 seconds).**
   - Show the dated event chain in plain speech (for example, "Apr 28, 2025: Statement -> Bill section -> Whole-bill vote").
   - Translate technical roles into human language with one short guardrail line. Instead of "Role: Package", show **Voted on whole bill (312 sections)** with *"Does not show a separate vote on this section alone."* Instead of "Tier 1 Measured Change", show **Agency-reported number; cause not established**.
   - Show **Insufficient evidence** as a calm, first-class card that teaches civics by naming the exact missing record (for example, *"The Senate passed S. 146 by unanimous consent, so no individual senator's vote was recorded"*). Never hide a reviewed insufficient-evidence assessment behind an empty-section rule.
2. **Layer 2: Interactive step-by-step exploration (on click or expand).**
   - Default to a **structured Evidence Path list or timeline** (`[1. Official's Words] -> [2. Exact Bill Text] -> [3. Amendment or Floor Vote] -> [4. Current Law or Delivery]`). Clicking any step opens the highlighted quotation or statutory passage in the side or bottom inspector and reaches the primary government source in one step.
   - Offer the **local neighborhood graph** as an optional interactive toggle ("Explore connections") for multi-hop discovery rather than forcing every reader through a node-link diagram.
   - Pair exact-name term popovers (already in R-68) with short contextual **"Why this step matters"** cues on legislative stages (for example, explaining that suspension of the rules requires two-thirds approval and bars floor amendments).
3. **Layer 3: Audit Drawer ("How we checked this", collapsed by default, one click away).**
   - Hold full provenance and statistical machinery here: exact record revisions, SHA-256 digests, match basis (`ExplicitBillNumber`, `LexicalMatch`, etc.), release number, `as_of_utc`, single-maintainer review and intra-rater reliability notes, sensitivity grid for baseline-dependent numbers, base rates and null-model comparisons, AI summary label details, correction history, and a plain **"Copy link and citation"** action.

## Proposed navigation

Do not reopen the shipped flat DR6 header (`Officials, Issues, Votes, Bills, Statements, Executive actions, Compare parties, Search`) without measured reader evidence. Keep DR6 as the mandatory control arm in any navigation test. If a persistent collapsible left rail is tested, compare branch-first, issue-first, and the hybrid structure below against DR6. Do not include **Coalitions** in navigation until a coalition insight passes every Plane 3 pattern gate in [pipeline rules](pipeline.md#rules-for-patterns).

| Hybrid rail parent (test arm) | Children or destination |
|---|---|
| Overview | Institutional milestones feed, latest substantive updates, your members (ZIP) |
| Issues | Recently active issues, issue timelines, all tracked issues |
| Claims and actions | Reviewed evidence paths, commitments and deadlines, latest evidence |
| People and comparisons | Your members, officials, chamber and party comparisons (no coalitions until validated) |
| Evidence library | Legislative (bills, votes, amendments as available). Executive (orders, proclamations, notices). Cross-branch statements and document changes. Add Judicial once case records exist. |
| Methods and coverage | How findings are checked, source coverage meter (R-56), definitions, site-wide correction log, and AI evaluation |

Render navigation in server HTML so it remains usable with JavaScript disabled. Give each parent a labeled icon and a separate chevron button when it has children, expose the active path matching the record's actual section (`aria-current="location"`), support keyboard focus, Enter, and Space, and move into a focus-managed drawer on mobile. [W3C disclosure navigation with parent links](https://www.w3.org/WAI/ARIA/apg/patterns/disclosure/examples/disclosure-navigation-hybrid/).

Retain the Mosaic v2 palette and typography. On mobile (360 px), order the plain-English answer and guardrail directly after the headline, followed by the interactive steps, and place the source and tally card after the explanation.

## What the mockups demonstrate

The existing [review-graph mockups](experiments/review-graph/reader.html) and [private review mockup](experiments/review-graph/review.html) exercised Mosaic v2 visual tokens, keyboard search, and map-vs-list rendering on local fixtures. **They are superseded as Phase 3 production references** because they omit statement-to-vote links, limit lines, motion polarity, and insufficient evidence, hide navigation under `<noscript>`, place the source card 1,050 px above the explanation on mobile, promote "Export to Obsidian", and say a whole-bill suspension vote "tells you where they stood".

**Required next mockup before any Phase 3 build:** Build one static three-state Evidence Path prototype using the three-layer progressive disclosure layout and server-rendered navigation:

| Prototype state | What it must demonstrate | Required guardrail |
|---|---|---|
| State A: Direct specific motion | Official's quote linked to a bill provision and a specific amendment or direct-motion vote | Shows exact motion question, motion polarity, and one-step source link. |
| State B: Whole-bill package or procedural vote | Official's quote linked to a provision beside a package passage or procedural vote (for example, IRA insulin budget waiver vs. passage, or S. 5 amendment vs. passage) | Shows bill scope, explicit plain-English limit line ("Voted on whole bill; does not show a separate vote on this section alone"), what Yea operationally did on procedural motions, and any direct amendment vote beside the package vote. |
| State C: Insufficient evidence | Official's quote or outcome claim where public records cannot establish the link (for example, unanimous consent with no individual cast, or victim-outcome claim with no outcome data) | First-class calm card naming the exact missing evidence, with zero private candidate lists shown. |

Verify the three-state prototype at 360 and 1280 px in light and dark themes, with JavaScript disabled (navigation and plain answer intact), with keyboard-only navigation, and with no console errors.

## What leads each page

| Page | Layer 1 first useful answer | Layer 2 and Layer 3 deeper detail |
|---|---|---|
| Legislative activity | What a proposal changes, what a Yea vote operationally did, and which step just occurred | Distinct stable bill and vote pages, passage method (roll call, voice vote, unanimous consent), amendment text, individual casts, and Audit Drawer digests. |
| Official and comparison | Dated statements beside relevant votes or edition-bound sponsorships, plus legislative stage reached and source coverage | Time-bounded term and party, complete eligible base, fixed "Not recorded as voting. The record does not say why" wording for absences, and Audit Drawer sources. |
| Issue | What changed across branches, what is known about implementation, and what is pending | Separate institutional events from measured outcomes (three tiers), with 12-month pre-trends and source coverage. |
| Executive action | Who must do what and by when | Directive text, statutory basis when identified, deadlines, and observed follow-up ("no report found as of" date when absent). |
| Map | Answer a geographic question such as how a reader's delegation split | Retain the full roll call and a non-map equivalent. On an issue with no geographic question, lead with the timeline. |
| Brief or document change | Which cited fact or passage changed since the last update | Exact old and new wording, date uncertainty, and underlying release records. |

## Replace the fixed featured vote

Today Home selects the newest published vote classified as Passage with a bill type and number ([HomeStore](../../src/CivicLens.PublicApi/Home/HomeStore.cs)).

Pilot a code-selected **Institutional milestones** feed (plus a clear "Find your members by ZIP" entry point at the top). Eligible events are deterministic Plane 1 milestones: floor passage votes, enacted laws, major executive directives, new verified outcome updates, and published corrections. Do **not** include Plane 2 human-reviewed connections in the Home feed until review queue ordering is a deterministic code rule, so solo-maintainer review capacity never acts as editorial selection. Print the one-sentence selection rule, ordering key, tie breaker, and as-of date directly on the page. Replay at least four weeks of frozen releases and check balance by **party, member, chamber, and topic** before launch.

## Decision test

Run two lightweight, distinct reader tests in sequence:

1. **Think-aloud on the three-state Evidence Path mockup (run before any Phase 3 UI build).** Recruit 6 first-time readers (no politics, journalism, or government background; e.g., library intercept or community college civics volunteers, ~$120 in gift cards). Test States A, B, and C plus one AI-labeled summary and one 360 px mobile check. Ask: *"Does this page show that Rep. X supports this provision?"* on State B, *"What is missing?"* on State C, *"Which part did a computer write?"*, and *"Open the original official source."* If 2 or more of 6 infer a provision position from a whole-bill or procedural vote, revise the Layer 1 guardrail wording and retest before shipping any public Evidence Path.
2. **First-click navigation test (run only when navigation redesign is scheduled).** Test 4 arms between subjects: **DR6 shipped header (control)**, branch-first, issue-first, and hybrid rail (~36 per arm, ~144 total, using static menu trees). Adopt a new rail only if it measurably beats the shipped DR6 header on first-click task success.

Do not run the earlier two-order (`reader-study.html`) study as prepared; no product decision depends on newest-first versus oldest-first ordering of two records without link states or guardrails.
