# Reusing classified evidence across features

**Design proposal.** A new reader question about facts we already publish should usually require one bounded `LATERAL` read query and a presentation change. For example, an official's profile starts from a member's recorded casts and filters by issues the reader follows. An issue page starts from the same issue assignments and follows the same casts back to officials. Both return the identical vote question, motion polarity, cast, revision, and citation. Changing the classifier or adding another issue leaves both readers usable.

## What already works and where coupling remains

Editors maintain issue IDs, definitions, policy areas, and examples in `config/issues.json`. For each published release, PostgreSQL stores names and definitions for the issue IDs present on its records in `release_issues`, with examples in child tables. The public views `bill_issues` and `vote_issues` associate tracked issue IDs with evidence revisions. The existing issue reader queries typed published votes, bills, and sponsors in one release snapshot. Existing relationship records serve a narrower purpose: `RelationshipPairFinder` requires an explicit bill number (or sponsor + bill title), which cannot yet link statements that discuss a policy mechanism without citing a bill number or link a non-sponsor's remarks to a bill. A survey question has no legislator cast of its own and needs its own producer when selected. [Issue catalog](../../config/issues.json), [issue reader](../../src/CivicLens.PublicApi/Issues/IssueStore.cs), [pair finder](../../src/CivicLens.Application/Relationships/RelationshipPairFinder.cs), and [current relationship kinds](../../src/CivicLens.Core/Relationships/RelationshipRecordKind.cs).

The trial scripts deliberately pin examples. Keep case IDs, expected answers, model names, and retrieval settings in experiments and evaluation inputs. Production consumers request facts by issue, official, predicate, and date. [Vote pair study](experiments/compare-senate-votes.py) and [retrieval trial](experiments/evidence-trials/text-trials.py).

## Can a versioned table replace the repeated definitions?

Yes, technically, **but this migration is dropped from the active plan.**

A [read-only PostgreSQL measurement](experiments/measure-issue-definition-reuse.sql) (run against operational `public.*` tables on November 19, 2026) found 192 `release_issues` rows across 12 releases (1424 through 1435, spanning roughly one day of publishes) representing 16 identical public definitions. That measures repetition across frequent publishes, not a storage or query latency bottleneck. The existing `release_issues` table is simple, already binds `(release_number, issue_id)` to its counts and wording, and protects release history. A migration would touch `release_issues`, `release_issue_includes`, `release_issue_excludes`, two party tables with foreign keys, and `PublicApiGrants` with no user-facing gain.

The only concrete defect identified in the analysis was that an older issue ID missing from `config/issues.json` can appear with its ID as its name and no definition. Fix that root cause directly in `PublicationCatalog` instead of introducing a content-addressed definition migration. Keep the [versioned-table SQLite check](experiments/versioned-issue-definitions.sql) and measurement as archived reference only; reopen only if full-database table size or publish write latency later proves a real bottleneck.

## Proposed interface between producers and readers

All four [pipeline data planes](pipeline.md#four-data-planes) share one **Evidence Reference** contract: `(RecordKind, RecordId, RevisionNumber, OptionalSpanOrCell, AsOfUtc)`.

| Responsibility | What crosses the boundary | Who owns its meaning |
|---|---|---|
| Evidence identity | Record kind, stable ID, exact revision, optional span or dataset cell, `as_of_utc`, and release identity. | The source's feature owns parsing, identifiers, and validation. A missing date stays unknown. |
| Issue assignment | Evidence reference, issue ID, catalog rubric version, relevance grade, reason, and cited passage. An item may carry several issues or none. | The classifier proposes assignments. Existing source-validation and review rules govern eligibility. Reclassifying against a revised rubric creates a new result. |
| Typed relationship | A named, directed fact with permitted endpoint kinds, exact endpoint revisions, `TargetRole`, `MotionPolarity` (for votes), `MatchBasis`, and supporting evidence. | Plane 1 documentary links are code-approved after citation checks. Plane 2 judgment links require solo-maintainer approval under D-19 and D-20. Give every new predicate a definition and eligible evidence before exposing it. |
| Public consumption | Publication chooses eligible revisions and enforces `WithdrawalCascade` plus the release-independent monotonic withdrawal tombstone ledger. `PublicApi` composes typed `public_api` views via correlated `LATERAL` queries (or optional `ReleaseIngest` ranged rows if latency requires them). `PublicWeb` renders three-layer progressive disclosure. | Publication owns release membership, corrections, and withdrawals. `PublicApi` owns read queries. `PublicWeb` owns presentation. [Publication](../architecture/publication.md) and [public site contracts](../architecture/public-site.md#storage-and-contracts). |

**Recommendation for the first pilot.** Reuse feature-owned tables, typed keys, and `public_api` read views with the shared Evidence Reference convention. Do not put every relation in one generic triple table, because it weakens foreign keys and domain-specific checks. Do not classify on demand per page. Extend `RelationshipRevision` privately under D-19 and D-20 first without changing `Publication.Contracts` until the pilot passes.

Additive fields allow older consumers to keep reading supported facts. A consumer that does not understand a new predicate omits its interpretation or shows an explicit unavailable state. Changing predicate meaning or a field's unit requires a new version. Every derived finding follows one unified dependency and withdrawal rule: approval requires current citations, publication verifies them against the release, `WithdrawalCascade` refuses withdrawal of a cited record while an active finding cites it, and the monotonic withdrawal tombstone ledger prevents rollbacks or cache warms from resurrecting withdrawn records. [Delivery trial](delivery.md#acceptance-trial).

## How much work should an extension take?

- **Another issue on existing evidence.** Add a versioned definition and positive and negative examples in `config/issues.json`, then evaluate on unseen families. Audit candidate coverage (policy-area gate baseline vs. BM25). Avoid issue-name switches in reviewers, APIs, or pages. [Bill issue choice](../../src/CivicLens.Application/Issues/BillIssueChoice.cs).
- **Another feature on existing facts.** Add a bounded `LATERAL` query, response mapping, and UI. Deduplicate on the identity of the thing being counted (one cast can match several followed issues). Authorization, eligibility, withdrawal tombstones, evidence checks, and classification stay with their existing owners.
- **Another evidence type or kind of inference.** Add ingestion, version binding, validation, evaluation, and publication mapping once. A poll needs exact wording, answers, population, geography, field dates, release date, sample, and survey-method disclosures; a national poll cannot establish district opinion. [Polling disclosure standards](https://aapor.org/standards-and-ethics/disclosure-standards/).

Keep evidence relevance, classifier confidence, reader-selected interests, issue salience in surveys, and survey answer distributions distinct. Explicit follows may reorder a private view while public factual results stay identical for all readers. Keep reader preferences out of shared release rows and public caches.

## Features built from those relations

| Reader question | Plane | Joins that supply the answer | Additional evidence or check |
|---|---|---|---|
| What has this official actually done on my issues? | P1 | Followed issue -> classified bill or vote -> exact cast or edition-bound sponsorship -> legislative stage reached and base rate | Keep sponsors, casts (with motion polarity), and executive signatures distinct. Show source coverage (R-56). |
| Why is no individual vote recorded on this bill? | P1 | Bill action -> passage method (roll call, voice vote, unanimous consent) | First-class Plane 1 transparency feature (R-69) with zero manual review bottleneck. |
| Which officials acted on the same problem in different ways? | P2 | Issue + affected group -> provisions grouped by mechanism -> sponsors and member casts | A tax credit and a grant have different beneficiaries and legal effects. Bounded solo-maintainer review. |
| Which promise is coming due, and what has been delivered? | P1/P2 | Quoted commitment -> responsible office -> due date -> implementation event -> reported measure | Preserve deadline anchor and each stage. Show "no report found as of" date when absent. |
| What changed for people eligible for this benefit? | P2 | Population + eligibility condition -> old and new provision revisions -> adoption or effective date | Compare actual qualifications across editions; renumbered clauses can survive and unchanged numbers can change subject. |
| Do several public claims rely on independent evidence? | P2 | Claim revisions -> cited observations -> originating source | Collapse citations repeating one agency release; label agency self-reports clearly. |
| How does a decision compare with opinion on this issue? | P2 | Issue -> poll question and distribution joined via reviewed comparison to action and vote | Preserve exact population, timing, and motion polarity; show context only when directional match fails. |

Plane 1 action, vote, passage-method, edition-bound sponsorship, and source-coverage features scale immediately across Congress without manual review. Plane 2 provision-level classification, mechanism links, commitment tracking, and outcome audits stay bounded to solo-maintainer review capacity.

## Offline interface check

The [SQLite example](experiments/reuse-issue-facts.sql) builds one activity view from three House roll calls and six casts by Reps. Thomas Massie and Alma Adams, plus one CBS/YouGov poll question. The six hand-typed casts must still be verified against Clerk XML in setup experiment 6 ([pipeline setup](pipeline.md#before-the-pilot-opens-its-holdout)) before reusing this fixture in any UI or automated test.

Run it with `sqlite3` or Python's built-in `sqlite3` module:

```sh
python3 -c "import sqlite3, pathlib; c = sqlite3.connect(':memory:'); c.executescript(pathlib.Path('docs/evidence-paths/experiments/reuse-issue-facts.sql').read_text())"
```

This small check verifies query reuse on fixed inputs. Classifier accuracy, `LATERAL` production query plans, and monotonic withdrawal-tombstone lifecycle behavior belong to the setup experiments and private pilot.
