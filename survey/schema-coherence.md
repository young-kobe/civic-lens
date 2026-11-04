# Data plane schema coherence review

Question: "Is our data plane schema coherent and well designed for our use case?"

Short answer: yes for the core shape. Each kind follows one pattern: candidate, queue, run, draft, revision, decision, publication. Checks on closed sets and tick bounds are strong. Three gaps stand out. Links to officials, issues, and citations have four shapes. Configuration entities have no rows, so no foreign key or public name reaches them. Immutability is a code rule only.

Sources: the generated schema reference, the `public_api` migrations, `PublicApiGrants.cs`, and `docs/architecture/public-api-columns.txt`, on `origin/main`. A bare `name.md:line` cites `docs/architecture/schema/`. A bare `.cs` name cites the source file of that name.

## 1. Map

| Group | Owner feature | What it records |
|---|---|---|
| `collection_jobs`, `collection_job_attempts`, `collection_job_origins`, `collection_collector_slot`, `collection_source_schedules`, `collection_candidate_jobs`, `collection_admission_batches` | Collection | Durable jobs, leases, pacing, schedules, and admission of discovered links (collection.md:5-130) |
| `collection_attempts`, `collection_captures`, `collection_discoveries`, `collection_configuration_revisions` | Collection | Immutable HTTP observations, content hashes, collector output, and hashed configuration (collection.md:14-75) |
| `evidence_processing`, `worker_heartbeat` | Collection | Extraction and comparison stages for each attempt, and loop liveness (collection.md:132-157) |
| `document_extractions`, `document_comparisons` | Documents | Text versions of captures and byte-compared diffs (documents.md:5-26) |
| `bill_gates`, `bill_pages`, `bill_text_versions`, `bill_stream_*`, `bill_follow_on_jobs`, `action_candidates` | Bills | Bill relevance gate, page chains, text reads, and the action candidate (bills.md:5-88) |
| `vote_versions`, `vote_candidates`, `vote_member_lists`, `vote_member_list_entries` | Votes | Roll call versions and shared member lists (votes.md:55-122, 207-218) |
| `statement_candidates`, `statement_gate_labels` | Statements | Statement evidence and gate labels (statements.md:56-101) |
| `roster_*`, `executive_officials`, `executive_terms`, `official_photos`, `zcta_districts` | Roster, Districts | Snapshots of members, terms, photos, and ZCTA to district maps (roster.md:5-46, districts.md:5-11) |
| `<kind>_analysis`, `<kind>_analysis_runs`, `<kind>_analysis_budget`, `analysis_system_prompts`, `analysis_output_schemas`, `document_change_analysis_status` | Analysis | Model work queues, runs, token budgets, and verbatim prompt text (analysis.md:5-86) |
| `<kind>_drafts`, `<kind>_revisions` and child rows, `<kind>_review_decisions`, `<kind>_review_decision_resolutions` | Each kind, Review | Drafts, immutable revisions, and decisions bound to a revision, for seven kinds |
| `brief_check_flags`, `brief_schedule` | Briefs | Model check flags and the weekly schedule (briefs.md:62-70, 178-182) |
| `publication_releases`, `publication_record_states`, `published_records`, `published_entries` and its three child tables, `publication_withdrawals`, `publication_withdrawal_corrections` | Publication | Releases, release membership ranges, entry facts, and withdrawals (publication.md:5-127) |
| `reader_reports`, `reader_report_cursor` | Reports | Reader problem reports (reports.md:5-23) |
| `public_api.*` views | Publication | Active release, votes, bills, ZIP districts (public-api-columns.txt:3-69) |

## 2. Coherence checks

### Naming

- **Defect.** Tick columns have two forms. Most use `_utc_ticks`. The job tables use `created_at`, `retry_at`, `started_at`, `not_before`, and `lease_expires_at` (collection.md:59, 81, 99, 107). `TimestampTickChecks` bounds only the `_utc_ticks` suffix (TimestampTickChecks.cs:6-9), so these columns have no range check.
- **Gap.** Date columns use `_on` (`event_on`, `voted_on`, actions.md:60), `_date` (`change_date`, `statement_date`, review.md:89, statements.md:191), and `event_date` (publication.md:64). The bills view renames `event_on AS event_date` (PublicApiBillViews.cs:20).
- **Gap.** Record keys differ. Document changes use a 32 hex `draft_id` (review.md:9). Statements use `candidate_id` as the record key (statements.md:70). The others use a 64 hex `record_id`. `brief_revision_citations.document_change_draft_id` is varchar(64), but the target is varchar(32) (briefs.md:120).
- **Gap.** One column has several types. `source_id` and `attempt_id` are `text` in collection.md:20 and statements.md:61, and varchar(128) in votes.md:64. `collection_candidate_jobs.job_id` is varchar(32), and its target is varchar(128) (collection.md:44, 107). `issue_id` is 128 in votes.md:163 and 256 in publication.md:96. `person_id` is 60 in roster.md:24 and 128 in actions.md:125.
- **Defect.** Kind spellings differ. Publication uses `documentChange`, `action`, `vote` (publication.md:72-73). Brief citations use `DocumentChange` (briefs.md:123). Relationships use `Action`, `Vote` (relationships.md:21). Run reasons use camelCase in votes.md:45 and kebab case in statements.md:45.
- **Defect.** `document_change_analysis_status` holds the status of every task (analysis.md:75-82). The action, brief, and gate tasks write it (ActionDraftComposition.cs:23, Program.cs:319). `document_change_analysis_budget` has no `task` key, but the other six budgets do (analysis.md:45, votes.md:27).

### One concept, several shapes

- **Gap: officials.** Drafts hold `official_ids text[]` (votes.md:85). Document changes have a child table with `official_id` in the key (review.md:76-82). Statements call it a speaker (statements.md:176-182). Actions and votes hold `person_id` plus `bioguide_id` (actions.md:125, votes.md:109). Relationships hold one scalar `official_id` (relationships.md:106). Executive actions hold `person_id` (executive-actions.md:85). Publication keys on `position` and adds `is_matched` (publication.md:101-110). The views rename `person_id AS official_id` (PublicApiBillViews.cs:30).
- **Gap: issues.** Action and vote issue rows have `is_tracked` and no position cap (votes.md:159-165). Statement and document change rows have no `is_tracked` and cap position at 63 (statements.md:136-142, review.md:68-74). Those three key on `issue_id`. `published_entry_issues` keys on `position` (publication.md:94).
- **Gap: citations.** Some kinds use inline triples (votes.md:178, actions.md:143). Others use child tables (votes.md:148-157, review.md:55-66, actions.md:111-119). Only statements bind a citation to their own candidate evidence, by a composite foreign key (statements.md:165, 189). A vote or action citation can point at any extraction. Length caps differ: 8192 (review.md:62), 4096 (statements.md:169), none (votes.md:154).
- **Gap: policy areas.** Actions hold one scalar pair (actions.md:143). Statements have a child table (statements.md:153-159).
- **Gap: author.** `author_kind` allows `Code` on actions (actions.md:144) but not on votes (votes.md:179), yet vote `text_origin` allows `Code` (votes.md:196). `code_version` exists on actions and executive actions only (actions.md:150, executive-actions.md:87).

### Keys and foreign keys

- **Good.** Composite keys stop a revision from changing its bill or roll call (votes.md:176, actions.md:139). `published_entries` has a foreign key pair for each revision table (publication.md:54-68).
- **Gap.** `evidence_processing` has no foreign key on `job_id` or `attempt_id` (collection.md:134-137, PersistedEvidenceProcessingRowConfiguration.cs:40-41).
- **Gap.** `publication_record_states` has no foreign key to `published_entries` (publication.md:5-13). R-63 lists it as open (docs/roadmap.md:109).
- **Gap.** `reader_reports` has no foreign key to `published_records` (reports.md:14). That table now exists.
- **Gap.** `relationship_analysis.target_kind` and `target_record_id` have no foreign key (relationships.md:10). `relationship_revisions` already splits the target into typed pairs (relationships.md:99, 105).
- **Cannot exist today.** `issue_id`, `official_id`, `policy_area_id`, and `source_id` cannot have foreign keys, because the configuration lives in files (docs/architecture/configuration.md:159). `issue_revision_id` has no target table (votes.md:178, bills.md:32), but collection configuration revisions do have one (collection.md:65-69).
- **Gap.** `current_revision_number` on each draft has no foreign key to its revision (votes.md:85). It forms a cycle, so it needs a deferrable key.
- **Defect.** `vote_drafts.candidate_id` and `action_drafts.candidate_id` default to `''` and have a foreign key (votes.md:83-85, actions.md:58-60). The default can never be valid. `event_on '-infinity'` and `last_revised_at_utc_ticks 0` are backfill defaults that stayed.

### Immutability

- **Gap.** No table rejects an update or delete. The only triggers send notifications (statements.md:207, collection.md:161). The docs call it an application rule (docs/architecture/collection.md:493). Revisions, decisions, captures, attempts, and `published_entries` are immutable by code only.

### Checks on closed value sets

- **Good.** States, outcomes, kinds, review statuses, reason codes, and roles have checks in each kind.
- **Gap.** `vote_member_list_entries.vote_cast` is free text (votes.md:111). A Speaker vote holds candidate names (docs/architecture/publication.md:337), so a plain list is not possible. The counts depend on six literal strings in the view (PublicApiVoteFacts.cs:29-33).
- **Gap.** `evidence_processing.outcome` has no check (collection.md:137). `reader_reports.kind` and `category` have no check (reports.md:14). `roster_terms.state` has no check (roster.md:44).

### Nullable columns after backfills

- **Gap.** Each revision kind writes its columns as nullable and guards them with `num_nulls(...) in (0, N)`: votes.md:184, actions.md:152, statements.md:193, review.md:91, relationships.md:108, briefs.md:161, executive-actions.md:88. After each backfill, these columns must become NOT NULL in the JSON drop migration. The views filter the old rows out by hand (PublicApiVoteFacts.cs:43, PublicApiBillViews.cs:27). Those filters must go at the same time.
- **Gap.** `publication_releases.code_version` is nullable (publication.md:18). `executive_action_revisions.code_version` is nullable, but every author is `Code` (executive-actions.md:85-87).

### Indexes against the read paths

- **Good.** Each worker queue has a partial claim index that matches its claim order (collection.md:114, votes.md:22, collection.md:149).
- **Gap.** The ZIP view looks for the newest attempt of each source with no outcome filter (PublicApiZipDistricts.cs:22-27). The matching index is partial on `outcome = 'captured'` (collection.md:37). A capture hash exists only on captured rows (collection.md:25), so the view can add that filter with no change in meaning.
- **Gap.** No index leads with `official_id` or `issue_id` on the published child tables (publication.md:99, 109). The official and issue pages need that lookup.
- **Gap.** The vote list sorts the whole `public_api.votes` view, which counts members for each active vote (VoteStore.cs:15, PublicApiVoteFacts.cs:28-34). It cannot use an index. R-68 already plans a measurement.

### JSON columns

| Column | Holds | R-63 step |
|---|---|---|
| `revision_json` on seven revision tables (votes.md:178 and peers) | Own data | Vote pilot 4, then one PR for each kind |
| `collection_jobs.definition_json` (collection.md:107) | Own data. Operations query it with `->>` (docs/operations.md:139) | None |
| `collection_admission_batches.input_json`, `result_json` (collection.md:10) | Own replay data | None |
| `statement_gate_labels.label_json` (statements.md:94) | Own label, also from people | None |
| `statement_analysis_runs.quotes_json` (statements.md:38) | Own serialized quotes (StatementGateQueueMapping.cs:52) | None |
| `<kind>_analysis_runs.validation_json` | Own validation result | None. The rule is not clear |
| `document_change_review_idempotency.result_json` (review.md:52) | Replay cache | None |
| `collection_configuration_revisions.json`, `document_comparisons.result_json`, `output_json`, `context_json`, `user_message`, `collection_discoveries.*_json`, `profile_json` | Verbatim text | Keep |

### No reader, no writer, duplicated facts

- I did not scan readers column by column. That check stays open.
- **Defect.** `public_api.bills` counts `sponsor_count` from rows, but it reads `cosponsor_count` from a stored column (PublicApiBillViews.cs:20-24, actions.md:153). One view uses two rules for one kind of fact.
- **Gap.** These stored counts can drift from their rows: `vote_member_lists.member_count` (votes.md:119), `publication_releases.record_count` (publication.md:18), and the draft arrays `issue_ids`, `official_ids`, and `event_on` (votes.md:85), which `ListedDraftRows.Set` copies from the current revision (VoteDraftRows.cs:128).
- **Gap.** `collection_discoveries.source_id` repeats the source of its attempt (collection.md:75).
- **Good.** `published_entries` copies the headline and dates of a revision on purpose. It is an immutable snapshot of what a release added.

## 3. The public_api read model

The views are coherent for what they cover. Each joins `active_records`, so one rule selects the active release (PublicApiReleaseTime.cs:11-16). The role reads only views (PublicApiGrants.cs:47-49). Officials appear by configured ID, never by bioguide ID.

Three points break the pattern:

- `vote_issues` has no `position`, but `bill_issues` has one (public-api-columns.txt:8-11, 36-38).
- `zip_districts` does not follow the active release (docs/architecture/districts.md:7). This is a deliberate choice. A cached page can show other districts than the API after a new snapshot.
- `question` and `result` come from text prefixes of facts (PublicApiVoteFacts.cs:24-25), not from columns.

Missing for the pages:

| Page | Missing |
|---|---|
| Official | An officials view (name, office, terms, party, district, photo). A records view by official. Official vote rows with alignment. Today these exist only in release files (publication.md storage section) and in configuration |
| Home | A records list across all seven kinds, built on `published_entries` and its children. The latest brief. Withdrawals and corrections |
| Issue | Issue and policy area names. A records view by issue and policy area |
| Record | Views for statements, document changes, relationships, executive actions, and briefs |

The names of officials, issues, and policy areas are in configuration files only. A view cannot show them until a table holds them.

## 4. Recommended changes

Ranked by risk to data. "Before drops" means before the R-63 JSON drops. "Prune" means the later prune pass.

1. **Make the guarded columns NOT NULL and remove the view filters in each drop migration.** Small for each kind. Before drops, in the same migration.
2. **Add the foreign key from `publication_record_states` to `published_entries`.** Small. Before drops, already in R-63.
3. **Agree one shape for the official and issue link rows (D1).** Medium. Before drops, because the JSON is the only second copy to verify a reshape.
4. **Store public names in tables (D2).** Medium. Before the R-68 official page.
5. **Add `question` and `result` columns to vote revisions (D3).** Small. Before the vote JSON drop.
6. **Decide database immutability (D4).** Medium. Can wait for R-24.
7. **Add the missing foreign keys: `evidence_processing`, `reader_reports`, `relationship_analysis` targets.** Small. Prune.
8. **Fix the tick names and the analysis status and budget names (D5).** Medium. Prune.
9. **Fix the bad defaults, the type drift, and the missing checks on closed sets.** Small. Prune.
10. **Read `cosponsor_count` from one rule, and add the ZIP outcome filter and the official and issue indexes.** Small. With the R-68 measurement.
11. **Plan R-63 steps for `definition_json`, `label_json`, `quotes_json`, and admission JSON.** Medium. After the revision drops.

### D1. Shape of link rows

Problem: one official or issue link has four shapes, so a cross-kind query or view needs one branch for each kind.

- **Recommended: A.** One shape in each per-kind child table: key on position, unique on the value, `is_tracked` on every issue row, and one cap. Keeps foreign keys to each revision. Costs one migration for each kind.
- B. One shared link table with `kind` and `record_id`. Simple queries. Loses the foreign key to each revision table, or needs seven nullable pairs.
- C. Keep the shapes. No cost now. Each new view repeats the mapping.

Precedent checked: `published_entry_officials` and `published_entry_issues` (publication.md:92-110) already use shape A.

### D2. Public names for officials, issues, and policy areas

Problem: names exist only in configuration and release files, so R-68 cannot render the official, home, or issue page from the API.

- **Recommended: A.** Write release-bound catalog rows at publish time: officials, terms, issues, and policy areas, keyed by release. The API shows what the release showed. Publish writes more rows.
- B. Store configuration revisions as tables, as `collection_configuration_revisions` does. This also gives `issue_revision_id` a foreign key. It is not bound to a release, so the role would read operational configuration.
- C. Let the API read `officials.json`. Breaks the R-68 rule that no JSON file carries public data.

Precedent checked: `published_entries` (publication.md:51-90) and `collection_configuration_revisions` (collection.md:65-69).

### D3. Vote question and result

Problem: the view finds question and result by text prefix (PublicApiVoteFacts.cs:24-42), and a test keeps the SQL equal to `VoteFacts.Find`.

- **Recommended: A.** Code writes `question` and `result` columns on the revision, as it writes `event_on` and `cosponsor_count` for actions (actions.md:155). One owner, plain views. Needs a backfill.
- B. Keep the prefix parse and its test. No migration. Two owners of one rule.

Precedent checked: the derived action columns.

### D4. Database immutability

Problem: a code bug can update a revision, decision, capture, or published entry, and the database accepts it.

- **Recommended: A.** At R-24, run the app as a role that is not the owner, with no UPDATE or DELETE on immutable tables. No trigger is needed. It needs a migration role apart from the app role.
- B. A `BEFORE UPDATE OR DELETE` trigger that raises. Works now. It departs from the AGENTS.md trigger rule, which allows only wakeups and lock order.
- C. Keep the code rule and its tests. No cost. No database guarantee.

Precedent checked: the column grants of `civic_lens_public` (PublicApiGrants.cs:31-45).

### D5. Tick and status names

Problem: six job columns escape the tick range check, and the shared status table has a kind-specific name.

- **Recommended: A.** Rename to `_utc_ticks` and `analysis_status`, and add `task` to the document change budget, in one prune PR. The existing checks then cover the columns.
- B. Add explicit checks and keep the names. Less churn. Two naming rules stay.

Precedent checked: `TimestampTickChecks` (TimestampTickChecks.cs:6-9).
