# Candidate features

**Status.** Candidates. None is accepted or implemented. Each one waits for an investigation of its data and its risk. Remove a feature when its investigation shows that it will not work, and record the reason in the feature reference that owns the data. Move an accepted feature into the [work queue](../evidence-paths.md#work-queue).

The list comes from the former product direction and the adversarial plan review of 2026-11-20 (PR #634 comments). Every feature follows the [pipeline rules](pipeline.md#rules-for-every-connection): it shows what the records say and never states a conclusion.

Size: S is days, M is about one to two weeks, L is longer.

## How a connection is shown

| Feature | Reader question | Data | Main risk | Size | Gate |
|---|---|---|---|---|---|
| Limit line on each quote and vote pair | Does this vote show what they think about this part? | Vote roles from [claims](claims.md#how-to-add-an-issue), the roll-call question and kind | A long line in a row. Test the wording with readers. | S | Vote-role labels |
| Insufficient evidence outcome | Is this link unproven, or wrong? | Relationship decisions, a new reason code and a candidate digest | Reviewers overuse it to avoid a call | S | [D-19](../evidence-paths.md#pending-decisions) |
| Provision span on a relationship | Which words of the bill does this quote refer to? | Bill text extractions cited by action revisions | A span on an old edition after a code-approved update | M | [D-20](../evidence-paths.md#pending-decisions) |
| Basis label on each link | Is this a documented citation or a text match? | The candidate basis in the [connection model](pipeline.md#connection-model) | Readers read "documented" as "supports the claim" | S | Pilot |
| Sensitivity grid for numeric claims | Does the result depend on the dates chosen? | Dataset cells and the claim dates | A large grid confuses readers. Show the result and the range first. | S to M | Single-series outcome check |
| Bounds beside small counts | How sure is 4 of 4? | Existing counts | Readers read a bound as a forecast | S | None |
| Risk and coverage chart on the methods page | How often does Civic Lens decline to link, and how often is a link wrong? | Pilot outcomes for each case | Readers read a small sample as a guarantee | S | Pilot result |
| Replay status on each finding | Can someone else check this number? | The input bundle manifest and expected outputs | The status goes stale when a source changes its bytes | S | Input bundle |

## Reader tools

| Feature | Reader question | Data | Main risk | Size | Gate |
|---|---|---|---|---|---|
| Site-wide correction log | What did Civic Lens get wrong, and when did it fix it? | Per-record corrections ship today | A withdrawn record needs a neutral entry that shows none of its content | S | None |
| New since your last visit, for issues and followed members | What changed since I last looked? | Release first-published dates. The ZIP member lookup already shows newly published votes. | Count a republication once. Return-visit features are parked during R-68. | S to M | R-68 closure |
| Shareable evidence card | How do I show someone this exact record? | Release, record revision and sources | An image cannot be withdrawn. Prefer a stable link with a preview that the server can update. | M | Withdrawal tests for each surface |
| Atom feeds by member or issue | Tell me when my member votes on this issue. | Published votes, issues, statements and release dates | A delivered item cannot be recalled. Put only the title and link in an item. No email. | M | Withdrawal tests for each surface |
| Promise and record page | Did my member do what they said? | The member's dated commitment and later casts on the specific motion | Highest attribution risk. A sparse page implies silence. | L | Pilot publishes reviewed links for several members, R-48 |
| Ask about this record | What does this mean for me? | Published text only, with citations | AI text beyond the evidence, and cost per question | L | Do not build in this plan |
| Local impact by ZIP | What did this law do in my town? | One traced award so far | False local credit to a named official | L | Second recipient trial passes |

## New evidence sources

Each source follows R-58: an official API or bulk file, with a terms and robots check first.

| Feature | Reader question | Data | Main risk | Size |
|---|---|---|---|---|
| Did the funded thing happen? | Was money obligated, spent, and delivered at a site? | USAspending awards and transactions, recipient or agency completion records, site inventories ([Deerfield trial](experiments/evidence-trials/results.md#3-from-a-charging-grant-to-an-operating-site)) | A wrong award-to-site match. Use a similar-name control. | M |
| What the bill does, beside what its sponsors said | Does the text match the description? | Edition-bound provision text and the member's own quotation | Paraphrase judgment. Needs a person and the edition date. | M |
| Directives with deadlines | Did a directed task get done? | Federal Register text, agency progress reports, follow-up orders | A missing report is not a failure. Show "no report found as of" a date. | M |
| Cost estimates | What does it cost? | CBO cost estimates | An estimate covers one edition. Bind the edition. | S |
| Independent reviews | What did independent reviewers find about this program? | GAO, inspector general and CRS reports | Report scope can differ from the claim | S |
| Agency performance data | Did the outcome move? | Agency dashboards and workbooks | Definitions change. Bind each definition version. | M |
| Lobbying disclosures | Who lobbied on this bill? | Senate LDA filings | Reads as collusion. Show no link to a member, and wait for a wording rule. | M |

## Derived insights

| Feature | Reader question | Data | Main risk | Size |
|---|---|---|---|---|
| Changed record | Did an office delete a press release, rewrite a quotation, or remove a position? | Deletion detection and the document-change gate (R-13) | A page move looks like a deletion | M |
| Shared language | Which offices use the same sentences, and who used them first? | Statement text. Planned as R-38. | Shared boilerplate looks like coordination. A person reviews each finding. | M |
| Unusual activity on an issue | Which issue saw unusual legislative activity this week? | Distinct substantive decisions by week and issue ([graph research](graph.md#which-ai-or-statistical-study-comes-next)) | Collection changes and backfills cause false alerts | M |
| Typical wait for a bill stage | How long has a comparable bill usually waited? | Bill actions by chamber, Congress and stage | Too few events. Show counts below 10 events. | M |
| Votes that split officials on an issue | Which votes explain a split? | Dated casts in one chamber | Reads as an ideology score. Show the motions. | M |
| Recurring cosponsor groups | Who keeps backing bills together on this issue? | Sponsorships by edition and date | Reads as a hidden bloc. Must pass the [pattern rules](pipeline.md#rules-for-patterns) with a party null. No "coalition" label before then. | M |
