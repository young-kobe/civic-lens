# Votes

## Purpose

The votes feature collects House roll-call votes from the Congress.gov `house-vote` API. It fetches the detail and the member votes of each roll call as durable jobs. Each roll call version with both pages captured becomes a vote candidate. [Action drafting](actions.md#limits-and-planned-work) will drain the candidates. That drafting is planned.

## Decisions

- The vote connector mirrors the [bills](bills.md) connector. It reuses job keys, admission, budgets, URL page rules, and preparation dispatch.
- The Congress.gov vote list ignores `fromDateTime` and is not ordered by date. The source therefore has no cursor and no window.
- A daily sweep reads the whole list of each session, from the session of `backfillFrom` to the session of today. Unchanged votes cost one list read and no job.
- The session of a calendar year Y has congress `(Y - 1787) / 2` (integer division). Its number is 1 for an odd Y and 2 for an even Y. `HouseVoteSession` owns this rule.
- The sweep is one chain of list jobs. The first job lists the first session. The last page of a session admits the first page of the following session, up to the session of today.
- Every list page is a root job, as for bills, so a running sweep blocks the next scheduled check.
- A detail job is keyed by source, roll call, and exact update time. A known key is a duplicate. A changed vote has a new update time and so gets a new job.
- Every collected roll call becomes a candidate. No relevance gate exists. The noise is measured before a gate is considered.
- A roll call may have no legislation. The bill identity is then null. A legislation type that the bills feature does not store also gives a null bill identity.
- The bill type uses the lowercase form of `CongressBillListItem.NormalizeType`. The known type list has one owner.
- The page limit rules (`limit`, `format`, `offset`) are the ones of `ApiPageQuery`. The 251-job admission rule is the one of bills.
- Preparation selects its work from `apiResource`, never from the URL text.
- A deferred job would be lost. Admission therefore throws `InvalidDataException` and saves nothing when the budget defers a job.
- The members response is one request. It lists every member, so it has no paging. Measured on 2026-10-16, a roll call lists 433 members.
- The detail and members `updateDate` were equal on 4 live votes on 2026-10-16 (119/1/100, 119/1/306, 119/1/320, 119/2/74). A members response with another update time than the recorded detail is still refused, because it is another version.
- The sweep of a session starts on 1 January, about 2 days before Congress starts on 3 January. The API returns 200 for a session that has not started (measured 2026-10-16 for `/house-vote/120/1`), so the early sweep is harmless.
- The Speaker election has totals by candidate, not by party. The parser reads them as candidate totals, and the renderer writes one line for each. The parser and render versions stay at v1. No input that gave a v1 extraction renders other text. A party item that also names a candidate is now invalid. No captured vote has that shape.
- Preparation renders each detail and each members response into extraction text, as the [bills](bills.md#extraction) connector does for its pages. The candidate binds both extractions. Drafting will cite the exact lines of these extractions.

## Invariants

Each line gives the rule, what enforces it, and the test that protects it. Cross-feature invariants are in [architecture](../architecture.md#invariants).

- One record exists for each vote version. Enforced by `ux_house_votes_version`. Test: `PostgresHouseVoteTests.ASecondDetailJobOfTheSameVoteVersionRecordsNothingAndAdmitsNoMembersJob`.
- A changed vote is a new version with its own members job. Enforced by `HouseVoteJobKeys` and `PostgresCollectionJobStore.AdmitDetailAsync`. Test: `PostgresHouseVoteTests.AChangedVoteIsRecordedAsANewVersionWithItsOwnMembersJob`.
- A bill type has a number, and a number has a type. Enforced by `ck_house_votes_bill` and `ck_house_vote_candidates_bill`. Test: `PostgresHouseVoteTests.TheDatabaseRefusesABillTypeWithoutANumber`.
- A candidate needs a recorded detail of the same version. Enforced by `PostgresCollectionJobStore.AdmitMembersAsync`. Test: `PostgresHouseVoteTests.MembersOfAVersionWithoutARecordedDetailAreInvalidDataAndWriteNoCandidate`.
- A candidate binds an extraction of its own detail job and an extraction of its own members job. Enforced by `PostgresCollectionJobStore.EnsureExtractionIsOfJobAsync`. Test: `PostgresHouseVoteTests.ADetailWithAnExtractionOfAnotherJobIsInvalidDataAndRecordsNoVote`.
- A vote version has one candidate. Enforced by `ux_house_vote_candidates_detail_job`. Test: `PostgresHouseVoteTests.ReplayingTheMembersOfAVoteAddsNoSecondCandidate`.
- A detail alone makes no candidate. Enforced by `PostgresCollectionJobStore.AdmitMembersAsync`. Test: `PostgresHouseVoteTests.NoCandidateExistsUntilTheMembersOfTheVoteAreCaptured`.
- A roll call without legislation still gets a candidate. Enforced by `PostgresCollectionJobStore.AdmitMembersAsync`. Test: `PostgresHouseVoteTests.ARollCallWithoutLegislationStillBecomesACandidateBecauseNoRelevanceGateExists`.
- Each job URL has the exact form of its resource kind. Enforced by `HouseVoteListUrl`, `HouseVoteRollUrl`, and `CollectionJobDefinition.Validate`. Test: `HouseVoteSourceConfigurationTests.AJobUrlMustHaveTheExactFormOfItsResourceSoTheWorkerNeverReadsItAsAnotherKind`.
- A next link stays in the source scope and the session, advances the offset, and carries no `api_key`. Enforced by `HouseVoteListUrl.ReadNext`. Test: `HouseVoteListUrlTests.NextLinksCarryingACredentialAreRejectedSoNoKeyIsStoredInAJobDefinition`.
- A malformed response is invalid data and blocks preparation. Enforced by the three parsers. Test: `JobPreparationTests.AMalformedVoteResponseIsInvalidDataSoTheWorkerBlocksThePermanentFailure`.
- A total is a party total or a candidate total, never both or neither. Enforced by `HouseVoteParser`. Test: `HouseVoteParserTests.ATotalMustBeExactlyOneOfAPartyTotalOrACandidateTotalWithACountSoNoCountIsMisfiled`.
- A response for another roll call than the job URL is invalid. Enforced by `HouseVoteWire.ToVote`. Test: `HouseVoteParserTests.ADetailOfAnotherRollCallThanTheJobRequestedIsInvalid`.
- The sweep covers every session from `backfillFrom` to today, also across a year boundary. Enforced by `HouseVoteSession`. Test: `HouseVoteSessionTests.TheSweepFollowsSessionByStepUntilTheSessionOfToday`.
- Vote admission never defers a job; a deferral fails the transaction. Enforced by `PostgresCollectionJobStore`. Test: `PostgresHouseVoteTests.AFullPageWithItsNextPageJobFitsTheBudgetButOneMoreJobIsAnInvalidDeferralThatSavesNothing`.
- The shipped source budgets hold a full page and its next-page job. Enforced by `CollectionConfiguration.Validate`. Test: `PostgresHouseVoteTests.ShippedSourceBudgetsAdmitAFullPageAndItsNextPage`.

## Behavior

### Source configuration

One `api` source with `apiParser` set to `congressHouseVoteList` follows the House votes. It uses the same fields as the bill source: `credential`, `minimumCongress`, and `backfillFrom`. It has no `coverage`, and it covers every configured person with a `bioguideId` from `backfillFrom` on.

`CollectionConfiguration.CoveredPersonIdsOf` owns this coverage for both Congress.gov parsers. `ListedPersonIdsOf` returns no people for the source.

The shipped source `congress-house-votes` sets `backfillFrom` 2025-01-03 and `minimumCongress` 119. It ships with `enabled: false`. Enable steps are in [operations](../operations.md#congressgov-house-vote-stream).

### Daily sweep

The due check of the schedule creates one list job for `HouseVoteSession.FirstOfSweep`. That is the session of `backfillFrom`, or the first session of `minimumCongress` when that is later. No job is created when the session starts after today. The check waits one interval.

A scheduled check does not start while a list job of the source is active. The source checks once a day.

The list URL is `{source url}/{congress}/{session}?format=json&limit=250`, with `offset` on later pages. A list page job admits:

- one detail job for each roll call on the page,
- the next page of the same session, when the page has a `next` link,
- otherwise the first page of the following session, when that session is not after the session of today.

The next-page job key is `house-vote/{source}/page/{list job ID}`, for both kinds of next page.

Paging is by offset. A vote updated during a sweep can move and push another vote past the reader. The next daily sweep reads the list again, so a missed vote appears one day late at most.

### Resource kinds and URLs

A job definition of a vote source carries `apiResource`. The kinds are `houseVoteList`, `houseVoteDetail`, and `houseVoteMembers`. A kind needs a source with `apiParser` `congressHouseVoteList` and a bound configuration revision.

The configuration binding check requires the exact URL form of each kind:

- `houseVoteList`: `{source url}/{congress}/{session}` with `format=json`, `limit=250`, and optionally one `offset` of 0 or more (`HouseVoteListUrl`).
- `houseVoteDetail`: `{source url}/{congress}/{session}/{roll}?format=json` (`HouseVoteRollUrl`).
- `houseVoteMembers`: `{source url}/{congress}/{session}/{roll}/members?format=json` (`HouseVoteRollUrl`).

Numbers must be canonical, the session must be 1 or 2, and the roll must be 1 or more. Any other or repeated parameter, including `api_key`, fails. The parser builds the detail URL itself and ignores the item `url`.

### Parsing

Each succeeded job goes through [preparation](collection.md#preparation). The parsers read the capture through `ICaptureBodyReader`. A malformed response is invalid data, so preparation blocks the job.

| Parser | Version | Reads |
|---|---|---|
| `HouseVoteListParser` | `congress-house-vote-list-v1` | One list page of at most 250 items |
| `HouseVoteParser` | `congress-house-vote-v1` | One detail |
| `HouseVoteMembersParser` | `congress-house-vote-members-v1` | One members response, with at least one member |

`HouseVoteRenderer` renders the parsed detail and members. Its normalization version is `house-vote-render-v1`.

A list item needs `congress`, `sessionNumber`, `rollCallNumber`, and `updateDate`. Its session must be the session of the page. A detail and a members response need the same roll call as the job URL and an `updateDate`.

The API covers the 118th and 119th Congresses. Each member entry gives `bioguideID`, the name, `voteParty`, `voteState`, and `voteCast`. A member needs `bioguideID` and `voteCast`. The detail parser keeps the question, vote type, result, start time, legislation, party totals, candidate totals, and source data URL. A total is a party total or a candidate total. A party total needs a party. A candidate total needs a candidate and a `total` of 0 or more. The parser ignores other counts on a candidate total. Any other total is invalid. One response can mix both kinds.

An unknown legislation type, such as an amendment, gives no bill identity whatever its number. A known type needs a number of 1 or more. A number without a type is invalid.

### Extractions

The text has one fact on each line, so a citation can quote one line. The extraction parser version is the `Version` of the parser. The source attempt is the captured attempt of the job. Optional facts that the API omits have no line. Line breaks inside a value become spaces. The `Legislation` line keeps the API spelling, such as `HCONRES`, and the vote row stores the lowercase bill type. `RenderedText` owns the line rules for bills and votes.

Detail, with party totals in party order and candidate totals in candidate order:

```
House vote: 119 1 100
Updated: 2025-06-24T12:34:41Z
Started: 2025-04-10T15:08:00Z
Question: On Motion to Concur in the Senate Amendment
Vote type: Yea-and-Nay
Result: Passed
Legislation: HCONRES 14
Party total D: Yea 0, Nay 212, Present 0, Not voting 1
Party total I: Yea 0, Nay 0, Present 0, Not voting 0
Party total R: Yea 216, Nay 2, Present 0, Not voting 2
Source data: https://clerk.house.gov/evs/2025/roll100.xml
```

Members, one line each, in bioguide ID order:

```
House vote members: 119 1 100
Updated: 2025-06-24T12:34:41Z
Member: A000055, Robert Aderholt, R, AL, Yea
```

### Admission

`IHouseVoteAdmissionStore` admits one list page, one detail, or one members response in one transaction. It shares `AdmitApiJobsAsync` with the [bill stream](bills.md#admission). The budgets come from `admissionPolicy` through `DiscoveryAdmissionPolicy`.

For each list item, the store admits one detail job. A known key is a duplicate and is skipped. A page of 250 items plus the next page needs 251 jobs. `CollectionConfiguration.Validate` rejects a vote source whose policy cannot reserve them.

A detail job saves its extraction first. It then records the vote version and the extraction in `house_votes` and admits one members job. The extraction must belong to an attempt of the job. A second detail job of the same version records nothing and admits nothing.

A members job finds the `house_votes` row by source, roll call, and exact update time. A missing row is invalid data. This covers a vote that changed between the two fetches. The members of that newer version then wait for its own detail job.

When the row is found, the store writes one candidate in `house_vote_candidates`. The candidate copies the vote identity, the bill identity, and the detail extraction of the row. It adds the members extraction, which must belong to an attempt of the members job. A replay finds the candidate and writes nothing.

### Job keys

`HouseVoteJobKeys` owns the keys. A replayed preparation finds the known keys and creates no job.

| Job | Key |
|---|---|
| Detail | `house-vote/{source}/vote/{congress}/{session}/{roll}/{updateDate}` |
| Members | `house-vote/{source}/members/{congress}/{session}/{roll}/{updateDate}` |
| Next list page | `house-vote/{source}/page/{list job ID}` |

The update date is the exact UTC time as `yyyy-MM-ddTHH:mm:ssZ`. `BillStreamJobKeys.ExactTime` owns the format.

## Storage and contracts

- The generated [votes schema](schema/votes.md) lists every table, column, key, check, and index.
- `house_votes` holds one row for each vote version, keyed by its detail job. The row stores the identity, the exact update time, and the nullable bill identity. It also names the extraction of the detail. The store reads it to find duplicates and to write the candidate.
- `house_vote_candidates` holds one row for each vote version with both pages captured. It names the detail job, the members job, both extractions, and the configuration revision. Drafting will read these rows.
- The candidate ID is a SHA-256 hash of the source, the roll call, and the exact update time. `PostgresCollectionJobStore.HashHouseVoteCandidate` builds it. It frames the text like the bill candidate ID, with the prefix `house-vote:`.
- The store writes no page rows. The two extractions and the captures of the two jobs are the evidence of a candidate.

## Code map

| Concern | Files |
|---|---|
| Sessions and the sweep | `src/CivicLens.Application/Collection/HouseVotes/HouseVoteSession.cs`, `src/CivicLens.Application/Collection/HouseVotes/HouseVoteRoll.cs` |
| URLs and keys | `src/CivicLens.Application/Collection/HouseVotes/HouseVoteListUrl.cs`, `src/CivicLens.Application/Collection/HouseVotes/HouseVoteRollUrl.cs`, `src/CivicLens.Application/Collection/HouseVotes/HouseVoteJobKeys.cs` |
| Rendering | `src/CivicLens.Application/Collection/HouseVotes/HouseVoteRenderer.cs`, `src/CivicLens.Application/Collection/RenderedText.cs` |
| Parsers | `src/CivicLens.Application/Collection/HouseVotes/HouseVoteListParser.cs`, `src/CivicLens.Application/Collection/HouseVotes/HouseVoteParser.cs`, `src/CivicLens.Application/Collection/HouseVotes/HouseVoteMembersParser.cs`, `src/CivicLens.Application/Collection/HouseVotes/HouseVoteWire.cs` |
| Resource kinds and parser list | `src/CivicLens.Application/Collection/ApiResource.cs`, `src/CivicLens.Application/Collection/ApiParser.cs` |
| Configuration and job binding | `src/CivicLens.Application/Collection/CollectionConfiguration.cs`, `src/CivicLens.Application/Collection/ConfiguredCollectionSource.cs`, `src/CivicLens.Application/Collection/Jobs/CollectionJobDefinition.cs` |
| Preparation | `src/CivicLens.Application/Collection/Processing/JobPreparation.cs` |
| Admission port | `src/CivicLens.Application/Collection/HouseVotes/IHouseVoteAdmissionStore.cs` |
| Postgres admission and candidates | `src/CivicLens.Infrastructure/Collection/Jobs/PostgresCollectionJobStore.HouseVotes.cs`, `src/CivicLens.Infrastructure/Collection/Jobs/PostgresCollectionJobStore.Scheduling.cs` |
| Table rows | `src/CivicLens.Infrastructure/Collection/HouseVotes/` |

## Limits and planned work

- Planned ([R-15](../roadmap.md#next)): draft one vote record for each candidate. Votes are a new record kind with their own tables, store, and review pages. One record covers one roll call and lists the vote of every tracked member. They reuse the shared analysis, review, and publication machinery.
- Planned: code supplies every fact of a vote record: member votes, totals, result, date, and the bill link, each cited. The model writes only the headline and a plain summary of the question.
- Planned: a vote record keeps the bill identity, so it links to the action record of that bill.
- Planned: measure the noise of drafting every roll call before adding a gate. The 119th Congress had 362 roll calls in session 1 and 314 so far in session 2 on 2026-10-16.
- The `house-vote` API is a beta. Its shape can change. A changed shape makes a parser raise invalid data, which blocks the job.
- A roll call with an unknown legislation type has no bill link, so it does not link to an action record.
- A members response that is newer than the recorded detail blocks its job until the next sweep admits the newer detail.
- The session of today uses the UTC date. The new session therefore starts a few hours early on 1 January in US time zones. Its list is empty then.
- Senate votes need senate.gov data and a `lis_member_id` crosswalk. See [actions](actions.md#limits-and-planned-work).
