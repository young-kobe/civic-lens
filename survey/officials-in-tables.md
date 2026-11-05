# Officials in tables survey

This survey plans how the people of `config/federal-coverage.json` move into database tables. It also covers the sources, the coverage, the pacing groups, and the document profiles. It changes no product code. The owner decides each item in [Decisions for the owner](#7-decisions-for-the-owner) before work starts.

## 0. Fixed owner decisions

These decisions are fixed. This survey does not reopen them.

1. The tracked members of Congress come from the roster. They are the people with a current term in the newest admitted member snapshot of `unitedstates/congress-legislators`.
2. People outside the roster, such as the President, the Vice President, and the justices, "all need to live in tables. if it is data and not purely configuration it should live in tables unless there is a functional reason otherwise".
3. Data is first class. Our own data goes in columns. JSON holds verbatim text only. No backward compatibility and no compatibility shims. Code that loses its last caller is deleted.

## 1. Measured facts and corrections to the brief

All counts come from `jq` and Python on `origin/main` at `b4084ff1`.

| Fact | Value |
|---|---|
| `config/federal-coverage.json` size | 814,052 bytes |
| Top level | `version` 1, `people` 542, `sources` 1,403, `pacingGroups` 2, `documentProfiles` 5 |
| People with `bioguideId` | 540 (539 current members and `jd-vance`) |
| People with `wikidataId` | 3 (`donald-trump`, `jd-vance`, `joe-biden`) |
| People with `offices` | 3. All 3 offices have the kind `executive`. |
| People with `website` | 0 |
| People with `withholdPhoto` | 0 |
| People with `names` | 0. No production code reads `Names` outside validation. |
| Justices in the file | 0 |
| Sources by mode | page 879, html 411, feed 104, api 9 |
| Sources with `officeOf` | 1,392. In each one it is the only covered person. |
| Coverage entries | 1,394. Each has `personId` and `startsOn`. `endsBefore` occurs 0 times. |
| `startsOn` values | 2026-10-10 (1,384), 2026-10-13 (8), 2026-10-29 (2) |
| `statementTextInFeed` set | 0 sources |

Corrections to the brief:

- The file has **nine** `api` sources, not eight. The ninth is `census-zcta-districts` (parser `censusZctaDistricts`).
- **Releases do not pin a roster snapshot ID today.** `PublicationOfficials.ReadRosterAsync` (`src/CivicLens.Application/Publication/PublicationOfficials.cs:19`) reads the newest snapshot at publish time. `publication_releases` has the columns `release_number`, `directory_name`, `actor_subject`, `published_at_utc_ticks`, `record_count`, `is_active`, and `code_version` only (`src/CivicLens.Infrastructure/Publication/Persistence/PublicationReleaseRow.cs`). The release contract `PublicationRelease` holds no roster ID. What a release keeps is a copy by value: `release_officials` and `release_official_terms` hold the joined names, websites, and terms of each release. This survey treats a release pin as new work (D6).
- Statement drafts, document-change drafts, and analysis runs do not store a revision ID. They find it through attempt, then job, then `configuration_revision_id` (`src/CivicLens.Infrastructure/Statements/AdmittingRevisionReader.cs:14-46`).

## 2. Inventory and classification

"Data" means a fact about the world or a per-person owner choice. It moves to tables. "Configuration" means a rule for how the system fetches or reads. It can stay in a file if a functional reason exists.

### 2.1 People (`PersonConfiguration`, `src/CivicLens.Application/Collection/PersonConfiguration.cs`)

| Field | Line | Today | Class | Future home |
|---|---|---|---|---|
| `id` | 7 | Slug, public page address | Data (identity) | `people.person_id`. Code mints it for a new member. |
| `name` | 8 | Display name | Data | Member: `roster_members.full_name`. Executive: `executive_officials.full_name`. Justice: owner table. |
| `names` | 9 | Dated aliases, unused | Data, dead | Delete. No reader. |
| `bioguideId` | 10 | Member key | Data (identity) | `people.bioguide_id` |
| `wikidataId` | 11 | Executive join key | Data (identity) | `people.wikidata_id` |
| `website` | 12 | Fallback URL, 0 set | Owner setting | `person_setting_revisions.website` |
| `offices` | 13 | Terms of executives | Data (facts) | Executive roster terms. Justices: owner office table. |
| `withholdPhoto` | 15-16 | Takedown flag, 0 set | Owner setting | `person_setting_revisions.withhold_photo` |

Office fields (`OfficeConfiguration.cs`): `title` (7), `startsOn` (8), `endsOn` (9), `party` (10), `kind` (11, `ConfiguredOfficeKind` executive or judicial). All are facts. The three shipped offices duplicate `executive_terms` rows that the executive roster already captures.

### 2.2 Sources (`WatchedSourceConfiguration`, `src/CivicLens.Application/Collection/WatchedSourceConfiguration.cs`)

| Field | Line | Class | Reason |
|---|---|---|---|
| `id` | 27 | Configuration | Stable key of a fetch rule |
| `coverage` | 28 | Data | Says who a source is monitored for. See 2.3. |
| `documentProfileId` | 29 | Configuration | Picks an extraction rule |
| `url`, `allowedOrigin`, `allowedPathPrefix` | 30-32 | Configuration, with a fact inside | The URL is a fact about an office. The origin and prefix are a fetch scope. |
| `mode`, `apiParser`, `credential` | 33-35 | Configuration | Selects code paths and secrets |
| `minimumCongress`, `backfillFrom` | 36-37 | Configuration | Collection window choices |
| `maxCandidates`, `admissionPolicy`, `jobPolicy` | 38-39, 51 | Configuration | Budgets |
| `statementTextInFeed` | 41 | Configuration | An owner check of feed shape |
| `officeOf` | 42 | Data | A fact: this source is the own office of one person. Code approval depends on it. |
| `enabled` | 43 | Configuration | Execution gate |
| `maxRequests`, `maxBytes`, `timeoutSeconds`, `minDelayMilliseconds`, `checkIntervalSeconds` | 44-48 | Configuration | Fetch limits |
| `etag`, `lastModified` | 49-50 | Configuration | Request validators |

### 2.3 Coverage (`SourceCoverageConfiguration.cs`)

| Field | Line | Class | Note |
|---|---|---|---|
| `personId` | 5 | Data | Equals `officeOf` on all 1,392 member sources. Redundant there. |
| `startsOn` | 6 | Our choice | Records when we began to monitor. Eligibility only. |
| `endsBefore` | 7 | Our choice | Never set. This is why a member who leaves stays covered. |

### 2.4 Pacing groups and document profiles

| Type | Fields | Class | Functional reason to stay a file |
|---|---|---|---|
| `PacingGroupConfiguration` (`src/CivicLens.Application/Collection/PacingGroupConfiguration.cs:9-10`) | `hostSuffix`, `minDelayMilliseconds` | Pure configuration | A politeness rule for our fetcher. It has no fact about the world. It changes with code review. The job pins it in the revision hash, and `pacing_key` on jobs derives from it. |
| `DocumentProfileConfiguration` (`src/CivicLens.Application/Documents/DocumentProfileConfiguration.cs:7-9`) | `id`, `selector`, `excludedSelectors` | Pure configuration | A CSS selector rule for the extractor. Extractions keep the exact profile revision. A selector is code-like and needs review beside parser changes. |
| `version` (`CollectionConfiguration.cs:17`) | 1 | Pure configuration | File format version |

## 3. Readers of each field

"Pinned" means the reader gets the value from a `CollectionConfigurationRevision` that a job, a schedule, or a candidate stores. "Live" means it reads the file that the process loaded at start (`src/CivicLens.Host/ConfigurationFile.cs:15-23`). All paths are under `src/`.

### 3.1 People readers

| Field | Reader | Use | Binding | Needs |
|---|---|---|---|---|
| `id`, `name` | `Application/Statements/StatementSpeakerRoster.cs:14-20` | Speakers of a source on a date: statement prompt, resolver, review, code approval | Pinned | Exact version: a later change must not change the speakers of an admitted article |
| `id`, `name` | `Application/Analysis/DocumentChangeDraftStep.cs:13,29` | Officials in the document-change prompt | Pinned | Exact version |
| `id`, `name`, `bioguideId` | `Application/Actions/ActionBillReader.cs:28-29,68-69,79-81,93-94` | Sponsor and cosponsor match, prompt names | Pinned (candidate revision) | Exact version |
| `id`, `name`, `bioguideId` | `Application/Votes/VoteReader.cs:81-84,98-99,115-124` | Member vote match, prompt names | Pinned (candidate revision) | Exact version |
| `id`, `name` | `Infrastructure/Review/ConfiguredPersonNames.cs:17-19` (from `PostgresActionReviewStore.cs:176`, `PostgresVoteReviewStore.cs:208`) | Names in action and vote review | Pinned | Exact version |
| `bioguideId` | `Application/Collection/ConfiguredCollectionSource.cs:113-114,213-219` | Bill cosponsor gate. Photo job batch, one job for each bioguide ID. | Pinned | Exact version |
| `bioguideId`, `id` | `Application/Collection/Processing/JobPreparation.cs:175,403,408` | Bill gate. Photo job to person. | Pinned | Exact version |
| `bioguideId` | `Infrastructure/Collection/Jobs/PostgresCollectionJobStore.BillFollowOns.cs:131` | Bill follow-on gate | Pinned | Exact version |
| `bioguideId` | `Application/Collection/CollectionConfiguration.cs:111` | Photo policy holds one job for each bioguide ID | Validation | Count at validation time |
| `offices` (title, dates) | `Application/ExecutiveActions/ExecutiveActionSigner.cs:21,112` | Finds the President in office on the signing day | Pinned (FR job, `JobPreparation.cs:308`) | Exact version, term history |
| `id`, `name`, `bioguideId`, `wikidataId`, `offices`, `website`, `withholdPhoto` | `Application/Publication/PublicationOfficials.cs:15-37,89-126` | `officials.json`, terms, website, photo read | Live | Current state, audit of what each release showed |
| `id`, `name` | `Host/Review/ReviewOfficials.cs:11-12`, `Host/Review/ReviewWorkspace.cs:256-259`, `Host/Publication/PublicationComposition.cs:52` | Review official list and catalog names | Live | Current state |
| `id`, `name` | `Host/Collection/SourceLabels.cs:18-19` | Source labels on the review home | Live | Current state |
| `id`, `name` | `Host/Relationships/RelationshipDraftComposition.cs:24`, `Host/Briefs/BriefDraftComposition.cs:24` | Names in relationship and brief prompts | Live (worker start) | Current state. No pin today. |
| `id`, `bioguideId`, `offices` | `Application/Roster/ProposeRosterChanges.cs:15-26`, `Host/Roster/RosterCommand.cs:39` | `roster propose` joins and leaves | Live (CLI) | Current state |
| `People` count | `Host/Program.cs:533` | `validate` prints the count | Live | None |

### 3.2 Source and coverage readers

| Field | Reader | Use | Binding |
|---|---|---|---|
| `coverage` | `Application/Collection/CollectionConfiguration.cs:136-150` (`IsActiveOn`, `CoveredPersonIdsOf`) | Eligibility and covered people | Pinned |
| `coverage` | `Application/Collection/Jobs/CollectionJobDefinition.cs:98` | Job needs active coverage on `coverageAsOf` | Pinned |
| `coverage` | `Infrastructure/Collection/Jobs/PostgresCollectionJobStore.cs:64` | Refuses to enqueue an inactive source | Pinned |
| `coverage` | `Infrastructure/Collection/Jobs/PostgresCollectionJobStore.Scheduling.cs:94` | Scheduler skips an inactive source | Pinned (schedule revision) |
| `coverage` | `CollectionConfiguration.cs:161` (`ListedPersonIdsOf`), `Host/Collection/SourceLabels.cs:18` | Source label names | Live |
| `officeOf` | `StatementSpeakerRoster.cs:20`, `Application/Statements/StatementSelectionResolver.cs:39`, `Application/Statements/StatementCodeApproval.cs:14,27-29` | Code approval: the office owner is the only speaker | Pinned |
| `officeOf` | `Host/Publication/PublishedAboutPage.razor:16` | Prose of the rule | None |
| `statementTextInFeed` | `ConfiguredCollectionSource.cs:109`, `JobPreparation.cs:88` | Feed items as statements | Pinned |
| `pacingGroups` | `CollectionJobDefinition.cs:37`, `Infrastructure/Collection/Jobs/JobRowWriter.cs:25`, `PostgresCollectionJobStore.cs:189,278,341,418` | Pacing key and gate | Pinned |
| `documentProfiles` | `Application/Collection/Processing/EvidenceProcessingWorker.cs:129-136`, `Application/Documents/ExtractDocument.cs:34-39` | Extraction profile | Pinned |
| fetch settings | `CollectionJobDefinition.cs:62-75`, `PostgresCollectionJobStore.Scheduling.cs:26-53,83-90` | Job request and schedules | Pinned |

### 3.3 Where revision IDs live today

`collection_configuration_revisions` holds each revision once, keyed by its SHA-256 ID (`Infrastructure/Collection/Jobs/Persistence/ConfigurationRevisionRow.cs:3`). `JobRowWriter.StoreAsync` (`JobRowWriter.cs:34-43`) inserts it with `ON CONFLICT DO NOTHING`. These tables reference it through `configuration_revision_id` with a restricting foreign key: `collection_jobs`, `collection_source_schedules`, `collection_admission_batches`, `action_candidates`, and `vote_candidates`.

## 4. Where each kind of data comes from after the change

| Data | Source after the change | Writer |
|---|---|---|
| Tracked members, names, terms, party, caucus, member URL | Newest member roster snapshot (`roster_members`, `roster_terms`) | Roster job, as today |
| Person ID of a member | New `people` row, minted when a snapshot first lists the bioguide ID | `PostgresMemberRosterStore.AdmitAsync`, in the snapshot transaction |
| President and Vice President, terms | Executive roster snapshot (`executive_officials`, `executive_terms`) | Executive roster job, as today |
| Which executives we track | D3 | D3 |
| Person ID of an executive | `people` row keyed by GovTrack ID or Wikidata ID (D2) | D3 |
| Justices, names, terms | Owner table now. A collected source later (D4). | Owner CLI |
| Website override, photo takedown | `person_setting_revisions` | Owner CLI |
| Press sources and `officeOf` | D5 | D5 |
| Pacing groups, document profiles, fetch settings | File, as today | Git |

### 4.1 Justices

The repo has no justice data and no justice source. The current file holds no justice. The network policy of this session refused `fjc.gov` and `supremecourt.gov`, so this section is an assumption that a person must check.

| Candidate | Content (assumed) | Terms (assumed, not verified) | Fit |
|---|---|---|---|
| Federal Judicial Center, Biographical Directory of Article III Federal Judges, CSV export | Every Article III judge with court, commission date, and termination date | A U.S. government work, so likely public domain | Best fit. One file, all justices, dated. Needs a new `api` parser. |
| Wikidata, property P39 (position held) | Justices with start and end qualifiers | CC0 | Good data. A SPARQL query is a new kind of source. Quality varies. |
| `supremecourt.gov` members page | Current justices only, HTML | Site terms not checked | Fragile HTML. Current only. |
| Oyez | Biographies | Believed to be noncommercial license | Do not use until terms are checked. |

Nine justices change rarely. A new collected source costs a parser, a source row, a store, and tests. Recommendation: an owner-edited office table now (D4). Plan the FJC source later and record it in the roadmap.

## 5. Proposed tables

All tables are insert-only, as `roster_snapshots` and the revision tables are. Columns hold our data. No JSON column.

| Table | Key | Columns | Precedent |
|---|---|---|---|
| `people` | `person_id` | `bioguide_id` (unique, null), `wikidata_id` (unique, null), `govtrack_id` (unique, null), `created_at`, `created_by_attempt_id` (null) or `created_by_actor` | The ID rules of `IssueConfiguration.ValidateId` and `ProposeRosterChanges.ProposeId` |
| `person_setting_revisions` | `person_id`, `revision_number` | `withhold_photo`, `website` (null), `note`, `actor_subject`, `recorded_at`, `registry_sequence` | `statement_revisions` numbering and `actor_subject` on `publication_releases` |
| `person_office_revisions` | `person_id`, `revision_number` | `actor_subject`, `recorded_at`, `note`, `registry_sequence` | Revision header with child rows, as `statement_revisions` |
| `person_office_revision_terms` | `person_id`, `revision_number`, `position` | `title`, `kind` (`executive` or `judicial`), `starts_on`, `ends_on` (null), `party` (null) | `roster_terms` columns and checks |
| `tracked_executives` (D3 option B only) | `person_id` | `tracked_from`, `tracked_before` (null), revision columns | None |

`registry_sequence` is one global sequence for every owner write. It gives one number that pins the whole owner state (D6).

Checks: `ck_*_dates` (end not before start), the kind set, the website rule of `PublishedWebsite` in C#, and the person ID rule in C#. The C# store validates before insert. The database checks repeat only the shape, as the roster tables do.

## 6. Topics

### 6.1 Per-person owner settings

Today a takedown is a file edit, a commit, and a restart of the worker and the review host (`docs/operations.md`, "Take down a member photo"). A worker with the old file writes the photo back at its next publish. A table removes that race: every publish reads the newest setting row.

Writer: new owner commands in Host, on the `OwnerCommand` precedent (`src/CivicLens.Host/OwnerCommand.cs:9`, which needs `CIVIC_LENS_DATABASE` and `CIVIC_LENS_REVIEW_OWNER`). `releases publish --withdraw` is the nearest precedent for an owner write with a note.

- `people photo withhold <person-id> --note <text>`
- `people photo restore <person-id> --note <text>`
- `people website set <person-id> <url> --note <text>` and `people website clear <person-id> --note <text>`
- `people office set <person-id> --file <offices.json>` for justices and others (D4). The file is input only. It is not kept.

Each command appends one revision row. It never updates a row. History is the row list. `actor_subject` and `note` give the audit trail. A shared Application handler owns validation, so the review UI can call it later.

`PublicationOfficials` reads the newest setting row of each named person in one query, as `ReadNewestPhotosAsync` reads photo rows. The photo jobs keep ignoring the flag (`roster.md` invariant), so a restore brings back the newest photo.

### 6.2 Press sources and coverage

All 1,392 member sources cover exactly their `officeOf` person. Their coverage list adds no information beyond `officeOf` and a start date. Term-aware coverage (6.4) makes the start date unneeded.

Options are in D5. A new member's press site is a manual step in every option, because discovery (R-27) does not exist. The roster gives the member URL of the last term (`roster_members.url`). Code can list each tracked member with no `officeOf` source. Recommendation: replace `roster propose` with `people gaps`, a read-only command that prints tracked members with no own-office source and their roster URL. The review home can show the same count. This flags the manual step without a file diff to copy.

### 6.3 Version binding

Today a job pins one hash ID that holds people, sources, and coverage. After the change, people state has three parts: the member snapshot, the executive snapshot, and the owner registry state. Readers that are pinned today (3.1) must stay pinned, because an admitted article must keep its speakers and a candidate must keep its tracked members.

Repo precedents checked:

- `collection_configuration_revisions`: hash ID, stored once, referenced by restricting foreign keys from jobs, schedules, batches, and candidates.
- `roster_snapshots`: an attempt ID names an immutable snapshot. `RosterSnapshotRules.ReadLatestSnapshotAttemptIdAsync` picks one.
- `publication_record_states`: ranges of release numbers instead of copies of each state.
- `RevisionBackfill` (`src/CivicLens.Infrastructure/Review/RevisionBackfill.cs:14`): the shared owner of backfill and verify for revision columns. It serves draft revisions, not configuration. It is the precedent for the one-time backfill of new pin columns.

Options are in D6. Recommendation: one new hash-identified row, `coverage_bindings`, with the configuration revision ID, the member snapshot attempt ID, the executive snapshot attempt ID, and the registry sequence. It reuses the shape and the writer of `collection_configuration_revisions`. Jobs, schedules, batches, and candidates keep one column, renamed to `coverage_binding_id`. Releases get the same column, so a release names the exact people state that it showed.

### 6.4 Term-aware coverage

Rule: a member is covered on day D when the pinned member snapshot has a term of the member that contains D. An executive is covered on day D when the pinned executive snapshot has a tracked term that contains D. A justice is covered by the owner office rows the same way. A press source is active on D when its `officeOf` person is covered on D.

Effects:

- A member who leaves is absent from the next snapshot. New admissions for their sources stop at the first job that pins that snapshot. Their admitted jobs still run from their pin.
- `CoveredBioguideIdsOf` for bills and votes becomes the members with a term on the bill or vote day. This answers the known gap "bill and vote coverage is not term aware".
- A member who leaves keeps their past records, because those candidates pin older snapshots. D-11 (past votes of a member added later) stays separate.

Assumption to check: when a member resigns mid-term, `legislators-current.json` drops them. It may never show the real end date, because the update moves the member to the historical file. Then the old snapshot still shows the scheduled end, such as 2027-01-03. Coverage then stops on the day of the first snapshot without the member, with up to one day of lag. A pin to an old snapshot can still say "covered" for a day after the resignation. D7 covers this.

### 6.5 Migration of the current file data, once

1. A migration creates the tables. A one-shot owner command, `people import <config.json>`, inserts the `people` rows: 540 by bioguide ID, plus the three executives with their Wikidata IDs. It keeps every current person ID, because published pages use them as addresses. It inserts no setting rows, because 0 people set `website` or `withholdPhoto`. It inserts no office rows, because the executive roster already holds the three executive offices. Precedent: `releases first-published backfill`, a one-shot command over existing data.
2. Stored configuration revisions hold `people` in hashed JSON. Owner decision 3 forbids a frozen parser. D8 covers the rewrite.
3. The next PR deletes `people import`, because it loses its purpose after the one run.

Deleted after the change (each in the PR that removes its last caller):

| Item | File |
|---|---|
| `PersonConfiguration`, `OfficeConfiguration`, `ConfiguredOfficeKind`, `DatedNameConfiguration` | `src/CivicLens.Application/Collection/` |
| `CollectionConfiguration.People` and about 61 lines of person validation (`CollectionConfiguration.cs:40-56,333-378`), `CoveredBioguidePeopleOf`, `BioguidePersonIds` | `CollectionConfiguration.cs` |
| `coverage` on member sources, `SourceCoverageConfiguration` if D5 option A or B | `SourceCoverageConfiguration.cs` |
| `ProposeRosterChanges`, `RosterProposal`, `RosterCommand`, and their tests | `src/CivicLens.Application/Roster/`, `src/CivicLens.Host/Roster/RosterCommand.cs` |
| `ConfiguredPersonNames` (replaced by a pinned name reader) | `src/CivicLens.Infrastructure/Review/ConfiguredPersonNames.cs` |
| `ReviewOfficials` reading the file | `src/CivicLens.Host/Review/ReviewOfficials.cs` |
| The `people` array and coverage lists | `config/federal-coverage.json`, `config/federal-pilot.json`, `config/example.json` |
| The People section, invariants on bioguide, Wikidata, website, offices | `docs/architecture/configuration.md` |
| The Proposals section and the propose invariants | `docs/architecture/roster.md` |
| "Take down a member photo" file steps | `docs/operations.md` |
| `roster propose` sentences | `README.md` |
| Diagram label "Coverage configuration" if the definition changes | `tools/render-architecture.py` |

The file shrinks from 814 KB. People are about 542 short objects. Coverage lists are 1,394 entries. Most of the size is source fetch settings, so the file stays large if sources stay (D5).

## 7. Decisions for the owner

### D1. Who mints the person ID of a new member

Problem: the person ID is the public page address. Today a person types it. With the roster as the source, code must mint it.

- A. The roster store mints it in the snapshot transaction, with the current `ProposeId` rule: slug of the official name, with the bioguide ID as a suffix on a clash. Tradeoff: no manual step. A name change later does not change the ID, which is right for an address.
- B. The ID is the bioguide ID. Tradeoff: stable and simple, but changes all 540 public addresses and is not readable.
- C. Code proposes, the owner confirms with a command. Tradeoff: a manual step for each new member. It keeps the gap "a new member has no name on the site" open until a person acts.

Recommendation: A. It follows the rule that the store that writes the triggering row creates the dependent rows in one transaction, as photo jobs do today.

### D2. Identity key of a person

Problem: members join by bioguide ID, executives by Wikidata ID. The executive file gives a GovTrack ID to all 80 people, a Wikidata ID to 8, and a bioguide ID to 67 (`roster.md`). Justices have neither.

- A. `people` holds nullable `bioguide_id`, `wikidata_id`, and `govtrack_id`, each unique. Join order stays bioguide, then Wikidata. Tradeoff: three columns, but each is a real external key.
- B. A separate `person_identifiers` table (person, scheme, value). Tradeoff: open-ended, but a generic table with one reader per scheme. AGENTS.md warns against generic structures without a need.

Recommendation: A. Add GovTrack because the executive table is keyed by it.

### D3. Which executives are tracked

Problem: the file tracks the President, the Vice President, and the former President Biden. Biden is there so that `ExecutiveActionSigner` can name him on older Federal Register documents.

- A. Rule in code: track every person with a `prez` or `viceprez` term that overlaps the collection window, which starts at the earliest `backfillFrom` of the Federal Register source. Tradeoff: no owner table. The window is configuration that already exists.
- B. An owner table `tracked_executives` with dates. Tradeoff: explicit, one more command.
- C. Track every executive in the snapshot. Tradeoff: 80 people, most of them dead. Officials pages list only people that a record names, so the cost is low, but prompts and the signer see noise.

Recommendation: A. It matches decision 1: the roster says who holds an office, and our window says how far back we look. `ExecutiveActionSigner` then reads `executive_terms` with `prez` instead of a configured title string "President".

### D4. Justices

Problem: no collected source exists, and the brief asks for justices in tables.

- A. Owner office table (`person_office_revisions` with terms of kind `judicial`), written by `people office set`. Tradeoff: manual, but nine rows that change rarely.
- B. A new FJC `api` source now. Tradeoff: a parser, a store, and tests for nine people. Terms of use not yet checked.
- C. Wikidata SPARQL. Tradeoff: a new source kind and query maintenance.

Recommendation: A now, B later after a person checks the FJC terms. The office table also serves any other official outside both rosters. That replaces `offices` in the file.

### D5. Press sources and coverage

Problem: the owner rule puts data in tables. A source mixes fetch configuration with one fact, `officeOf`.

- A. Keep sources in the file. Drop `coverage` from sources that set `officeOf`. Coverage then derives from `officeOf` and terms (6.4). Keep `coverage` only for shared sources, such as the White House feeds. Tradeoff: smallest change. The fact `officeOf` stays in Git with review and history.
- B. Move sources and coverage to tables now, with revision rows and an owner command to add a source. Tradeoff: most consistent with the owner rule, but it needs a source editor, a revision mechanism for 1,400 rows, and a schedule sync from tables. R-27 needs it anyway.
- C. Split: an `official_sources` table with `source_id`, `person_id`, and URL as facts, and the file keeps fetch settings keyed by `source_id`. Tradeoff: two owners for one source, which breaks "one owner for each rule".

Functional reason for A: fetch settings are code-like rules that jobs pin by hash. No runtime writer exists until R-27. The shared feeds need coverage that is our choice, not a fact.

Recommendation: A in this work, B with R-27. A new member's press source stays a manual file edit, flagged by `people gaps` (6.2).

### D6. Version binding for table data

Problem: pinned readers need the exact people state of their job. Releases pin nothing today.

- A. One hash-identified `coverage_bindings` row: configuration revision, member snapshot, executive snapshot, registry sequence. Jobs and candidates reference it with one restricting foreign key. Releases reference it too. Tradeoff: one more table, one join. It reuses the `collection_configuration_revisions` writer shape.
- B. Four pin columns on each pinning table. Tradeoff: no new table, but five tables gain four columns, and every reader must carry four values.
- C. No pin. Read "as of" the admission time: the newest snapshot and registry rows at or before it. Tradeoff: no columns, but a late snapshot admission or a clock skew can change the answer. It breaks the exact pin rule.

Recommendation: A. The candidate and job readers then load one binding and build one `RegistryView` (people, terms, settings) once per operation.

### D7. Resignation before the term end

Problem: the current file may drop a member without a real end date (assumption in 6.4).

- A. Accept up to one day of lag. Coverage ends at the first snapshot that lacks the member. Tradeoff: simple. A record within a day after leaving can still name them.
- B. Also capture `legislators-historical.json` to get real end dates. Tradeoff: real dates, but a much larger file over the 10 MB read limit (assumption), and the roster doc puts it out of scope.
- C. Owner sets an end date with a setting row. Tradeoff: manual and rare.

Recommendation: A, with C as the fix for a known case. Measure one real departure before B.

### D8. Stored configuration revisions that hold `people`

Problem: `collection_configuration_revisions` rows are strict hashed JSON with `people`. When `PersonConfiguration` is deleted, those rows no longer parse. Owner decision 3 forbids a compatibility parser. The retired parser `senateVoteList` is the current precedent of keeping old shapes, and decision 3 retires that approach.

- A. One migration rewrites every stored revision: remove `people` and member coverage, compute the new hash, insert the new row, repoint the five referencing tables, create a `coverage_bindings` row from the newest snapshot at the job time, and delete the old row. Tradeoff: one heavy migration, but no shim remains.
- B. Drain first: stop the worker, let jobs finish, cancel the rest, then rewrite only rows that candidates still reference. Tradeoff: same rewrite, smaller.
- C. Keep old rows and a frozen reader. Tradeoff: forbidden by decision 3.

Recommendation: A, run after B's drain so the rewrite touches no running job. Also delete the `senateVoteList` retired parser path in the same PR if its last stored job is rewritten. Ask the owner whether that is in scope.

### D9. Names for prompts on live paths

Problem: relationship and brief prompts read names from the worker's start file, with no pin.

- A. Read the newest registry view once per draft and store the binding ID on the draft row. Tradeoff: a new column on two draft tables.
- B. Read the newest view with no pin. Tradeoff: same as today, unpinned.

Recommendation: B in this work, because it keeps today's behavior. Record A as planned work. The drafts cite approved records that already carry their own names.

## 8. PR plan

Each PR runs the locked restore, the Release build, the tests, `dotnet format --verify-no-changes`, and the diagram check. Each updates the feature docs in place.

1. **Tables and import.** Migration for `people`, `person_setting_revisions`, `person_office_revisions`, `person_office_revision_terms`, and the registry sequence. Store and `people import`. Tests: import keeps every ID; a second import adds nothing; a duplicate bioguide is refused; rows are insert-only. Deletes nothing yet.
2. **Owner settings.** `people photo withhold|restore`, `people website set|clear`. `PublicationOfficials` reads settings from the table. Tests: withhold drops the photo and restore brings back the newest; history keeps every row; a non-owner is refused; an invalid URL is refused. Deletes `website` and `withholdPhoto` from `PersonConfiguration`, their validation and tests, and the file steps in `operations.md`.
3. **Minting members.** `PostgresMemberRosterStore.AdmitAsync` inserts a `people` row for each new bioguide ID in the snapshot transaction (D1). Tests: a new member gets an ID in the same commit; a failed insert leaves no snapshot; a clash gets the suffix. Deletes `people import`.
4. **Bindings.** `coverage_bindings` table and writer. Jobs, schedules, batches, candidates, and releases reference it (D6). Rewrite of stored revisions (D8). Tests: two equal bindings share one row; a referenced binding cannot be deleted; the rewrite repoints every row and leaves no old revision; a release names its binding.
5. **Pinned readers.** `StatementSpeakerRoster`, `ActionBillReader`, `VoteReader`, `ExecutiveActionSigner`, photo batch, bill gate, and review names read the bound registry view. Term-aware coverage (6.4). Tests: a member who left is not covered after the snapshot that drops them; an admitted article keeps its speakers after a later snapshot; the signer finds the President from `executive_terms`; the photo batch holds one job per tracked member. Deletes `ConfiguredPersonNames`, `CoveredBioguidePeopleOf`, `BioguidePersonIds`.
6. **Live readers.** `PublicationOfficials`, `ReviewOfficials`, `SourceLabels`, relationship and brief compositions read the newest registry view once per operation. Tests: one query per publish (measured with a command counter, as the photo read test does); review lists new members without a file change. Deletes `CollectionConfiguration.People`, `PersonConfiguration`, `OfficeConfiguration`, `ConfiguredOfficeKind`, `DatedNameConfiguration`, the `people` arrays, and the person validation and its tests.
7. **Coverage from `officeOf`.** Drop coverage lists from own-office sources (D5 A). Tests: an own-office source with coverage is refused; the White House feeds keep explicit coverage; eligibility follows terms. Deletes the 1,392 coverage entries.
8. **Justices and gaps.** `people office set` (D4). `people gaps`. Tests: offices place a justice in the Justices tab; a person with roster terms ignores owner offices; gaps lists a tracked member with no own-office source. Deletes `ProposeRosterChanges`, `RosterProposal`, `RosterCommand`, and the Proposals docs.

PRs 4 and 5 can merge as one if the rewrite cannot leave the readers half moved. Each PR before PR 6 keeps the build green with both paths, but no PR keeps an old path past the PR that moves its last reader.

## 9. Risks and open questions

### Measured

- 1,392 of 1,403 sources set `officeOf`, each equal to the only coverage person.
- No coverage entry sets `endsBefore`.
- 0 people set `website`, `withholdPhoto`, or `names`.
- Releases pin no roster snapshot. They copy officials and terms into `release_officials` and `release_official_terms`.
- Five tables reference `configuration_revision_id` with a restricting foreign key.
- Statement and document-change drafts reach their revision through the job of the attempt.

### Assumptions to check

- `legislators-current.json` drops a resigned member without a real end date.
- The historical legislators file is over the 10 MB capture read limit.
- The FJC export is public domain and stable. Not fetched: the session network refused `fjc.gov`.
- The rewrite of stored revisions (D8) fits in one migration window on the production database. The revision count is not measured.

### Risks

- **Address stability.** A minted ID that clashes with a retired ID could reuse a page address. Mitigation: `people` rows are never deleted, so the clash rule sees retired IDs too.
- **A roster outage.** An empty or failed snapshot must not read as "everyone left". The roster readers already return the newest admitted snapshot. Keep the existing `ProposeRosterChanges` test intent as a coverage test: no snapshot means no change.
- **Code approval.** `officeOf` stays the only proof of own office. Term-aware coverage must not let a source whose person left still code-approve. Protect it with a test in PR 5.
- **Mixed jobs during rollout.** Pacing already tolerates mixed revisions for a short time (`collection.md`). The D8 drain avoids mixed bindings.
- **Photo policy size.** `CollectionConfiguration.cs:111` checks the photo policy against the configured bioguide count. With minted members, the count is not known at validation time. The check must move to the roster job, or the policy must cap jobs per batch.

### Open questions

- Should the review UI write owner settings too, or only the CLI for now?
- Should the White House feeds move to coverage by executive term, so a new President is covered with no file edit?
- Is the retired `senateVoteList` parser in scope for deletion with D8?
