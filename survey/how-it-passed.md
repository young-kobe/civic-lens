# How it passed: design survey (R-69 F1)

This survey plans one feature. It changes no code. Repository paths cite origin/main at d0610764.

## Goal

Each bill and nomination shows how it passed: by roll call, by voice vote, or by unanimous consent. A reader then sees when no member's position is on record. The same data must also serve a later insight, "laws with no roll call". The owner reports that production has 71 of 134 laws in the 119th Congress with no substantive roll call on our site. We could not check this count from this session.

## Access note

The proxy refused api.congress.gov and www.congress.gov with "CONNECT tunnel failed, response 403". We sent no request around the block. We read the Congress.gov API documentation on github.com instead.

Labels:

- **Verified** means we read the text in this repository or at the URL given.
- **Unverified** means background knowledge. Read the source before you rely on it.

The Congress.gov documentation is verified at https://github.com/LibraryOfCongress/api.congress.gov/blob/main/Documentation/BillEndpoint.md (here "BillEndpoint.md") and https://github.com/LibraryOfCongress/api.congress.gov/blob/main/Documentation/NominationEndpoint.md (here "NominationEndpoint.md"), read on 2026-11-05.

## 0. Correction to the brief

The brief says that we store only the latest action of each bill. That is true for parsed data. It is not true for evidence.

- The gate row stores only `latestAction` of the detail (`docs/architecture/bills.md:29`, `docs/architecture/schema/bills.md:31`).
- A passed gate also admits the full actions chain (`docs/architecture/bills.md:188`, `docs/architecture/bills.md:190`). Each actions page is saved as one extraction and one `bill_pages` row with the role `Actions` (`docs/architecture/bills.md:200`, `docs/architecture/bills.md:302`, `docs/architecture/schema/bills.md:59`).
- An action candidate needs the actions chain closed (`docs/architecture/bills.md:271`, `src/CivicLens.Infrastructure/Collection/Jobs/PostgresCollectionJobStore.ActionCandidates.cs:78`).
- No code reads the actions pages after that. "Actions pages are not read" (`docs/architecture/actions.md:134`). The action draft store skips the role (`src/CivicLens.Infrastructure/Actions/PostgresActionDraftStore.cs:140`).

So the action history is in our captures and extractions today. It has no table, and the parser drops the roll call links. This makes F1 mostly a parse and storage task, not a collection task.

## 1. Where the action history comes from

### Source

The source is the Congress.gov bill actions endpoint, `{source url}/{congress}/{type}/{number}/actions` with `format=json&limit=250` (`docs/architecture/bills.md:101`). `CongressBillActionsParser` reads it, at extraction version `congress-bill-actions-v1` (`src/CivicLens.Application/Collection/Bills/CongressBillActionsParser.cs:7`).

Each action item has these fields (verified, BillEndpoint.md, "actions" section):

- `actionDate`, an optional `actionTime`, and `text`.
- `type`, one of "Committee", "Calendars", "Floor", "BecameLaw", "IntroReferral", "President", "ResolvingDifferences", "Discharge", "NotUsed", and "Veto".
- `actionCode`, present "only for actions where the `<sourceSystem>` is 2 (House) or 9 (Library of Congress)". The documentation says "An authoritative list of action codes where the `<sourceSystem>` is 2 (House) does not exist."
- `sourceSystem` with a `code` and a `name`. "0" is Senate, "1" and "2" are House, and "9" is Library of Congress.
- `recordedVotes`, a "Container for recorded (roll call) votes associated with the action". Each item has `rollNumber`, `url`, `chamber`, `congress`, `date`, and `sessionNumber`.

The parser keeps date, time, text, type, code, and source system (`src/CivicLens.Application/Collection/Bills/CongressBillActionsParser.cs:22`, `src/CivicLens.Application/Collection/Bills/CongressBillAction.cs:3`). It ignores `recordedVotes`, because its wire record has no such field (`src/CivicLens.Application/Collection/Bills/CongressBillActionsParser.cs:34`). The capture still holds the raw JSON body, because a capture is the exact response body by SHA-256 (`docs/architecture/collection.md:115`, `docs/architecture/collection.md:249`).

### Fixtures in the repo

The repo holds no captured actions page. It holds only short inline test rows:

- One Library of Congress row: `{"actionCode":"E40000",...,"sourceSystem":{"code":9,"name":"Library of Congress"},"text":"Became Public Law No: 119-21.","type":"President"}` (`tests/CivicLens.Tests/Application/Collection/Bills/CongressBillActionsParserTests.cs:14`). This row is hand-written. The real code for this action is unverified.
- One floor row with only a date, a time, and "Passed House." (`tests/CivicLens.Tests/Application/Collection/Bills/CongressBillActionsParserTests.cs:19`, `tests/CivicLens.Tests/Application/Collection/Bills/CongressBillRendererTests.cs:100`).
- Committee texts "Ordered to be Reported by Voice Vote." and "... by Unanimous Consent." (`tests/CivicLens.Tests/Application/Publication/BillStatusMeaningTests.cs:82`, `tests/CivicLens.Tests/Application/Publication/BillStatusMeaningTests.cs:83`). These are committee votes, not floor passage.

No fixture holds `recordedVotes`. PR 2 below must add a few real action pages as small fixtures with provenance, as AGENTS.md requires.

### How collection fetches it within jobs and budgets

No new job and no new budget are needed.

- Every configured member has a bioguide ID. `config/federal-coverage.json` lists all 539 current members (`docs/architecture/configuration.md:48`). A covered sponsor passes the gate at once (`src/CivicLens.Application/Collection/Bills/BillGate.cs:22`). So almost every bill of a current member gets its actions chain.
- The detail job key holds the update date (`docs/architecture/bills.md:153`). Each new version of a bill therefore fetches its actions again. The newest version always has the newest history.
- The 251-job budget already counts the actions job (`docs/architecture/bills.md:141`). A detail admits at most four follow-ons, and a follow-on page admits at most three.
- A long history pages through `next` links in the same chain (`docs/architecture/bills.md:196`).

### Request count for the 119th Congress

- **Backfill: 0 Congress.gov requests.** The backfill reads the saved actions captures, as the status backfill reads saved detail captures. "The command makes no Congress.gov request, because the capture holds the detail" (`docs/architecture/bills.md:263`, `src/CivicLens.Application/Collection/Bills/BackfillBillStatus.cs:36`). Captures have no retention policy, so they stay on disk (`docs/architecture/collection.md:542`).
- **Steady state: 0 added requests.** The actions page is already fetched for each passed bill version.
- **Gap: bills whose gate is `notRelevant`.** They have no actions chain. With full member coverage, this is mostly a bill whose sponsor is outside coverage dates. Count them with `SELECT count(*) FROM bill_gates WHERE congress = 119 AND decision = 'NotRelevant'` (columns at `docs/architecture/schema/bills.md:31`). We could not run it. Each such bill costs 1 request per 250 actions. Fetching them needs an owner decision (D5).
- **A full re-fetch, if ever needed:** about 1 request for each bill. A dev scan read 16,819 bill details (`docs/architecture/bills.md:129`). The API limit is 5,000 requests an hour (unverified here; the earlier survey verified it in the API README). A re-fetch is therefore a few hours of key budget. We do not recommend it.

## 2. How code tells the methods apart

### Signals, strongest first

1. **`recordedVotes` on the action (verified field, BillEndpoint.md).** A passage action with a recorded vote is a roll call. Code joins it to our vote rows by chamber, Congress, session, and roll number. `vote_versions` holds the chamber and the roll identity (`docs/architecture/votes.md:10`, votes Storage and contracts). A House roll call of the 118th or 119th Congress is in our data (`docs/architecture/votes.md:208`). A Senate roll call is there only when the Voteview source is enabled (`docs/architecture/votes.md:368`).
2. **The roll number in the text.** House text ends with "(Roll no. N)" and Senate text with "Record Vote Number: N" (Senate form verified: "Passed Senate without amendment by Yea-Nay Vote. 79 - 19. Record Vote Number: 71.", BillEndpoint.md; House form unverified). Code uses it as a cross check of signal 1.
3. **Method words at the end of the text.** Examples are "by Voice Vote", "by voice vote", "by Unanimous Consent", "without objection", and "by Yea-Nay Vote". BillEndpoint.md verifies one voice vote form: "On agreeing to the Maloney, Carolyn B. amendment (A002) Agreed to by voice vote." The other forms are unverified. The precedent for anchored text rules is `BillStatusMeaning`, which matches whole known shapes from the start and never guesses (`src/CivicLens.Application/Publication/BillStatusMeaning.cs:5`, `docs/architecture/publication.md:238`). Its committee rule already reads "by Voice Vote", "by Unanimous Consent", and "by the Yeas and Nays: N - N" (`src/CivicLens.Application/Publication/BillStatusMeaning.cs:82`).
4. **Action codes.** These only find the step, not the method. Library of Congress codes mark "passed House", "passed Senate", and "became law" (unverified values, the code list page was blocked). House codes have no authoritative list (verified quote above). Senate actions have no code (verified, BillEndpoint.md). So codes never decide the method alone.

The step also needs `type` "Floor" or "ResolvingDifferences", and the source system of the chamber. Congress.gov often lists one event twice, once from the chamber and once from the Library of Congress (unverified). Code must treat the pair as one step.

### Example actions

From repo fixtures: none show a floor method. The only method words in the repo are committee texts (`tests/CivicLens.Tests/Application/Publication/BillStatusMeaningTests.cs:82`). From the verified documentation:

| Text | Method | Source |
|---|---|---|
| "Passed Senate without amendment by Yea-Nay Vote. 79 - 19. Record Vote Number: 71." with `recordedVotes` roll 70 example | Roll call | BillEndpoint.md, verified |
| "On agreeing to the Maloney, Carolyn B. amendment (A002) Agreed to by voice vote." | Voice vote (an amendment, not passage) | BillEndpoint.md, verified |
| "Confirmed by the Senate by Voice Vote." with `actionCode` S05310 | Voice vote (nomination) | NominationEndpoint.md, verified |
| "Passed Senate without amendment by Unanimous Consent." | Unanimous consent | Unverified |
| "On motion to suspend the rules and pass the bill Agreed to by voice vote." | Voice vote | Unverified |
| "On passage Passed without objection." | Unanimous consent | Unverified |

PR 2 must replace each unverified row with a captured page.

### Cases code cannot decide

Code must show these as "Civic Lens could not tell" with the official text, as `BillStatusMeaning` does for unmatched text (`docs/architecture/publication.md:238`).

- **Text that matches no rule.** Show the raw text.
- **Text and `recordedVotes` disagree,** for example a voice vote text with a roll number, or two different roll numbers.
- **A roll call that we do not hold,** such as a Senate vote while the Voteview source is off. Code knows a roll call took place. It cannot show the casts. The page says a roll call exists and links to the official record.
- **Deemed passage.** A House rule can pass a bill when the House adopts the rule (unverified). The roll call is on the rule, which our kind table calls procedural (`docs/architecture/votes.md:45`, `docs/architecture/publication.md:275`).
- **En bloc passage.** One voice vote, one consent, or one roll call can pass many measures (unverified). The roll call legislation names one bill or none (`docs/architecture/votes.md:19`).
- **Passage inside another bill.** The text can become law as part of a larger bill (unverified). The small bill then never passes. The insight must not count it as "no roll call".
- **Companion bills.** The Senate can pass the House bill in place of its own (unverified). Each bill keeps its own path.
- **A step that was undone,** such as a consent vitiated or a vote reconsidered (unverified).
- **A House "recorded vote" or "Yeas and Nays" with no roll number in the text.** Code then relies on `recordedVotes` alone.

## 3. Storage options

Precedents checked:

- `bill_text_versions`, a feature table beside `bill_gates`, written in the admission transaction (`docs/architecture/bills.md:46`, `docs/architecture/bills.md:239`).
- `bill_pages` rows, written in the same transaction as the page admission (`docs/architecture/bills.md:245`).
- `action_revision_sponsorships` and `vote_revision_facts`, child tables keyed by position (`docs/architecture/actions.md:292`, votes Storage and contracts).
- The gate columns `latest_action_date` and `latest_action_text`, which hold a status, not a history (`docs/architecture/bills.md:294`).
- R-63 and data-first-class: "Our data and our schemas are a first class feature", and stored JSON stays only for verbatim text (`docs/roadmap.md:96`, `docs/roadmap.md:160`, `docs/agent-rules.md:26`).
- The rule that status is never part of an action revision, "because a bill moves after approval" (`docs/architecture/bills.md:29`).

### Option A: child tables of the bill version (recommended)

- `bill_actions`: one row for each action item, keyed by `detail_job_id` and position. Columns: `action_date`, `action_time` null, `text` (verbatim, trimmed), `type` null, `action_code` null, `source_system_code` null, `source_system_name` null, and the `job_id` of its `bill_pages` row. Foreign keys to `bill_gates` and `bill_pages`.
- `bill_action_recorded_votes`: one row for each recorded vote of an action, keyed by `detail_job_id`, action position, and its own position. Columns: `chamber`, `congress`, `session`, `roll_number`, `voted_at`, and `url`. Checks on the chamber and the number ranges.
- The actions page admission writes both in its transaction, as `bill_pages` is written (`docs/architecture/bills.md:245`).
- **For:** every fact is a column with database checks. It keeps the history of each version, so a corrected text never overwrites evidence. The later insights, such as bill fate (R-69 F2), read the same rows.
- **Against:** rows repeat for each version of a bill. A bill with 10 versions and 50 actions holds 500 rows. The size is still small. A query takes the newest version, as the bill status query does (`docs/architecture/publication.md:235`).

### Option B: one table keyed by bill identity, first seen wins

- One row for each distinct action of a bill, keyed by congress, type, number, and an action hash.
- **For:** no repeated rows.
- **Against:** a text that Congress.gov corrects becomes a second row with no clear order. It breaks the per-version evidence chain of `bill_pages`. No comparable feature uses this key for a list that can change.

### Option C: derived columns on `bill_gates`

- Columns such as `house_method`, `senate_method`, and `house_roll_number` on each gate row.
- **For:** smallest change. It copies the latest action columns.
- **Against:** it stores our classification, not the source facts. A rule change needs a new backfill. It cannot serve bill fate or other history insights. It cites no action line.

### Option D: columns on the action revision

- **Against:** rejected by an agreed decision. Status is never part of an action revision, because a bill moves after approval (`docs/architecture/bills.md:29`). A path changes after approval, for example when the Senate passes the bill later.

### Option E: a JSON array of actions on the gate

- **Against:** R-63 forbids our own data in JSON (`docs/roadmap.md:96`). The capture already holds the verbatim JSON.

### Where the classification lives

The method is a rule over stored facts. Two choices exist, and D2 asks the owner. Code can compute it at publish, as `BillStatusMeaning` does. Or C# can write it once as a column, as `action_revisions.event_on` is written (`docs/architecture/actions.md:290`).

## 4. Contract and page

### Today

- `bill-status.json` holds one row for each action record: record ID, text, date, and read time (`src/CivicLens.Publication.Contracts/PublishedBillStatus.cs:12`, `docs/architecture/publication.md:40`).
- The action page shows "Latest action on Congress.gov", "Status read on", and "Official bill page" (`src/CivicLens.Host/Publication/PublishedActionView.cs:31`).
- The official page shows a status line under each sponsored bill (`docs/architecture/publication.md:239`).
- A carried action page is written again when its status row changes (`docs/architecture/publication.md:237`). The worker publishes a status-only release when a status changed (`docs/architecture/publication.md:240`).

### Proposed published fields

A new file, `bill-passage.json`, beside `bill-status.json`, for the same reason: a path changes after approval. One row for each action record with at least one passage step. Each step has:

- `chamber`: `house` or `senate`.
- `step`: `passage`, `agreeToOtherChamber` (a vote on the other chamber's version), `vetoOverride`, or `law`.
- `date`.
- `method`: `rollCall`, `voiceVote`, `unanimousConsent`, or `unknown`.
- `officialText`: the verbatim action text.
- `roll`: chamber, Congress, session, and roll number, or null.
- `voteRecordId`: our published vote record of that roll, or null when we hold none.

A row also has `readAtUtc`, as a status row does. The file is validated on write and read, like `PublishedBillStatuses`.

### Where the path shows

1. The action page, as a "How it passed" fact under "Latest action on Congress.gov" (`src/CivicLens.Host/Publication/PublishedActionView.cs:31`).
2. The official page bill rows, as one short line under the status line (`docs/architecture/publication.md:239`).
3. A statement record that names the bill links to the action page. Statement records find bill numbers with `BillNumberFinder` (`docs/architecture/statements.md:48`). The Related panel already links records that cite each other (`docs/architecture/publication.md:134`).
4. The method page, with one note that defines each method.

### Draft wording

- Roll call: "The House passed this bill by roll call 190 on 3 Mar 2026. See how each member voted."
- Roll call that we do not hold: "The Senate passed this bill by roll call 71 on 8 Mar 2026. Civic Lens does not have the votes of this roll call. See the official record."
- Voice vote: "The House passed this bill by voice vote on 3 Mar 2026. The record shows no member's position."
- Unanimous consent: "The Senate passed this bill by unanimous consent on 8 Mar 2026. No senator objected. The record shows no senator's position."
- Unknown: "Civic Lens could not tell how this step passed. Official text: ..."
- Summary for a law with no roll call: "This bill became law with no roll call in either chamber. The record shows no member's position on it."
- Method note: "A voice vote and unanimous consent record no names. A member can still support or oppose the bill. The record does not show which."

### Nominations: what we hold today

- We hold only Senate roll calls from Voteview. A roll call with question "On the Nomination" has the kind nomination (`docs/architecture/votes.md:46`). A cloture vote on a nomination has the kind cloture.
- The Voteview `bill_number` column holds the nomination number, such as `PN1235` (`tests/CivicLens.Tests/Fixtures/VoteviewVoteFixtures.cs:26`). The parser keeps only bill types and drops it (`src/CivicLens.Application/Collection/VoteviewVotes/VoteviewParser.cs:82`). So a nomination vote has no nomination identity (`docs/architecture/votes.md:93`).
- We hold no nomination record and no nomination action. A confirmation by voice vote or by consent is absent from our data.
- The `/nomination/{congress}/{number}/actions` endpoint gives action text such as "Confirmed by the Senate by Voice Vote." and a Senate `actionCode` such as S05310 (verified, NominationEndpoint.md). Its documented action item has no `recordedVotes` field (verified by absence in the "Actions Level" section). So a nomination roll call needs the text "Record Vote Number" (unverified form) and the Voteview roll.
- Nominations therefore need a new source under R-58, with a terms and robots check first (`docs/roadmap.md:89` as amended by PR #368). F1 can ship for bills first.

## 5. PR list

| # | PR | Size | Precedent |
|---|---|---|---|
| 1 | Parse `recordedVotes` in `CongressBillActionsParser`. Add the two child tables and write them in the actions page admission. Regenerate schema docs. | Medium | `bill_text_versions` and `bill_pages` writes in the admission transaction (`docs/architecture/bills.md:239`, `docs/architecture/bills.md:245`); child rows of `action_revision_sponsorships` (`docs/architecture/actions.md:292`) |
| 2 | Add 4 to 6 captured action pages as fixtures with provenance: a House roll call, a House voice vote, a Senate consent, a Senate roll call, a law, and one en bloc case. The owner captures them, because this session cannot reach Congress.gov. | Small | Voteview and House vote fixtures (`tests/CivicLens.Tests/Fixtures/VoteviewVoteFixtures.cs`, `tests/CivicLens.Tests/Fixtures/HouseVoteFixtures.cs`) |
| 3 | `bills actions backfill [limit]` reads saved actions captures into the tables. No Congress.gov request. | Small | `bills status backfill` (`docs/architecture/bills.md:261`, `src/CivicLens.Application/Collection/Bills/BackfillBillStatus.cs:10`) |
| 4 | `PassageMethod`, a pure classifier with anchored rules and a roll cross check. It returns `unknown` for the cases in section 2. | Small | `BillStatusMeaning` (`src/CivicLens.Application/Publication/BillStatusMeaning.cs:10`) and `VoteKindTable` (`docs/architecture/votes.md:45`) |
| 5 | `bill-passage.json` contract, the read query, the action page fact, the official row line, and the method note. Status-only publish covers a path change. | Medium | `bill-status.json` and the bill status line (`docs/architecture/publication.md:40`, `docs/architecture/publication.md:549`) |
| 6 | Insight "laws with no roll call": a count and list per chamber, with the method note and the undecided cases shown, not hidden. | Small | R-47 boards (`docs/roadmap.md:41`); `IsSubstantive` as the one owner of substantive kinds (`docs/architecture/publication.md:275`) |
| 7 | Later: Congress.gov `/nomination` source and the Voteview `PN` identity. | Medium | The bill stream connector, which the vote connector mirrors (`docs/architecture/votes.md:11`) |

PRs 1 to 4 need no contract change. PR 5 changes a public contract and public wording, so the owner approves its wording first (`docs/agent-rules.md:15`).

## 6. Open decisions

**D1. Storage shape for the action history.**

- Problem: the history is in captures and extractions, but no table holds it as data (section 0).
- Options:
  - (a) Child tables of the bill version (Option A). Full fidelity and history. Rows repeat across versions.
  - (b) One table by bill identity (Option B). No repeats. Corrected text has no clear order.
  - (c) Derived columns on `bill_gates` (Option C). Smallest. Stores our classification only, and cannot serve other insights.
- Recommended: (a). It follows `bill_pages` and the child table precedent, and it serves F2.

**D2. Where the method is computed.**

- Problem: the method is a rule over stored facts, and the rule will grow with new cases.
- Options:
  - (a) Compute at publish in C#, like `BillStatusMeaning`. A rule change needs no backfill. The `public_api` views cannot filter by method without a copy.
  - (b) Write a `method` column once per action with a rule version, like `event_on`. The database can count it. A rule change needs a recompute command.
- Recommended: (a) now. Add (b) only when a `public_api` view or search filter needs the method.

**D3. Roll call evidence.**

- Problem: `recordedVotes` and the text can disagree.
- Options:
  - (a) Trust `recordedVotes` only. Simple. A missing field hides a roll call.
  - (b) Trust the text only. Senate text has the number; House text form is unverified.
  - (c) Use `recordedVotes` and require the text number to agree when present. Disagreement gives `unknown`.
- Recommended: (c). It never states a method that two sources contradict.

**D4. Citation of the roll link.**

- Problem: the extraction line has no roll tag (`src/CivicLens.Application/Collection/Bills/CongressBillRenderer.cs:118`). A change to rendered text needs a new extraction version (`docs/architecture/bills.md:24`).
- Options:
  - (a) Bump to `congress-bill-actions-v2` and render a roll tag. Each roll is citable in text. Old pages keep v1, so the backfill has mixed versions.
  - (b) Keep v1. Cite the action text line. Keep the roll from the capture in the row. The row points to its `bill_pages` row and so to its capture.
- Recommended: (b). The path is not a reviewed record. The capture is the exact evidence.

**D5. Bills with a `notRelevant` gate.**

- Problem: they have no actions chain, so their path is unknown.
- Options:
  - (a) Leave them out and show the count on the method page. No new requests.
  - (b) Admit an actions chain for each law, whatever the gate. About 1 request per such law.
- Recommended: (a) first. Run the count query in section 1. Choose (b) only if laws fall into this gap.

**D6. Contract file.**

- Problem: the path changes after approval, like the status.
- Options:
  - (a) Add the steps to `PublishedBillStatus`. One file. It mixes the latest action with a history, and every status reader changes.
  - (b) A new `bill-passage.json` with its own validation and change check. Follows `bill-status.json`.
- Recommended: (b).

**D7. What counts as "no roll call" for the insight.**

- Problem: a law can have a roll call on a rule, on cloture, or on another bill that carried its text.
- Options:
  - (a) Count only roll calls on a passage step of this bill, with substantive kinds from `IsSubstantive`. Matches the 71 of 134 figure, if the owner used the same rule (unverified).
  - (b) Also count procedural roll calls on the bill. Fewer laws look unrecorded, but a rule vote is not a position on the bill.
- Recommended: (a), with the undecided cases listed beside the count, never folded in.

**D8. Nominations scope.**

- Problem: we hold only roll-call nomination votes, with no nomination identity.
- Options:
  - (a) Ship F1 for bills. Add nominations with the R-58 nomination source.
  - (b) Show "roll call" on nomination vote pages now from Voteview. It cannot show voice votes, so a reader sees only one side.
- Recommended: (a). A partial nomination path would mislead.
