# Survey: move the other five revision kinds to columns

This survey is read-only research. It changes no source, test, or doc file. It plans one PR for each of five revision kinds, in the shape of the vote pilot (R-63).

Kinds: statement, relationship, brief, executive action, document change draft.

Facts here come from the code on `main` at commit `ecd4eac`. Line numbers come from that commit. Check them before you start a PR.

## 1. What the vote pilot does

The pilot is the model for all five PRs. I checked these files: `docs/architecture/votes.md` (Storage and contracts, Limits), `docs/architecture/schema/votes.md`, `src/CivicLens.Infrastructure/Votes/VoteRevisionRows.cs`, `VoteRevisionPage.cs`, `VoteMemberLists.cs`, `PostgresVoteRevisionBackfillStore.cs`, `VoteRevisionComparison.cs`, and `Votes/Persistence/*Configuration.cs`.

### Keys and foreign keys

- The revision table keeps its key `(record_id, revision_number)`. It keeps `author_subject`, `author_kind`, `run_id`, `revision_json`, and `created_at_utc_ticks`.
- Each child table has the key `(record_id, revision_number, <natural key or "position">)`. Facts use `position`. Issues use `issue_id` and also store `position`.
- Each child table has a foreign key `(record_id, revision_number) -> vote_revisions` with `on delete restrict`.
- Each source span is three columns: `<name>_extraction_id`, `<name>_start`, `<name>_length`. `SourceSpanColumns.Configure` owns them. It adds a foreign key to `document_extractions`, an index on the extraction column, and the check `start >= 0 AND length > 0`. An optional span has all three columns null or all three set.
- The roll identity is a composite foreign key to a unique key on the draft table (`ux_vote_drafts_record_roll`). The draft owns the identity. The revision repeats it, and the database proves they agree.
- Ordered member lists that many revisions share live in `vote_member_lists`. A list ID is a SHA-256 over its ordered content. This is for large lists only.

### Checks

- Enum columns use `EnumChecks.InList` over the enum names. They are varchar with a `HasConversion<string>`.
- Text uses a length check from a constant. Examples: `ck_vote_revisions_headline` and `ck_vote_revision_facts_text`. Headline is `varchar(256)`. Summary is `text` plus a check. The constants are `VoteDraftRevision.Maximum*Length`. `ReviewTextColumns.Configure` takes the two constants as arguments.
- Hash IDs have a regex check, for example `issue_revision_id ~ '^[0-9a-f]{64}$'`.
- Pairs that are null together have an equality check: `(bill_type IS NULL) = (bill_number IS NULL)`.
- `ck_vote_revisions_columns_written` uses `num_nulls(...) IN (0, N)`. It makes a legacy row (all null) or a complete row (none null). The new columns stay nullable until the drop step.

### How NewRevision writes the rows

`VoteRevisionRows.Create` builds the row, serializes the JSON, and calls `FillColumns`. `FillColumns` sets every column and builds the child row lists (`row.Facts`, `row.Issues`). EF saves the parent and children in one `SaveChanges`. The JSON stays as a copy until step 4.

### How readers rebuild a revision

- `VoteRevisionRows.ReadPageAsync` reads the parent rows. Then it reads each child table once, with `JOIN unnest(ids, numbers)` on the exact keys of the parent rows. The page costs a fixed number of queries.
- `Rebuild` builds the record from the row and the child rows. It orders children by `position`.
- A reader fails on a row with null columns. `NotBackfilled` is the error. Only the backfill fixes such a row.
- `ReviewRecords` has a `readRevisions` hook (`ReviewRecords.cs:18`, used at `:431`). A kind with columns passes its own reader. Every other kind uses `ReadJsonRevisionsAsync` (`:433`). `Resolve` (`:437`) still calls `ValidateShape` on each read. Keep this.
- `CitedRevisions.ReadRevisionJsonAsync` (`CitedRevisions.cs:28`) throws for the vote kind. It must throw for each kind that moves.

### Backfill and verify

`PostgresVoteRevisionBackfillStore` takes the review lock. For each legacy row it writes the columns in a savepoint, reads the row back, rebuilds the revision, and compares it to the stored JSON with `RevisionJsonComparison` (top-level keys only, no values). It skips a row it cannot prove. `VerifyAsync` re-checks every moved row.

### The four steps

1. Migration, with write-both.
2. Readers read the columns.
3. Backfill and verify in production.
4. Drop the JSON. Step 4 needs a change of `readonly_access.revisions` (`deploy/scripts/readonly-grants.sql:75`) first.

Each PR in this survey is steps 1 to 3 in code. Step 4 is a later change for each kind, after the owner runs the backfill in production.

### Shared owners that exist today

- `SourceSpanColumns` (Infrastructure/Documents/Persistence) for spans.
- `ReviewTextColumns` and `ReviewTextRows` (Review) for headline, summary, origin, and suggestion. They require `text_origin` and the suggestion columns. See question Q5.
- `EnumChecks`, `TimestampTickChecks`, `RevisionJsonComparison`, `RevisionBackfillReasons`, `IRevisionBackfillStore`.
- The vote-specific parts (`VoteRevisionRows`, `VoteRevisionPage`, the backfill store) are not shared.

### Bounds precedent

`git grep -n Maximum src/CivicLens.Core src/CivicLens.Application` shows the rule. A `public const int Maximum...` on the revision record owns each bound. The validator, the resolver, the prompt, and the EF configuration all read the constant. Examples: `ActionDraftRevision.MaximumSummaryLength`, `VoteDraftRevision.MaximumFactLength`, `VoteDraftRevision.MaximumBillTypeLength`. A column that has no constant today needs one before its check is written. I list each case below.

## 2. Findings that apply to all five kinds

1. JSON field names are camelCase (`JsonSerializerDefaults.Web`, `ReviewJson.cs:8`). Run the first SQL measure of each kind to see how enums are stored (string name or number).
2. Every reader goes through a small set of shared paths. The generic reader is `ReviewRecords.ReadJsonRevisionsAsync` (`ReviewRecords.cs:433`). The queue search reads the headline from JSON (`PostgresReviewQueueStore.cs:167`, and `ApprovedHeadlineMatches` at `:61`). `CitedRevisions.ReadStoredRevisionsAsync` (`CitedRevisions.cs:39-45`) reads `revision_json` for any kind. `readonly-grants.sql:75-76` reads it for every `_revisions` table.
3. Cross-kind readers create a dependency order. A brief reads statement, action, and document change JSON (`BriefRecordReader.cs:60`). A relationship reads statement and action JSON (`RelationshipCitedReader.cs:45-56`, `:101`; `RelationshipEvidenceReader.cs:22,43,74`). When the action PR lands, `ReadRevisionJsonAsync` will refuse the action kind, so those readers must already use the columns.
4. Tests insert `revision_json` only. Examples: `BriefWorldSeeder.cs:71-135`, `RelationshipWorldSeeder.cs:25-74`, `PostgresReviewQueueTests.cs`, `PostgresFederalRegisterTests.cs:100-123`. After step 2 a reader fails on a row with null columns. Each PR must move its test seeders to rows with columns. Recommendation: a seeder helper that builds the row with the production `NewRevision`.
5. Drafts keep denormalized copies of revision fields (`ListedDraftRows.Set`: `event_on`, `issue_ids`, `official_ids`). Leave them. They are indexes on the draft, not revision data.
6. The read-only role cannot select `revision_json` from the table. For the measures below, use the owner role. Or replace `FROM <table>` with `FROM readonly_access.revisions('<table>')` and replace `revision_json::jsonb` with `revision`.
7. `length()` in Postgres counts characters. `string.Length` in C# counts UTF-16 units. A text with astral characters can differ. Treat a measure within 2 percent of a bound as at the bound.

## 3. Statement revisions

Sources: `src/CivicLens.Core/Statements/StatementDraftRevision.cs`, `docs/architecture/schema/statements.md` (table `statement_revisions`).

Current table: `statement_revisions` has `candidate_id varchar(64)`, `revision_number`, `author_subject varchar(256)`, `author_kind varchar(16)`, `run_id varchar(32) null`, `revision_json text`, `created_at_utc_ticks bigint`. Key `(candidate_id, revision_number)`. Foreign keys to `statement_analysis_runs` and `statement_drafts`.

### 3.1 JSON fields

| JSON key | C# type | Null | Verbatim source text? | Note |
|---|---|---|---|---|
| candidateId | string | no | no | same as the key column. 64 hex |
| revisionNumber | int | no | no | key column |
| authorSubject | string | no | no | column exists |
| createdAtUtc | DateTimeOffset | no | no | column exists as ticks |
| headline | string | no | no | model or person text. Max 256 |
| summary | string | no | no | model or person text. Max 8000 |
| statementDate | DateOnly? | yes | no | null together with the evidence |
| statementDateEvidence | SourceSpan? | yes | span only | extraction ID, start, length |
| speakerOfficialIds | ImmutableArray<string> | no, may be empty | no | configuration IDs. Max 64. Element length not bounded |
| otherSpeakers | ImmutableArray<string> | no | speaker names from the source | plain names, max 16, each max 128, distinct |
| issueIds | ImmutableArray<string> | no | no | configuration IDs. Max 64. Element length not bounded |
| policyAreaIds | ImmutableArray<string> | no | no | configuration IDs. Max 64. Element length not bounded |
| quotations | ImmutableArray<StatementQuotation> | no | the span points to the verbatim text | each has a SourceSpan and a kind (`DirectQuotation`, `ReleaseText`). Max 32. Span length max 4096 |
| billNumbers | ImmutableArray<string> | no | text found in the article | `ValidateShape` does not check count or length |

The quotation text is not in the revision. Only the span is. The verbatim text stays in `document_extractions`. So no text needs to stay as text in this table.

### 3.2 Proposed columns and tables

On `statement_revisions` (all new columns nullable until the drop step):

- `headline varchar(256)`, `summary text`.
- `statement_date date null`, and the evidence span `statement_date_evidence_extraction_id/_start/_length`. Use `SourceSpanColumns.Configure(..., optional: true)`. Add `ck_statement_revisions_date_evidence: (statement_date IS NULL) = (statement_date_evidence_extraction_id IS NULL)`.
- A `columns_written` check over the core columns: `headline`, `summary`. The date columns are legitimately null, so they are not in it. Use the shape of `ck_vote_revisions_columns_written`.
- Checks: `length(btrim(headline)) BETWEEN 1 AND 256`, `length(summary) BETWEEN 1 AND 8000`.

Child tables, each with `fk (candidate_id, revision_number) -> statement_revisions on delete restrict`:

| Table | Key | Other columns | Checks |
|---|---|---|---|
| `statement_revision_speakers` | `(candidate_id, revision_number, official_id)` | `position smallint` | `length(btrim(official_id)) > 0`, `position >= 0` |
| `statement_revision_other_speakers` | `(candidate_id, revision_number, position)` | `speaker_name varchar(128)` | `length(speaker_name) BETWEEN 1 AND 128`, no NUL, `speaker_name = btrim(speaker_name)`. Unique `(candidate_id, revision_number, speaker_name)` |
| `statement_revision_issues` | `(candidate_id, revision_number, issue_id)` | `position smallint` | same as `vote_revision_issues` |
| `statement_revision_policy_areas` | `(candidate_id, revision_number, policy_area_id)` | `position smallint` | same |
| `statement_revision_quotations` | `(candidate_id, revision_number, position)` | span (`quotation_extraction_id`, `_start`, `_length`), `kind varchar(16)` | span check, `length <= 4096`, `kind IN ('DirectQuotation','ReleaseText')`. Unique `(candidate_id, revision_number, quotation_extraction_id, quotation_start, quotation_length)` |
| `statement_revision_bill_numbers` | `(candidate_id, revision_number, position)` | `bill_number varchar(N)` | length check after the measure |

Foreign keys on spans go to `document_extractions`. The existing `ValidateAgainst` rule says each span must cite the extraction of the candidate. A composite foreign key to `statement_candidates (candidate_id, extraction_id)` could enforce it. See question Q8.

### 3.3 Bounds

| Column | Constant today? |
|---|---|
| headline 256 | yes, `StatementDraftRevision.MaximumHeadlineLength` |
| summary 8000 | yes, `MaximumSummaryLength` |
| selections count 64 | yes, `MaximumSelections` |
| other speakers count 16, length 128 | yes, `MaximumOtherSpeakers`, `MaximumOtherSpeakerLength` |
| quotations count 32, span length 4096 | yes, `MaximumQuotations`, `MaximumQuotationLength` |
| speaker, issue, policy area ID length | no. Vote pilot uses `varchar(128)` and the `btrim > 0` check. `DocumentChangeDraftRevision.ValidateSelections` uses the literal 128. Add one shared constant (see Q4) |
| bill number count and length | no. Add constants after the measure. `ValidateShape` must check them |

### 3.4 Readers and writers of the JSON

Writers:
- `src/CivicLens.Infrastructure/Statements/StatementDraftRows.cs:51-58` (`NewRevision`). Callers: `StatementDraftRows.cs:23` (code-approved append), `PostgresStatementReviewStore.cs:59` (`db.Add`), `PostgresStatementReviewStore.cs:75` (person save).

Readers:

| Place | What it reads | PR must move it? |
|---|---|---|
| `Review/ReviewRecords.cs:433-436` | whole revision, via `Records` in `PostgresStatementReviewStore.cs:24` | yes. Pass a `readRevisions` hook |
| `Statements/StatementBillMentions.cs:21,25` | SQL: `statementDate`, `billNumbers` (`jsonb_array_elements_text`) | yes. Query the date column and the bill number table |
| `Relationships/RelationshipEvidenceReader.cs:38` | SQL: `statementDate` range | yes |
| `Relationships/RelationshipEvidenceReader.cs:74-76` | whole revision | yes |
| `Relationships/RelationshipCitedReader.cs:45-56` | headline only, from `ReadStoredRevisionsAsync` | yes. Read the `headline` column |
| `Relationships/RelationshipCitedReader.cs:101` (`ReadAsync<StatementDraftRevision>`) | whole revision via `ReadRevisionJsonAsync` | yes |
| `Briefs/BriefRecordReader.cs:60` | whole revision via `ReadRevisionJsonAsync` | yes |
| `Review/PostgresReviewQueueStore.cs:61,167` | SQL: headline of every kind | yes for this kind. Set `HeadlineInColumn: true` at `:27-28` |
| `deploy/scripts/readonly-grants.sql:75-76` | function for every `_revisions` table | later, step 4 |
| Tests: `PostgresStatementDraftStoreTests.cs:63,332,441`, `PostgresReviewQueueTests.cs:202-261`, `ReviewHeldReasonMigrationTests.cs:37`, `RelationshipWorldSeeder.cs:25-27`, `BriefWorldSeeder.cs:71` | JSON insert and select | yes |

### 3.5 SQL measures (run on production, read only)

```sql
SELECT revision_json::jsonb -> 'quotations' -> 0 ->> 'kind' AS kind_sample, count(*) FROM statement_revisions GROUP BY 1
SELECT count(*) FROM statement_revisions
SELECT max(length(revision_json::jsonb ->> 'headline')) FROM statement_revisions
SELECT min(length(btrim(revision_json::jsonb ->> 'headline'))) FROM statement_revisions
SELECT max(length(revision_json::jsonb ->> 'summary')) FROM statement_revisions
SELECT count(*) FROM statement_revisions WHERE revision_json::jsonb ->> 'headline' IS NULL OR revision_json::jsonb ->> 'summary' IS NULL
SELECT count(*) FROM statement_revisions WHERE revision_json::jsonb ->> 'statementDate' IS NULL
SELECT count(*) FROM statement_revisions WHERE (revision_json::jsonb ->> 'statementDate' IS NULL) <> (revision_json::jsonb -> 'statementDateEvidence' IS NULL OR jsonb_typeof(revision_json::jsonb -> 'statementDateEvidence') = 'null')
SELECT max(jsonb_array_length(revision_json::jsonb -> 'speakerOfficialIds')) FROM statement_revisions
SELECT max(jsonb_array_length(revision_json::jsonb -> 'otherSpeakers')) FROM statement_revisions
SELECT max(jsonb_array_length(revision_json::jsonb -> 'issueIds')) FROM statement_revisions
SELECT max(jsonb_array_length(revision_json::jsonb -> 'policyAreaIds')) FROM statement_revisions
SELECT max(jsonb_array_length(revision_json::jsonb -> 'quotations')) FROM statement_revisions
SELECT max(jsonb_array_length(revision_json::jsonb -> 'billNumbers')) FROM statement_revisions
SELECT count(*) FROM statement_revisions WHERE jsonb_array_length(revision_json::jsonb -> 'speakerOfficialIds') = 0
SELECT max(length(e)) FROM statement_revisions r, jsonb_array_elements_text(r.revision_json::jsonb -> 'speakerOfficialIds') e
SELECT max(length(e)) FROM statement_revisions r, jsonb_array_elements_text(r.revision_json::jsonb -> 'otherSpeakers') e
SELECT max(length(e)) FROM statement_revisions r, jsonb_array_elements_text(r.revision_json::jsonb -> 'issueIds') e
SELECT max(length(e)) FROM statement_revisions r, jsonb_array_elements_text(r.revision_json::jsonb -> 'policyAreaIds') e
SELECT max(length(e)) FROM statement_revisions r, jsonb_array_elements_text(r.revision_json::jsonb -> 'billNumbers') e
SELECT count(*) FROM statement_revisions r, jsonb_array_elements_text(r.revision_json::jsonb -> 'otherSpeakers') e WHERE e <> btrim(e) OR e = '' OR position(chr(1) in e) > 0
SELECT max((q -> 'span' ->> 'length')::int) FROM statement_revisions r, jsonb_array_elements(r.revision_json::jsonb -> 'quotations') q
SELECT count(*) FROM statement_revisions r, jsonb_array_elements(r.revision_json::jsonb -> 'quotations') q WHERE (q -> 'span' ->> 'start')::int < 0 OR (q -> 'span' ->> 'length')::int <= 0
SELECT count(*) FROM statement_revisions r, jsonb_array_elements(r.revision_json::jsonb -> 'quotations') q WHERE NOT EXISTS (SELECT 1 FROM document_extractions x WHERE x.extraction_id = q -> 'span' ->> 'extractionId')
SELECT count(*) FROM statement_revisions r WHERE r.revision_json::jsonb -> 'statementDateEvidence' ->> 'extractionId' IS NOT NULL AND NOT EXISTS (SELECT 1 FROM document_extractions x WHERE x.extraction_id = r.revision_json::jsonb -> 'statementDateEvidence' ->> 'extractionId')
SELECT count(*) FROM statement_revisions r WHERE jsonb_array_length(r.revision_json::jsonb -> 'quotations') <> (SELECT count(DISTINCT (q -> 'span' ->> 'extractionId', q -> 'span' ->> 'start', q -> 'span' ->> 'length')) FROM jsonb_array_elements(r.revision_json::jsonb -> 'quotations') q)
SELECT count(*) FROM statement_revisions r JOIN statement_candidates c ON c.candidate_id = r.candidate_id, jsonb_array_elements(r.revision_json::jsonb -> 'quotations') q WHERE q -> 'span' ->> 'extractionId' <> c.extraction_id
SELECT count(*) FROM statement_revisions WHERE revision_json::jsonb ->> 'candidateId' <> candidate_id OR (revision_json::jsonb ->> 'revisionNumber')::int <> revision_number
SELECT count(*) FROM statement_revisions WHERE revision_json::jsonb ->> 'authorSubject' <> author_subject
SELECT count(*) FROM statement_revisions r, jsonb_array_elements_text(r.revision_json::jsonb -> 'issueIds') e GROUP BY r.candidate_id, r.revision_number, e HAVING count(*) > 1
```

The last measure and the quotation duplicate measure find rows that would break a unique key. A non-zero result means the backfill needs a rule.

## 4. Relationship revisions

Sources: `src/CivicLens.Core/Relationships/RelationshipRevision.cs`, `RelationshipCitation.cs`, `RelationshipMatch.cs`, `docs/architecture/schema/relationships.md`.

Current table: `relationship_revisions` has the same columns as the statement table with `record_id`. Foreign keys to `relationship_analysis_runs` and `relationship_drafts`. The draft already has `statement_candidate_id`, `target_kind`, `target_record_id` (seen in `RelationshipStaleness.cs:42-44`).

### 4.1 JSON fields

| JSON key | C# type | Null | Verbatim source text? | Note |
|---|---|---|---|---|
| recordId | string | no | no | 64 hex. Key column |
| revisionNumber | int | no | no | key |
| authorSubject | string | no | no | column exists |
| createdAtUtc | DateTimeOffset | no | no | column exists |
| statement | RelationshipCitation | no | no | kind must be `Statement`, record ID (64 hex), revision number >= 1 |
| target | RelationshipCitation | no | no | kind must be `Action` or `Vote`, record ID, revision number |
| match | RelationshipMatch | no | span only | `basis` (`BillNumber`, `BillTitle`) and a span |
| type | RelationshipType | no | no | four values. Must fit the target kind (`RelationshipTypes.For`) |
| reason | string | no | no | model or person text. One line. Max 400 |
| statementQuote | SourceSpan | no | span only | must cover `match.span`. Length max 4096 (`StatementDraftRevision.MaximumQuotationLength`) |
| targetFactId | string | no | no | max 16. Names one fact of the target |
| officialId | string | no | no | configuration ID. Max 256 |

No field is verbatim text. Spans point into extractions.

### 4.2 Proposed columns

All on `relationship_revisions`; no child table is needed. Every list in this kind is one item.

- Statement citation: `statement_candidate_id varchar(64)`, `statement_revision_number int`. Composite foreign key to `statement_revisions (candidate_id, revision_number)`. This is a real link (R-63). It depends on the statement PR only for the reader, not for the key, because the key exists today.
- Target citation: `target_kind varchar(16)` (`Action` or `Vote`), `target_record_id varchar(64)`, `target_revision_number int`. For the foreign key see Q1. Recommendation is two nullable composite foreign keys, `target_action_*` to `action_revisions (record_id, revision_number)` and `target_vote_*` to `vote_revisions`, with one check that exactly one is set and that it matches `target_kind`.
- Match: `match_basis varchar(16)` with `EnumChecks.InList`, and span `match_extraction_id/_start/_length` (`SourceSpanColumns`, not optional).
- Quote: span `statement_quote_extraction_id/_start/_length` (not optional). Check `statement_quote_length <= 4096`. Check that the quote covers the match: `statement_quote_extraction_id = match_extraction_id AND statement_quote_start <= match_start AND match_start + match_length <= statement_quote_start + statement_quote_length`. This is a plain row check, so the database can hold it. `SourceSpan.Covers` is the C# owner.
- `type varchar(24)` with `EnumChecks.InList`. A check can bind type to target kind: `(target_kind = 'Action') = (type IN ('DescribesOwnBill','StatesPositionOn','ClaimsCreditFor'))` and the vote rule. Read `RelationshipTypes.For` first to get the exact map.
- `reason varchar(400)` or `text`, check `length(btrim(reason)) BETWEEN 1 AND 400` and `reason !~ '[\r\n]'`.
- `target_fact_id varchar(16)`, check `length(btrim(target_fact_id)) BETWEEN 1 AND 16`. See Q2 about a foreign key.
- `official_id varchar(256)`, check `length(btrim(official_id)) > 0`. Note that other kinds use 128 for official IDs.
- Check that the statement and target are 64 hex.
- `columns_written` check over all new required columns.

### 4.3 Bounds

| Column | Constant? |
|---|---|
| reason 400 | yes, `MaximumReasonLength` |
| target fact ID 16 | yes, `MaximumFactIdLength` |
| official ID 256 | yes, `MaximumOfficialIdLength`. Look wide against 128 elsewhere (measure it) |
| quote length 4096 | constant lives on `StatementDraftRevision` (`MaximumQuotationLength`). Reuse it. Do not copy it |
| target kind, type, basis | enum names |

### 4.4 Readers and writers

Writers:
- `Relationships/RelationshipDraftRows.cs:53-60` (`NewRevision`). Callers: `RelationshipDraftRows.cs:28` (analysis append), `PostgresRelationshipReviewStore.cs:50` (person save).

Readers:

| Place | What it reads | PR must move it? |
|---|---|---|
| `Review/ReviewRecords.cs:433` | whole revision via `Records` (`RelationshipDraftRows.cs:14`, `PostgresRelationshipReviewStore.cs:19`) | yes |
| `Relationships/RelationshipDraftRows.cs:43` (`ReadCurrentAsync`) | whole revision | yes |
| `Relationships/RelationshipStaleness.cs:35` | SQL: `target -> revisionNumber` | yes. Read `target_revision_number` |
| `Publication/PostgresPublicationStore.cs:305` (`ReadAddedRelationshipCitationsAsync`) | whole revision, to list cited records | yes. Read the citation columns only |
| `Review/PostgresReviewQueueStore.cs:35` | queue headline. The relationship kind has no headline (`HeadlineProperty` null) | no |
| `Review/CitedRevisions.cs:35,45` | generic. Relationships are not cited by other kinds today | refuse the kind after the move |
| Tests: `PostgresRelationshipDraftStoreTests.cs:581`, `RelationshipWorldSeeder.cs:74` | JSON | yes |

### 4.5 SQL measures

```sql
SELECT count(*) FROM relationship_revisions
SELECT revision_json::jsonb -> 'statement' ->> 'kind' AS statement_kind, revision_json::jsonb -> 'target' ->> 'kind' AS target_kind, revision_json::jsonb ->> 'type' AS type, revision_json::jsonb -> 'match' ->> 'basis' AS basis, count(*) FROM relationship_revisions GROUP BY 1, 2, 3, 4
SELECT max(length(revision_json::jsonb ->> 'reason')) FROM relationship_revisions
SELECT count(*) FROM relationship_revisions WHERE revision_json::jsonb ->> 'reason' ~ '[\r\n]' OR length(btrim(revision_json::jsonb ->> 'reason')) = 0
SELECT max(length(revision_json::jsonb ->> 'targetFactId')) FROM relationship_revisions
SELECT min(length(btrim(revision_json::jsonb ->> 'targetFactId'))) FROM relationship_revisions
SELECT max(length(revision_json::jsonb ->> 'officialId')) FROM relationship_revisions
SELECT count(DISTINCT revision_json::jsonb ->> 'officialId') FROM relationship_revisions
SELECT max((revision_json::jsonb -> 'statementQuote' ->> 'length')::int) FROM relationship_revisions
SELECT count(*) FROM relationship_revisions WHERE revision_json::jsonb -> 'match' -> 'span' ->> 'extractionId' IS DISTINCT FROM revision_json::jsonb -> 'statementQuote' ->> 'extractionId'
SELECT count(*) FROM relationship_revisions r WHERE NOT ((r.revision_json::jsonb -> 'statementQuote' ->> 'start')::int <= (r.revision_json::jsonb -> 'match' -> 'span' ->> 'start')::int AND (r.revision_json::jsonb -> 'match' -> 'span' ->> 'start')::int + (r.revision_json::jsonb -> 'match' -> 'span' ->> 'length')::int <= (r.revision_json::jsonb -> 'statementQuote' ->> 'start')::int + (r.revision_json::jsonb -> 'statementQuote' ->> 'length')::int)
SELECT count(*) FROM relationship_revisions WHERE (revision_json::jsonb -> 'match' -> 'span' ->> 'start')::int < 0 OR (revision_json::jsonb -> 'match' -> 'span' ->> 'length')::int <= 0 OR (revision_json::jsonb -> 'statementQuote' ->> 'start')::int < 0 OR (revision_json::jsonb -> 'statementQuote' ->> 'length')::int <= 0
SELECT count(*) FROM relationship_revisions r WHERE NOT EXISTS (SELECT 1 FROM document_extractions x WHERE x.extraction_id = r.revision_json::jsonb -> 'match' -> 'span' ->> 'extractionId')
SELECT count(*) FROM relationship_revisions r WHERE NOT EXISTS (SELECT 1 FROM document_extractions x WHERE x.extraction_id = r.revision_json::jsonb -> 'statementQuote' ->> 'extractionId')
SELECT count(*) FROM relationship_revisions r WHERE NOT EXISTS (SELECT 1 FROM statement_revisions s WHERE s.candidate_id = r.revision_json::jsonb -> 'statement' ->> 'recordId' AND s.revision_number = (r.revision_json::jsonb -> 'statement' ->> 'revisionNumber')::int)
SELECT count(*) FROM relationship_revisions r WHERE r.revision_json::jsonb -> 'target' ->> 'kind' IN ('Action', '1') AND NOT EXISTS (SELECT 1 FROM action_revisions a WHERE a.record_id = r.revision_json::jsonb -> 'target' ->> 'recordId' AND a.revision_number = (r.revision_json::jsonb -> 'target' ->> 'revisionNumber')::int)
SELECT count(*) FROM relationship_revisions r WHERE r.revision_json::jsonb -> 'target' ->> 'kind' IN ('Vote', '2') AND NOT EXISTS (SELECT 1 FROM vote_revisions v WHERE v.record_id = r.revision_json::jsonb -> 'target' ->> 'recordId' AND v.revision_number = (r.revision_json::jsonb -> 'target' ->> 'revisionNumber')::int)
SELECT count(*) FROM relationship_revisions r JOIN relationship_drafts d ON d.record_id = r.record_id WHERE d.statement_candidate_id <> r.revision_json::jsonb -> 'statement' ->> 'recordId' OR d.target_record_id <> r.revision_json::jsonb -> 'target' ->> 'recordId'
SELECT count(*) FROM relationship_revisions WHERE revision_json::jsonb ->> 'recordId' <> record_id OR (revision_json::jsonb ->> 'revisionNumber')::int <> revision_number
```

The enum numbers in `IN ('Action', '1')` assume the order in `RelationshipRecordKind` (`Statement`, `Action`, `Vote`). The first grouped measure shows the real form. Adjust the two target measures to match.

## 5. Brief revisions

Sources: `src/CivicLens.Core/Briefs/BriefRevision.cs`, `BriefWindow.cs`, `BriefParagraph.cs`, `BriefCitation.cs`, `BriefTextSuggestion.cs`, `BriefCoverage.cs`, `docs/architecture/schema/briefs.md`, `docs/architecture/briefs.md` (line 328 says citations stay in `revision_json`; the PR must change this line).

Current table: `brief_revisions`, same columns as the statement table with `record_id`. Foreign keys to `brief_analysis_runs` and `brief_drafts`. `brief_check_flags` and `brief_review_decisions` have foreign keys to it (`briefs.md` schema lines 38 and 92).

### 5.1 JSON fields

| JSON key | C# type | Null | Verbatim source text? | Note |
|---|---|---|---|---|
| recordId | string | no | no | 64 hex. Must equal `window.recordId` (derived from the week start) |
| revisionNumber | int | no | no | key |
| authorSubject | string | no | no | column exists |
| createdAtUtc | DateTimeOffset | no | no | column exists |
| window.weekStart | DateOnly | no | no | |
| window.startUtcTicks | long | no | no | |
| window.endUtcTicks | long | no | no | week is 167 to 169 hours |
| window.firstReleaseNumber | int | no | no | |
| window.cutoffReleaseNumber | int | no | no | |
| fingerprint | string | no | no | 64 hex |
| headline | string | no | no | model or person text. Max 256 |
| textOrigin | ReviewTextOrigin | no | no | `Model`, `Person`, `Code`. A person revision must be `Person` |
| suggestion | BriefTextSuggestion? | yes | no | has headline, paragraphs, citations, runId (32 hex), fingerprint (64 hex) |
| paragraphs | ImmutableArray<BriefParagraph> | no | no | 1 to 12. Each has `text` (max 4000) and `citationIds` (distinct, 1 or more) |
| citations | ImmutableArray<BriefCitation> | no | no | 1 to 256. Each has `id` (`R1` to `R9999`), `kind` (`DocumentChange`, `Statement`, `Action`), `recordId` (max 64), `revisionNumber` >= 1 |
| coverage.considered | int | no | no | |
| coverage.sentToModel | int | no | no | must be <= considered |
| coverage.cited | int | no | no | must equal the citation count. Derivable |

No field is verbatim source text. A brief is our own prose with links to approved records.

### 5.2 Proposed columns and tables

On `brief_revisions`:

- `week_start date`, `window_start_utc_ticks bigint`, `window_end_utc_ticks bigint`, `first_release_number int`, `cutoff_release_number int`. Add `TimestampTickChecks`, and `window_end > window_start`. Add the week length check (167 to 169 hours) only if `BriefWindow.Validate` owns it as a constant. Foreign key `(record_id, week_start)` to a unique key on `brief_drafts`. `brief_drafts` already has `week_start`. This follows the vote roll identity precedent.
- `fingerprint varchar(64)` with the hex regex.
- `headline varchar(256)`, `text_origin varchar(16)`. Check person author means person origin, as `ValidateShape` does: `author_kind <> 'Person' OR text_origin = 'Person'`.
- Suggestion header: `suggestion_headline varchar(256)`, `suggestion_run_id varchar(32)`, `suggestion_fingerprint varchar(64)`. All null or all set. Foreign key `suggestion_run_id` to `brief_analysis_runs`, as in `vote_revisions`.
- `coverage_considered int`, `coverage_sent_to_model int`. Check `0 <= sent_to_model AND sent_to_model <= considered`. Do not store `cited`. It equals the number of citation rows. See Q9.
- `columns_written` check.

`ReviewTextColumns` does not fit, because a brief has no summary. See Q5.

Child tables, each with `fk (record_id, revision_number) -> brief_revisions on delete restrict`:

| Table | Key | Columns | Notes |
|---|---|---|---|
| `brief_revision_citations` | `(record_id, revision_number, part, citation_id)` | `part varchar(16)` (`Text` or `Suggestion`), `position smallint`, `kind varchar(16)`, `cited_record_id varchar(64)`, `cited_revision_number int` | `citation_id ~ '^R[1-9][0-9]{0,3}$'`, `cited_revision_number >= 1`. Unique `(record_id, revision_number, part, kind, cited_record_id)`. Foreign keys to the cited revision (Q1) |
| `brief_revision_paragraphs` | `(record_id, revision_number, part, position)` | `paragraph_text text` | `length(btrim(paragraph_text)) BETWEEN 1 AND 4000`, no NUL. `position < 12` |
| `brief_revision_paragraph_citations` | `(record_id, revision_number, part, paragraph_position, position)` | `citation_id` | foreign key to `brief_revision_paragraphs` and to `brief_revision_citations (record_id, revision_number, part, citation_id)`. Unique `(record_id, revision_number, part, paragraph_position, citation_id)` for "distinct" |

The two rules "each paragraph cites an ID the brief lists" and "every listed citation is cited" become: the second foreign key proves the first. The second rule needs code or a deferred check (see Q10).

### 5.3 Bounds

| Column | Constant? |
|---|---|
| headline 256 | yes, `BriefRevision.MaximumHeadlineLength` |
| paragraph 4000 | yes, `MaximumParagraphLength` |
| paragraphs 12 | yes, `MaximumParagraphs` |
| citations 256 | yes, `MaximumCitations` |
| cited record ID 64 | yes, `MaximumRecordIdLength` |
| citation ID pattern | regex in a private generated regex (`CitationId()`). Make it a constant so the check reads it |
| coverage ints | no max. Fine. Add `>= 0` |
| week length | `BriefWindow.ShortestWeek` and `LongestWeek` are private statics |

### 5.4 Readers and writers

Writers:
- `Briefs/BriefDraftRows.cs:45-52` (`NewRevision`). Callers: `BriefDraftRows.cs:24` (analysis append), `PostgresBriefReviewStore.cs:43` (person save).

Readers:

| Place | What it reads | PR must move it? |
|---|---|---|
| `Review/ReviewRecords.cs:433` | whole revision via `Records` (`PostgresBriefReviewStore.cs:23`) | yes |
| `Briefs/BriefDraftRows.cs:34` (`ReadCurrentAsync`) | whole revision | yes |
| `Publication/PostgresPublicationStore.cs:291` (`ReadAddedBriefCitationsAsync`) | whole revision, to list cited records | yes. Read the citation table |
| `Review/PostgresReviewQueueStore.cs:33-34,167` | headline | yes. `HeadlineInColumn: true` |
| `Briefs/BriefRecordReader.cs:60` | JSON of cited statement, action, document change revisions (not brief JSON) | not for this kind. It is the reader that the other kinds' PRs must move |
| Tests: `PostgresBriefDraftStoreTests.cs:516,618,708`, `BriefWorldSeeder.cs:71-135` | JSON | yes |
| Docs: `docs/architecture/briefs.md:328` | says citations stay in JSON | yes. Rewrite |

### 5.5 SQL measures

```sql
SELECT count(*) FROM brief_revisions
SELECT revision_json::jsonb ->> 'textOrigin' AS text_origin, author_kind, count(*) FROM brief_revisions GROUP BY 1, 2
SELECT max(length(revision_json::jsonb ->> 'headline')) FROM brief_revisions
SELECT min(length(btrim(revision_json::jsonb ->> 'headline'))) FROM brief_revisions
SELECT max(length(revision_json::jsonb -> 'suggestion' ->> 'headline')) FROM brief_revisions
SELECT count(*) FROM brief_revisions WHERE revision_json::jsonb -> 'suggestion' IS NOT NULL AND jsonb_typeof(revision_json::jsonb -> 'suggestion') <> 'null'
SELECT max(jsonb_array_length(revision_json::jsonb -> 'paragraphs')) FROM brief_revisions
SELECT max(jsonb_array_length(revision_json::jsonb -> 'citations')) FROM brief_revisions
SELECT max(jsonb_array_length(revision_json::jsonb -> 'suggestion' -> 'paragraphs')) FROM brief_revisions
SELECT max(jsonb_array_length(revision_json::jsonb -> 'suggestion' -> 'citations')) FROM brief_revisions
SELECT max(length(p ->> 'text')) FROM brief_revisions r, jsonb_array_elements(r.revision_json::jsonb -> 'paragraphs') p
SELECT max(length(p ->> 'text')) FROM brief_revisions r, jsonb_array_elements(r.revision_json::jsonb -> 'suggestion' -> 'paragraphs') p
SELECT max(jsonb_array_length(p -> 'citationIds')) FROM brief_revisions r, jsonb_array_elements(r.revision_json::jsonb -> 'paragraphs') p
SELECT min(jsonb_array_length(p -> 'citationIds')) FROM brief_revisions r, jsonb_array_elements(r.revision_json::jsonb -> 'paragraphs') p
SELECT max(length(c ->> 'id')) FROM brief_revisions r, jsonb_array_elements(r.revision_json::jsonb -> 'citations') c
SELECT count(*) FROM brief_revisions r, jsonb_array_elements(r.revision_json::jsonb -> 'citations') c WHERE c ->> 'id' !~ '^R[1-9][0-9]{0,3}$'
SELECT max(length(c ->> 'recordId')) FROM brief_revisions r, jsonb_array_elements(r.revision_json::jsonb -> 'citations') c
SELECT c ->> 'kind' AS kind, count(*) FROM brief_revisions r, jsonb_array_elements(r.revision_json::jsonb -> 'citations') c GROUP BY 1
SELECT count(*) FROM brief_revisions r, jsonb_array_elements(r.revision_json::jsonb -> 'citations') c WHERE (c ->> 'revisionNumber')::int < 1
SELECT count(*) FROM brief_revisions r WHERE (r.revision_json::jsonb -> 'coverage' ->> 'cited')::int <> jsonb_array_length(r.revision_json::jsonb -> 'citations')
SELECT count(*) FROM brief_revisions r WHERE (r.revision_json::jsonb -> 'coverage' ->> 'sentToModel')::int > (r.revision_json::jsonb -> 'coverage' ->> 'considered')::int
SELECT min((revision_json::jsonb -> 'coverage' ->> 'sentToModel')::int), max((revision_json::jsonb -> 'coverage' ->> 'considered')::int) FROM brief_revisions
SELECT count(*) FROM brief_revisions r JOIN brief_drafts d ON d.record_id = r.record_id WHERE (r.revision_json::jsonb -> 'window' ->> 'weekStart')::date <> d.week_start
SELECT count(*) FROM brief_revisions WHERE (revision_json::jsonb -> 'window' ->> 'endUtcTicks')::bigint - (revision_json::jsonb -> 'window' ->> 'startUtcTicks')::bigint NOT BETWEEN 167::bigint * 36000000000 AND 169::bigint * 36000000000
SELECT count(*) FROM brief_revisions WHERE revision_json::jsonb ->> 'fingerprint' !~ '^[0-9a-f]{64}$'
SELECT count(*) FROM brief_revisions r, jsonb_array_elements(r.revision_json::jsonb -> 'citations') c WHERE c ->> 'kind' IN ('Statement', '1') AND NOT EXISTS (SELECT 1 FROM statement_revisions s WHERE s.candidate_id = c ->> 'recordId' AND s.revision_number = (c ->> 'revisionNumber')::int)
SELECT count(*) FROM brief_revisions r, jsonb_array_elements(r.revision_json::jsonb -> 'citations') c WHERE c ->> 'kind' IN ('Action', '2') AND NOT EXISTS (SELECT 1 FROM action_revisions a WHERE a.record_id = c ->> 'recordId' AND a.revision_number = (c ->> 'revisionNumber')::int)
SELECT count(*) FROM brief_revisions r, jsonb_array_elements(r.revision_json::jsonb -> 'citations') c WHERE c ->> 'kind' IN ('DocumentChange', '0') AND NOT EXISTS (SELECT 1 FROM document_change_revisions d WHERE d.draft_id = c ->> 'recordId' AND d.revision_number = (c ->> 'revisionNumber')::int)
SELECT count(*) FROM brief_revisions r WHERE revision_json::jsonb -> 'suggestion' ->> 'runId' IS NOT NULL AND NOT EXISTS (SELECT 1 FROM brief_analysis_runs a WHERE a.run_id = r.revision_json::jsonb -> 'suggestion' ->> 'runId')
SELECT count(*) FROM brief_revisions WHERE revision_json::jsonb ->> 'recordId' <> record_id OR (revision_json::jsonb ->> 'revisionNumber')::int <> revision_number
```

The `IN ('Statement', '1')` pairs assume the order of `BriefCitationKind` (`DocumentChange`, `Statement`, `Action`). The grouped kind measure shows the real form.

## 6. Executive action revisions

Sources: `src/CivicLens.Core/ExecutiveActions/ExecutiveActionRevision.cs`, `ExecutiveActionFact.cs`, `ExecutiveActionNotice.cs`, `ExecutiveActionFacts.cs`, `src/CivicLens.Application/ExecutiveActions/ExecutiveActionRevisions.cs`, `docs/architecture/schema/executive-actions.md`.

Current tables:
- `executive_action_revisions`: `record_id varchar(64)`, `revision_number`, `revision_json text`, `created_at_utc_ticks`. No author, no run columns. Code is the only author (`ValidateShape` requires `ReviewAuthor.IsCode`). Foreign key to `executive_action_drafts`.
- `executive_action_documents`: key `(source_id, document_number)`, `record_id`, `revision_number null`, `notice_json text null`, `published_on date`, `document_job_id`. Check `ck_executive_action_documents_pending: revision_number IS NOT NULL OR notice_json IS NOT NULL`. Foreign key `(record_id, revision_number)` to the revision table.

### 6.1 JSON fields of the revision

| JSON key | C# type | Null | Verbatim source text? | Note |
|---|---|---|---|---|
| recordId | string | no | no | 64 hex. Key |
| revisionNumber | int | no | no | key |
| authorSubject | string | no | no | always `civic-lens:code`. No column |
| createdAtUtc | DateTimeOffset | no | no | column exists |
| sourceId | string | no | no | max 128 (literal) |
| documentNumber | string | no | no | Federal Register number. Max 1000 as a fact value |
| kind | ExecutiveActionKind | no | no | `ExecutiveOrder`, `Proclamation`, `Notice`, `Memorandum` |
| presidentialNumber | string? | yes | no | |
| title | string | no | the title is source text, but the quote lives in the extraction | max 1000, one line |
| signedOn | DateOnly | no | no | |
| publishedOn | DateOnly | no | no | |
| federalRegisterCitation | string | no | no | max 1000, one line |
| presidentName | string | no | no | max 1000, one line |
| personId | string | no | no | configuration ID. Max 128 (literal) |
| documentUrl | string | no | no | max 1000, one line |
| facts | ImmutableArray<ExecutiveActionFact> | no | the cited span is verbatim | `label`, `value`, `citation` span. `ValidateShape` forces the list to equal `ExecutiveActionFacts.Of(...)`. So the labels and values are derived. Only the spans are new data. 8 or 9 items |
| notices | ImmutableArray<ExecutiveActionNotice> | no, may be empty | no | `documentNumber` (max 32), `publishedOn`, `url` (max 1000), `citation` span. Max 8. Distinct. Not the revision's own document number |

### 6.2 Fields of `notice_json` on `executive_action_documents`

`notice_json` holds one `ExecutiveActionNotice`: `documentNumber`, `publishedOn`, `url`, `citation` (extraction ID, start, length). It is null for an original document and set for a correction notice.

- `documentNumber` repeats the row key `document_number`.
- `publishedOn` repeats the column `published_on`.
- Only `url` and the `citation` span are new. The JSON is the only copy.
- `notice_json` is never cleared. `ExecutiveActionRows.cs:85` reads every notice of a record that has the JSON. So it is durable source data for every revision that lists the notice.
- The `ck_..._pending` check will use the new columns.

### 6.3 Proposed columns and tables

On `executive_action_revisions`:

- `source_id varchar(128)`, `document_number varchar(1000)`, `kind varchar(16)` (`EnumChecks`), `presidential_number varchar(1000) null`, `title varchar(1000)`, `signed_on date`, `published_on date`, `federal_register_citation varchar(1000)`, `president_name varchar(1000)`, `person_id varchar(128)`, `document_url varchar(1000)`.
- Checks: each text column `length(btrim(x)) BETWEEN 1 AND 1000` and no `\r` or `\n`. This is the rule in `ValidateShape` (`MaximumFieldLength`). `signed_on` and `published_on` are not the default date.
- Author: see Q7. Recommendation: add `author_subject varchar(256)` with a check that it equals the code subject, and `author_kind`.
- `columns_written` check.

Child tables with `fk (record_id, revision_number) -> executive_action_revisions on delete restrict`:

| Table | Key | Columns |
|---|---|---|
| `executive_action_revision_facts` | `(record_id, revision_number, position)` | span (`citation_extraction_id/_start/_length`, `SourceSpanColumns` not optional). The label and value are not stored (Q6) |
| `executive_action_revision_notices` | `(record_id, revision_number, position)` | `source_id`, `document_number`. Foreign key `(source_id, document_number)` to `executive_action_documents`. Unique on `(record_id, revision_number, document_number)` |

On `executive_action_documents` (replaces `notice_json`):

- `notice_url varchar(1000) null`, and the span `notice_citation_extraction_id/_start/_length` as an optional span.
- Check: the url and the span are all null or all set.
- Replace `ck_..._pending` with `revision_number IS NOT NULL OR notice_url IS NOT NULL`.
- The notice rows in the revision then hold no copy of the URL or the date. The document row owns them. See Q6.

### 6.4 Bounds

| Column | Constant? |
|---|---|
| field values 1000 | yes, `MaximumFieldLength` |
| notices 8 | yes, `MaximumNotices` |
| source ID 128, person ID 128 | no. Literals in `ValidateShape` |
| notice document number 32 | no. Literal in `HasBoundedFields` |
| facts count | derived from `ExecutiveActionFacts.Of`. No constant needed. A check on the label set is a code rule |

### 6.5 Readers and writers

Writers of `revision_json`:
- `ExecutiveActions/ExecutiveActionRows.cs:124-128` (`NewRevision`). Caller: `:109` (`AppendApprovedAsync`). The `PostgresExecutiveActionReviewStore.cs:22` records object has no person edit (code writes every field).

Readers of `revision_json`:

| Place | What it reads | PR must move it? |
|---|---|---|
| `Review/ReviewRecords.cs:433` | whole revision via `Records` (`ExecutiveActionRows.cs:17`) | yes |
| `Review/PostgresReviewQueueStore.cs:37,167` | title, as `HeadlineProperty = "title"` | yes. Use the `title` column. `HeadlineInColumn` reads `revision.headline`, so the config needs a column name |
| `ExecutiveActionRows.cs:61-69` (`ReadCurrent` for waiting notices) | uses `ExecutiveActionRevisions.Add(current...)` | check which reader loads `current` |
| tests: `PostgresFederalRegisterTests.cs:100-123` (7 sites), `PostgresReviewQueueTests.cs:265` | JSON selects | yes |

Readers and writers of `notice_json`:

| Place | Use |
|---|---|
| `ExecutiveActionRows.cs:61` | filter `NoticeJson != null` |
| `ExecutiveActionRows.cs:67` | deserialize the notices |
| `ExecutiveActionRows.cs:78` | existence check for a corrected number |
| `ExecutiveActionRows.cs:85,88` | filter, deserialize |
| `ExecutiveActionRows.cs:99` | writer: `row.NoticeJson = Serialize(NoticeOf(evidence))` |
| `Persistence/ExecutiveActionDocumentRow.cs:11`, `ExecutiveActionDocumentRowConfiguration.cs:14,21` | row, check, column |
| `PostgresFederalRegisterTests.cs:113` | `executive_action_documents` count query |

All of these must move in the same PR as the notice columns. The notice move is the cleanest step of the five, because the document row has the key and the date already.

### 6.6 SQL measures

```sql
SELECT count(*) FROM executive_action_revisions
SELECT revision_json::jsonb ->> 'kind' AS kind, count(*) FROM executive_action_revisions GROUP BY 1
SELECT max(length(revision_json::jsonb ->> 'sourceId')) FROM executive_action_revisions
SELECT max(length(revision_json::jsonb ->> 'documentNumber')) FROM executive_action_revisions
SELECT max(length(revision_json::jsonb ->> 'presidentialNumber')) FROM executive_action_revisions
SELECT count(*) FROM executive_action_revisions WHERE revision_json::jsonb ->> 'presidentialNumber' IS NULL
SELECT max(length(revision_json::jsonb ->> 'title')) FROM executive_action_revisions
SELECT max(length(revision_json::jsonb ->> 'federalRegisterCitation')) FROM executive_action_revisions
SELECT max(length(revision_json::jsonb ->> 'presidentName')) FROM executive_action_revisions
SELECT max(length(revision_json::jsonb ->> 'personId')) FROM executive_action_revisions
SELECT max(length(revision_json::jsonb ->> 'documentUrl')) FROM executive_action_revisions
SELECT count(*) FROM executive_action_revisions WHERE revision_json::jsonb ->> 'title' ~ '[\r\n]' OR revision_json::jsonb ->> 'presidentName' ~ '[\r\n]' OR revision_json::jsonb ->> 'federalRegisterCitation' ~ '[\r\n]' OR revision_json::jsonb ->> 'documentUrl' ~ '[\r\n]'
SELECT count(*) FROM executive_action_revisions WHERE revision_json::jsonb ->> 'signedOn' IS NULL OR revision_json::jsonb ->> 'publishedOn' IS NULL
SELECT min(revision_json::jsonb ->> 'signedOn'), max(revision_json::jsonb ->> 'signedOn') FROM executive_action_revisions
SELECT jsonb_array_length(revision_json::jsonb -> 'facts') AS fact_count, count(*) FROM executive_action_revisions GROUP BY 1
SELECT max(jsonb_array_length(revision_json::jsonb -> 'notices')) FROM executive_action_revisions
SELECT max(length(n ->> 'documentNumber')) FROM executive_action_revisions r, jsonb_array_elements(r.revision_json::jsonb -> 'notices') n
SELECT max(length(n ->> 'url')) FROM executive_action_revisions r, jsonb_array_elements(r.revision_json::jsonb -> 'notices') n
SELECT max(length(f ->> 'value')) FROM executive_action_revisions r, jsonb_array_elements(r.revision_json::jsonb -> 'facts') f
SELECT count(*) FROM executive_action_revisions r, jsonb_array_elements(r.revision_json::jsonb -> 'facts') f WHERE NOT EXISTS (SELECT 1 FROM document_extractions x WHERE x.extraction_id = f -> 'citation' ->> 'extractionId')
SELECT count(*) FROM executive_action_revisions r, jsonb_array_elements(r.revision_json::jsonb -> 'notices') n WHERE NOT EXISTS (SELECT 1 FROM document_extractions x WHERE x.extraction_id = n -> 'citation' ->> 'extractionId')
SELECT count(*) FROM executive_action_revisions r, jsonb_array_elements(r.revision_json::jsonb -> 'notices') n WHERE NOT EXISTS (SELECT 1 FROM executive_action_documents d WHERE d.record_id = r.record_id AND d.document_number = n ->> 'documentNumber')
SELECT count(*) FROM executive_action_revisions r, jsonb_array_elements(r.revision_json::jsonb -> 'notices') n JOIN executive_action_documents d ON d.record_id = r.record_id AND d.document_number = n ->> 'documentNumber' WHERE d.published_on <> (n ->> 'publishedOn')::date
SELECT count(*) FROM executive_action_documents WHERE notice_json IS NOT NULL
SELECT count(*) FROM executive_action_documents WHERE notice_json IS NOT NULL AND revision_number IS NULL
SELECT max(length(notice_json::jsonb ->> 'url')) FROM executive_action_documents
SELECT count(*) FROM executive_action_documents WHERE notice_json::jsonb ->> 'documentNumber' <> document_number OR (notice_json::jsonb ->> 'publishedOn')::date <> published_on
SELECT count(*) FROM executive_action_documents d WHERE d.notice_json IS NOT NULL AND NOT EXISTS (SELECT 1 FROM document_extractions x WHERE x.extraction_id = d.notice_json::jsonb -> 'citation' ->> 'extractionId')
SELECT count(*) FROM executive_action_documents d WHERE d.notice_json IS NOT NULL AND NOT EXISTS (SELECT 1 FROM executive_action_documents o WHERE o.record_id = d.record_id AND o.notice_json IS NULL)
SELECT count(*) FROM executive_action_revisions WHERE revision_json::jsonb ->> 'recordId' <> record_id OR (revision_json::jsonb ->> 'revisionNumber')::int <> revision_number
SELECT count(*) FROM executive_action_revisions WHERE revision_json::jsonb ->> 'authorSubject' <> 'civic-lens:code'
```

## 7. Document change draft revisions

Sources: `src/CivicLens.Core/Review/DocumentChangeDraftRevision.cs`, `docs/architecture/schema/review.md` (`document_change_revisions`), `src/CivicLens.Infrastructure/Review/PostgresDocumentChangeReviewStore.cs`.

Current table: `document_change_revisions` has `draft_id varchar(32)`, `revision_number`, `revision_json text`, `created_at_utc_ticks`. Key `(draft_id, revision_number)`. Foreign key to `document_change_drafts`. It has no `author_subject`, `author_kind`, or `run_id` column. The author is only in the JSON.

### 7.1 JSON fields

| JSON key | C# type | Null | Verbatim source text? | Note |
|---|---|---|---|---|
| draftId | string | no | no | 32 hex. Key |
| revisionNumber | int | no | no | key |
| comparisonId | string | no | no | 64 hex. The draft table also has `comparison_id` |
| authorSubject | string | no | no | only in JSON today |
| createdAtUtc | DateTimeOffset | no | no | column exists |
| headline | string | no | no | model or person text. Max 256 |
| summary | string | no | no | max 8000 |
| significance | string? | yes | no | max 4000 |
| limits | string? | yes | no | max 4000. Required for approval? `ValidateForApproval` does not check it, but the resolver does (`required: true`) |
| institution | string | no | no | max 256. Name of the source institution |
| changeDate | DateOnly? | yes | no | null together with evidence |
| changeDateEvidence | SourceSpan? | yes | span only | must be one of `citations` |
| officialIds | ImmutableArray<string> | no | no | configuration IDs. Max 64, each max 128 (literal), distinct |
| issueIds | ImmutableArray<string> | no | no | same |
| citations | ImmutableArray<SourceSpan> | no | spans only | 0 to 64 (approval needs 1). Each length max 8192. Distinct |

The comparison text itself is byte-compared elsewhere and stays as JSON by the R-63 rule. This revision holds no verbatim text.

### 7.2 Proposed columns and tables

On `document_change_revisions`:

- `comparison_id varchar(64)` with the hex check. Option: foreign key to the comparison table if one exists (read `docs/architecture/schema/documents.md`), or the draft's `comparison_id` through a composite foreign key to a unique key on `document_change_drafts (draft_id, comparison_id)`. This follows the vote roll identity precedent.
- `author_subject varchar(256)`, `author_kind varchar(16)` (Q7).
- `headline varchar(256)`, `summary text`, `significance text null`, `limits text null`, `institution varchar(256)`.
- `change_date date null`, and the optional span `change_date_evidence_extraction_id/_start/_length`. Check: date null iff evidence null.
- `columns_written` check over headline, summary, institution, comparison ID, author.
- Checks from the constants: lengths 256, 8000, 4000, 4000, 256. Use the shared headline and summary owner (Q5).

Child tables with `fk (draft_id, revision_number) -> document_change_revisions on delete restrict`:

| Table | Key | Columns |
|---|---|---|
| `document_change_revision_officials` | `(draft_id, revision_number, official_id)` | `position smallint` |
| `document_change_revision_issues` | `(draft_id, revision_number, issue_id)` | `position smallint` |
| `document_change_revision_citations` | `(draft_id, revision_number, position)` | span (`SourceSpanColumns`), check `length <= 8192`. Unique on the span columns |

The change date evidence must be one of the citations. Enforce it with a composite foreign key from the revision row `(draft_id, revision_number, change_date_evidence_extraction_id, _start, _length)` to a unique key on the citation table. A null evidence skips the foreign key (default `MATCH SIMPLE`). See Q8.

### 7.3 Bounds

All text bounds have constants (`MaximumHeadlineLength` and the rest). Missing: selection element length (`128` literal in `ValidateSelections`). Add a shared constant (Q4).

### 7.4 Readers and writers

Writers:
- `PostgresDocumentChangeReviewStore.cs:320-325` (`NewRevisionRow`). Callers: `:315` (`db.Add`, creation of the first revision). Person saves and analysis appends go through `ReviewRecords.AppendRevision`/`Records` (`:27`); read each caller of `Records` in this file to list them.

Readers:

| Place | What it reads | PR must move it? |
|---|---|---|
| `Review/ReviewRecords.cs:433` | whole revision via `Records` (`PostgresDocumentChangeReviewStore.cs:27`) | yes |
| `PostgresDocumentChangeReviewStore.cs:204-215` (`ListRecentActivityAsync`) | whole revision of saved revisions | yes. Needs only author and headline |
| `PostgresDocumentChangeReviewStore.cs:226-234` (`ReadActivityRevisionsAsync`) | whole revision | yes |
| `Review/PostgresReviewQueueStore.cs:25-26,167` | headline | yes. `HeadlineInColumn` |
| `Briefs/BriefRecordReader.cs:60` | whole revision of cited document change | yes (reader of this kind, in the brief feature) |
| `Review/CitedRevisions.cs:35,45` | generic | refuse the kind after the move |
| `Publication` loader for document changes | goes through the review store | confirm in the PR |
| Tests: `ReviewHeldReasonMigrationTests.cs:41`, `BriefWorldSeeder.cs:78` | JSON insert | yes |

The `readonly_access.revisions` function strips `authorSubject` from the JSON and `author_subject` from the row. Doc change has the key only in JSON, so after the move the new `author_subject` column must be stripped too. The function already strips `author_subject`.

### 7.5 SQL measures

```sql
SELECT count(*) FROM document_change_revisions
SELECT max(length(revision_json::jsonb ->> 'headline')) FROM document_change_revisions
SELECT min(length(btrim(revision_json::jsonb ->> 'headline'))) FROM document_change_revisions
SELECT max(length(revision_json::jsonb ->> 'summary')) FROM document_change_revisions
SELECT max(length(revision_json::jsonb ->> 'significance')) FROM document_change_revisions
SELECT count(*) FROM document_change_revisions WHERE revision_json::jsonb ->> 'significance' IS NULL
SELECT max(length(revision_json::jsonb ->> 'limits')) FROM document_change_revisions
SELECT count(*) FROM document_change_revisions WHERE revision_json::jsonb ->> 'limits' IS NULL
SELECT max(length(revision_json::jsonb ->> 'institution')) FROM document_change_revisions
SELECT count(*) FROM document_change_revisions WHERE revision_json::jsonb ->> 'institution' IS NULL OR length(btrim(revision_json::jsonb ->> 'institution')) = 0
SELECT count(*) FROM document_change_revisions WHERE revision_json::jsonb ->> 'changeDate' IS NULL
SELECT count(*) FROM document_change_revisions WHERE (revision_json::jsonb ->> 'changeDate' IS NULL) <> (revision_json::jsonb -> 'changeDateEvidence' IS NULL OR jsonb_typeof(revision_json::jsonb -> 'changeDateEvidence') = 'null')
SELECT max(jsonb_array_length(revision_json::jsonb -> 'officialIds')) FROM document_change_revisions
SELECT max(jsonb_array_length(revision_json::jsonb -> 'issueIds')) FROM document_change_revisions
SELECT max(jsonb_array_length(revision_json::jsonb -> 'citations')) FROM document_change_revisions
SELECT count(*) FROM document_change_revisions WHERE jsonb_array_length(revision_json::jsonb -> 'citations') = 0
SELECT max(length(e)) FROM document_change_revisions r, jsonb_array_elements_text(r.revision_json::jsonb -> 'officialIds') e
SELECT max(length(e)) FROM document_change_revisions r, jsonb_array_elements_text(r.revision_json::jsonb -> 'issueIds') e
SELECT max((c ->> 'length')::int) FROM document_change_revisions r, jsonb_array_elements(r.revision_json::jsonb -> 'citations') c
SELECT count(*) FROM document_change_revisions r, jsonb_array_elements(r.revision_json::jsonb -> 'citations') c WHERE (c ->> 'start')::int < 0 OR (c ->> 'length')::int <= 0
SELECT count(*) FROM document_change_revisions r, jsonb_array_elements(r.revision_json::jsonb -> 'citations') c WHERE NOT EXISTS (SELECT 1 FROM document_extractions x WHERE x.extraction_id = c ->> 'extractionId')
SELECT count(*) FROM document_change_revisions r WHERE r.revision_json::jsonb -> 'changeDateEvidence' ->> 'extractionId' IS NOT NULL AND NOT EXISTS (SELECT 1 FROM jsonb_array_elements(r.revision_json::jsonb -> 'citations') c WHERE c = r.revision_json::jsonb -> 'changeDateEvidence')
SELECT count(*) FROM document_change_revisions r WHERE jsonb_array_length(r.revision_json::jsonb -> 'citations') <> (SELECT count(DISTINCT c) FROM jsonb_array_elements(r.revision_json::jsonb -> 'citations') c)
SELECT count(*) FROM document_change_revisions r, jsonb_array_elements_text(r.revision_json::jsonb -> 'officialIds') e GROUP BY r.draft_id, r.revision_number, e HAVING count(*) > 1
SELECT count(*) FROM document_change_revisions r, jsonb_array_elements_text(r.revision_json::jsonb -> 'issueIds') e GROUP BY r.draft_id, r.revision_number, e HAVING count(*) > 1
SELECT revision_json::jsonb ->> 'authorSubject' AS author, count(*) FROM document_change_revisions GROUP BY 1
SELECT max(length(revision_json::jsonb ->> 'authorSubject')) FROM document_change_revisions
SELECT count(*) FROM document_change_revisions r JOIN document_change_drafts d ON d.draft_id = r.draft_id WHERE d.comparison_id <> r.revision_json::jsonb ->> 'comparisonId'
SELECT count(*) FROM document_change_revisions WHERE revision_json::jsonb ->> 'draftId' <> draft_id OR (revision_json::jsonb ->> 'revisionNumber')::int <> revision_number
```

## 8. Open design questions

Each question says what I checked. The AGENTS.md rule applies: the owner chooses before code is written.

### Q1. How does a revision link to a cited revision of more than one kind?

Where: relationship target (action or vote), brief citations (document change, statement, action).

Precedent checked: no polymorphic foreign key exists today. The vote pilot uses plain foreign keys (`kind_citation_extraction_id` to `document_extractions`) and a composite foreign key to its own draft. R-63 says each link becomes a real row with a foreign key.

Options:
- A. Store `kind`, `record_id`, `revision_number` with no foreign key. Same as the JSON today. Simple. The database cannot prove the link, which goes against R-63.
- B. One nullable composite foreign key per target table, plus a check that exactly one is set and matches `kind`. Two foreign keys for relationships, three for briefs. The database proves each link. The columns are wider (relationship: 6 columns for the target; brief citation rows: 7).
- C. A shared supertype table `record_revisions (kind, record_id, revision_number)` that every revision table references. One foreign key per link. It needs a new write on every revision append and a migration of all kinds. This is the largest change.

Recommendation: B. It gives real links with no new shared table. It uses only keys that exist. Revisit C if a third consumer appears.

### Q2. Does `target_fact_id` get a foreign key?

Checked: `RelationshipRevision.TargetFactId` (max 16). Vote facts are keyed by `position`, not by an ID (`vote_revision_facts`). Action facts: read the action fact owner to see whether it has an ID.

Options:
- A. Plain `varchar(16)` with a length check.
- B. Foreign key to the fact table of the target (needs a fact ID key on action and vote facts, and two more nullable foreign keys).

Recommendation: A for this PR. B depends on how the action pilot keys facts. Decide when the action PR merges.

### Q3. Do configuration IDs (official, issue, policy area) get foreign keys?

Checked: `vote_revision_issues.issue_id` has no foreign key, only `btrim > 0`. Officials and issues are configuration, not database rows (AGENTS.md).

Options: A. No foreign key (pilot). B. Mirror configuration into tables (large change, out of scope).

Recommendation: A. The dangling-ID measures in sections 3 to 7 cannot cover these IDs, because the configuration is not in the database. Add a verify step in code if the owner wants it.

### Q4. One constant for the ID length that has no owner

Checked: speaker, issue, policy area, official IDs are 128 literal in `DocumentChangeDraftRevision.ValidateSelections`, `ExecutiveActionRevision.ValidateShape` (source and person), and the pilot (`varchar(128)`). `RelationshipRevision.MaximumOfficialIdLength` is 256.

Options: A. One Core constant for a configuration ID (128) used by every kind. B. One constant for each record, as the pilot did for bill type.

Recommendation: A, if the relationship measure for `officialId` shows at most 128. If it shows more, keep 256 only for that column and say why in the doc. Add the constant before the first PR so all five reuse it.

### Q5. How do statement and document change reuse the headline and summary owner?

Checked: `ReviewTextColumns.Configure` requires `text_origin` and the four suggestion columns. `IReviewTextRow` has all seven properties. Statement and document change revisions have a headline and summary but no origin and no suggestion. A brief has a headline but no summary.

Options:
- A. Split the owner. A small owner for headline and summary. `ReviewTextColumns` calls it and adds origin and suggestion. Statement and document change call only the small one. A brief uses its own columns.
- B. Add `text_origin` to those kinds. This invents data that the JSON does not hold. Reject.
- C. Copy the column configuration in each kind. This breaks DRY.

Recommendation: A. It follows the shared-owner move in commit `7282888` (vote code that actions need). Do the split in the first PR that needs it (executive action does not need it, document change does).

### Q6. Executive action: store fact text and notice fields, or derive them?

Checked: `ValidateShape` forces `Facts` to equal `ExecutiveActionFacts.Of(...)` from the other fields. So fact label and value are a pure function of the revision's own columns. A notice's document number and date repeat `executive_action_documents`.

Options:
- A. Store label, value, and span for each fact. Store every notice field again in the revision rows. Exact copy of the JSON. Two owners for each rule, which AGENTS.md forbids.
- B. Store only the span of each fact and the document key of each notice. Rebuild the text with `ExecutiveActionFacts.Of`. The notice URL and span move to columns on the document row.
- C. Store the fact text only (no label).

Recommendation: B. One owner for each rule. The backfill rebuild-and-compare proves that the derived text equals the stored JSON, which is the precedent check. If the owner wants the database to prove the facts, add a later check, not a copy.

One risk of B: `executive_action_documents` rows must never change after a revision cites them. The code sets `revision_number` only. Confirm this in the PR and add a test.

### Q7. Author columns for executive action and document change

Checked: statement, relationship, brief, vote, and action revisions have `author_subject`, `author_kind`, `run_id`. Executive action and document change have none. The existing check is `author_kind IN ('Person','Analysis')`. Executive action authors are `Code`.

Options:
- A. Add `author_subject` and `author_kind` to both. Check `author_kind IN ('Person','Analysis','Code')` for these two. Add a check that exec revisions are `Code`.
- B. Add them to exec as a constant only (always code) and to document change as a real column.
- C. Keep the author in JSON. This blocks the JSON drop.

Recommendation: A, without `run_id`. The JSON holds no run link for these two kinds. A run link would be new data. Ask the owner if it is wanted.

### Q8. Can the database prove that evidence spans belong to the right record?

Checked: statement `ValidateAgainst` requires every span to cite the extraction of the candidate. Document change requires `changeDateEvidence` to be in `citations`. Relationship requires the quote to cover the match.

Options:
- A. Leave all three in code (`ValidateShape`, `ValidateAgainst`).
- B. Add database proofs where a key or a row check can hold them: composite foreign key from the statement spans to `statement_candidates (candidate_id, extraction_id)`; composite foreign key from change date evidence to the citation row; row check for the quote covering the match.
- C. B plus triggers. Reject (AGENTS.md: prefer C# when both can hold the guarantee).

Recommendation: B for the three above. Each is a key or a row check, so no trigger is needed. Keep the C# rule too, for the error message. The statement foreign key needs a unique key on `statement_candidates (candidate_id, extraction_id)`. Check that it exists. Measures in sections 3 and 4 and 7 find rows that would break the keys.

### Q9. Brief coverage: store `cited`?

Checked: `ValidateShape` forces `Coverage.Cited == Citations.Length`. It is derivable.

Options: A. Store it and add no check (two owners). B. Do not store it. Rebuild from the count of citation rows (`part = 'Text'`). C. Store it and prove it with a trigger. Reject.

Recommendation: B. The rebuild compare proves equality with the JSON.

### Q10. Brief: one table with a `part` column, or separate tables for the suggestion?

Checked: the suggestion has the same paragraph and citation shape as the text. `BriefRevision.ValidateContent` is one function for both. `ReviewTextColumns` puts the suggestion header columns on the revision row.

Options:
- A. One set of child tables with `part IN ('Text','Suggestion')` in the key. One shape, one set of checks. A foreign key from paragraph citations to citations includes `part`.
- B. Duplicate the three tables for the suggestion. Six tables. Simpler keys, but double the checks and double the reader code.

Recommendation: A. It mirrors the one validation function. One rule the database cannot hold in a foreign key: "every listed citation is cited in some paragraph". Keep it in C# (`ValidateShape`) and prove it with the rebuild compare. Do not add a trigger.

### Q11. Statement bill numbers: child table or `text[]`?

Checked: the vote pilot has no array columns. Drafts do use `text[]` (`statement_drafts.issue_ids`, `official_ids`) as listing indexes. `StatementBillMentions.cs:21-25` queries the numbers with `LIKE` over each element.

Options: A. Child table `statement_revision_bill_numbers` with a `bill_number` index. B. `text[]` column with a GIN index.

Recommendation: A. It follows the pilot and R-63 ("child tables"). Reconsider B only if the measure shows the table is large and the `LIKE` query is slow. Also decide whether the bill number text should link to bill rows. It is a text mention found in the article, so recommend no link.

### Q12. One backfill store per kind, or one generic store?

Checked: `PostgresVoteRevisionBackfillStore` (150 lines) uses the vote lock, `VoteRevisionRows.Rebuild`, and `VoteRevisionComparison`. The shared parts are `IRevisionBackfillStore`, `RevisionJsonComparison`, and `RevisionBackfillReasons`. A command surface exists for votes (`votes revisions backfill|verify`, `docs/operations.md#vote-revision-backfill`).

Options:
- A. One small store per kind that reuses the shared parts. Each uses its own lock key.
- B. One generic store parameterized by a mapper per kind.

Recommendation: A for the first two PRs. After the third kind, look at the three copies and extract only what is identical. The action PR will also add a store. Compare with it first. Do not build the generic store before then (AGENTS.md: no generic helper without a concrete requirement).

### Q13. Do tests keep working until the drop?

Checked: after step 2 a reader fails on a row with null columns. About 20 test files insert `revision_json` only (section 2, point 4).

Options: A. Update each insert to write the columns. B. Add a shared seeder helper that builds the row with the production `NewRevision`. C. Keep the old inserts and let readers fall back to JSON. Reject: the pilot says every reader reads the columns.

Recommendation: B. One helper in `tests/CivicLens.Tests/Fixtures`, used by `BriefWorldSeeder` and `RelationshipWorldSeeder` first. This is a case where AGENTS.md allows a shared fixture ("several test groups need them").

## 9. Suggested PR order

1. **Executive action revisions.** It is the smallest kind. Code is the only author, so there is no person edit path. No other kind reads its JSON. It also moves `notice_json`, which is the only JSON column outside the revision tables. It tests Q6 and Q7 (derived facts, author columns) on the least risky data. Nothing else needs to wait for it.
2. **Document change draft revisions.** Its readers are in one store plus the brief reader. It introduces the split of the headline and summary owner (Q5) and the evidence-in-citations foreign key (Q8). The brief PR cites it, so it must come first.
3. **Statement revisions.** It has the widest reader footprint: relationships, briefs, `StatementBillMentions`, and the queue. It adds useful columns (`statement_date`, bill numbers) that the relationship SQL needs. Relationships and briefs cite it, so it must come before them. Do it after document change, because it reuses the split owner from PR 2.
4. **Relationship revisions.** It needs the statement keys (they exist today) and the statement reader (PR 3). Its action target needs the action PR (in flight) to have moved the action reader, because `ReadRevisionJsonAsync` will refuse the action kind. Its design choice (Q1, option B) is the first use of the polymorphic link, with two targets. That is easier than the brief case with three.
5. **Brief revisions.** It is the largest structure: three child tables, two parts, three polymorphic links. It reads statement, action, and document change revisions, so all of those readers must be on columns first. It uses the Q1 pattern proven in PR 4. It has the most test seeders to move.

Step 4 of the pilot (drop the JSON) for each kind is a separate later change. The first drop must also change `readonly_access.revisions` (`readonly-grants.sql:75`), and the same change then serves every later drop.

Dependency summary:

| PR | Must land before it |
|---|---|
| executive action | none |
| document change | none (the split owner is in this PR) |
| statement | none. Use the split owner from the document change PR |
| relationship | statement, and the action PR that is in flight |
| brief | document change, statement, action |

Items the owner must decide before PR 1 starts: Q4 (the shared ID length constant), Q6 and Q7.
