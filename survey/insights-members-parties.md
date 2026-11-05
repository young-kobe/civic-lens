# Survey: "Your three members" and "Parties agree count"

This survey is a design proposal. It changes no product code. The owner chose two reader insights for regular citizens. This file gives the facts, the options, and a recommendation for each decision. The owner decides. Then the PRs in section 4 start.

Rules for the page text in this survey:

- The target reading grade is 8.
- Every public number says who it counts, what it counts, and its denominator.
- No bare ratio. No percent without its count. No composite score or grade.

Marks: **[unverified]** means that I did not check the fact in code, in data, or in production. The sandbox has no production access, so every production number is from the owner.

## 1. What exists now

### 1.1 The ZIP lookup (PR #374)

- Route: `GET /v1/zip-districts/{zcta}` in `src/CivicLens.PublicApi/ZipDistricts/ZipDistrictEndpoints.cs:17`. It sets `Cache-Control: public, max-age=3600` (`ZipDistrictEndpoints.cs:12`).
- Store: `ZipDistrictStore` is the one owner of the join from a ZCTA to the seat holders (`src/CivicLens.PublicApi/ZipDistricts/ZipDistrictStore.cs:8`). It reads both queries in one `RepeatableRead` transaction (`ZipDistrictStore.cs:51`), so the districts and the senators come from one release.
- Current term rule: `ZipDistrictStore.cs:14`. The House query selects `t.official_id, o.name, t.party` from the newest current term (`ZipDistrictStore.cs:25-28`). The Senate query uses `DISTINCT ON (t.state, t.official_id)` and already reads `t.end_on` for its order (`ZipDistrictStore.cs:36-40`), but it does not return it.
- Answer: `ZipDistrictAnswer`, `ZipDistrict`, `ZipState`, and `SeatHolder(OfficialId, Name, Party)` (`src/CivicLens.PublicApi/ZipDistricts/ZipDistrictAnswer.cs:5-13`; `SeatHolder` is at line 13). No term date is in the answer.
- Home page form and panel: `src/CivicLens.Host/Publication/PublishedHomePage.razor:18-23` (form with `data-zip-api`, `data-official-page`, `data-states`, `data-house-offices`) and `PublishedHomePage.razor:33-39` (panel "Your officials", Census source note, ZCTA note).
- Script: `initZipLookup` in `src/CivicLens.Host/wwwroot/js/public.js:175-253`. `seat()` builds one `li.zip-seat` with the seat title and "Name, Party" (`public.js:196-207`). `showAnswer` lists districts, then senators (`public.js:209-222`). `formatDay` gives "5 May 2026" (`public.js:3-6`), the same form as `Moment.Day` (`src/CivicLens.Host/Components/Ui/Moment.cs:11`).
- Styles: `.zip-seats`, `.zip-seat`, `.zip-count` in `src/CivicLens.Host/wwwroot/css/public.css:60-63`.
- Docs: `docs/architecture/search.md:202` (route row), `docs/architecture/districts.md` (view and source).

### 1.2 Votes in the public_api views (PRs #360, #365, #375)

- `public_api.votes`: one row for each active vote. It has `chamber`, `congress`, `session_number`, `roll_call_number`, `voted_on`, `kind`, `bill_type`, `bill_number`, `headline`, `label`, `question`, `result`, and four counts (`src/CivicLens.Infrastructure/Collection/Migrations/20261105005544_VoteQuestionResultColumns.cs:40-64`, `docs/architecture/public-api-columns.txt`).
- `public_api.vote_members`: `record_id`, `revision_number`, `position`, `official_id`, `vote_cast` (`src/CivicLens.Infrastructure/Collection/Migrations/20261103165431_PublicApiViews.cs:50-55`). The official ID is the configured ID, the same as `PublishedOfficial.Id`.
- The member list holds **tracked members only**. `VoteReader` puts only covered people in the record (`docs/architecture/votes.md:110`, test `VoteDraftStepTests.MembersWhomNobodyTracksNeverEnterTheRecordBecauseOnlyTrackedVotesAreCited`).
- No view holds a party, a party majority, or an alignment. Party majorities exist only in the release JSON. `VoteTally.Read` computes them at publish from every member line and the roster party on the vote day (`src/CivicLens.Application/Publication/VoteTally.cs:30-49`).
- `VoteStore` serves `/v1/votes`, `/v1/votes/{recordId}`, and `/v1/votes/{recordId}/members` (`src/CivicLens.PublicApi/Votes/VoteStore.cs:28`, `:46`, `:60`). The list order is `voted_on DESC, chamber, session_number DESC, roll_call_number DESC, record_id` (`VoteStore.cs:16`). The `kind` filter takes one kind and passes `kind.ToString()` (`VoteStore.cs:128`). The members route filters by cast, not by official.
- Doc drift: `docs/architecture/search.md:208` still says "The members of a vote are not served yet". The route row above it says they are. A later PR should delete the stale sentence.

### 1.3 Release-bound names (PR #370)

- `public_api.release_officials` and `public_api.release_official_terms` show the names and terms of the active release. A term row has `kind`, `state`, `district`, `party`, `caucus`, `start_on`, and `end_on` (`docs/architecture/schema/publication.md:155-172`). `end_on` can be null.

### 1.4 The vote rules

- Substantive kinds: `PublishedVoteKindExtensions.IsSubstantive` is the one owner (`src/CivicLens.Publication.Contracts/PublishedVoteKindExtensions.cs:5`). The kinds are passage, suspension, amendment, conference report, veto override, and nomination (`docs/architecture/publication.md:280`).
- Party alignment: `PublishedVote.IsSplit` and `PublishedVote.PartiesAgree` (`src/CivicLens.Publication.Contracts/PublishedVote.cs:75-84`). Both need a substantive kind and a majority for both the Democrats and the Republicans. A tie gives no majority.
- Entry alignment: `PublishedVote.AlignmentOfParties` sets `Split`, `PartiesAgree`, or `Mixed` on the manifest entry, only for a substantive vote with totals and both major parties (`PublishedVote.cs:104-111`, enum `src/CivicLens.Publication.Contracts/PublishedVoteAlignment.cs`).
- Badge rule: `PublishedVoteView.Badge` gives "Both parties agreed" (`src/CivicLens.Host/Publication/PublishedVoteView.cs:93`, `:127`), with the definition `PublishedOfficialView.PartiesAgreedMeaning` (`src/CivicLens.Host/Publication/PublishedOfficialView.cs:239`). Doc: `docs/architecture/publication.md:299`.
- Existing count of agreed votes: `FacetSummary.From` counts `Split`, `PartiesAgree`, and the rest from entry alignments (`src/CivicLens.Application/Publication/FacetSummary.cs:10-18`). The issue page shows "N of them were votes where both parties agreed" (`src/CivicLens.Host/Publication/PublishedFacetSummaryView.cs:35-36`) and the note "That the parties voted the same way or on opposite sides is about the question of the vote. It does not show a position on the issue." (`PublishedFacetSummaryView.cs:45`). This is the direct precedent for Insight C.
- Vote kind labels: `PublishedRecordLabels.VoteKind` (`src/CivicLens.Host/Publication/PublishedRecordLabels.cs:148-163`). The method page lists the substantive kinds from the owner (`src/CivicLens.Host/Publication/PublishedMethodPage.razor:71-72`).
- Cast words: `PublishedOfficialVote.SideOf` maps Aye to Yea and No to Nay (`src/CivicLens.Publication.Contracts/PublishedOfficialVote.cs:37-42`). `PublishedRecordLabels.CastTermKey` maps each cast to its glossary term (`PublishedRecordLabels.cs:191-201`). The vote strip legend says "Yes" and "No" (`src/CivicLens.Host/Publication/PublishedVoteStrip.razor:17-18`).

### 1.5 Board pages and home picks (R-47)

- Boards: `OfficialBoard.All` gives four pages, one for each chamber and party (`src/CivicLens.Application/Publication/OfficialBoard.cs`). `OfficialBoardPage` picks one Congress for each chamber (`src/CivicLens.Application/Publication/OfficialBoardPage.cs:6-13`, `:56-64`). The page is `src/CivicLens.Host/Publication/PublishedBoardPage.razor`.
- Closest vote card: `ClosestVote.From` reads manifest entries with an alignment (`src/CivicLens.Application/Publication/ClosestVote.cs:17-26`). It is a chamber-wide fact shown on a party board, with its pool stated (`src/CivicLens.Host/Publication/PublishedBoardView.cs:41-43`).
- Home vote list: `HomeHighlights.SubstantiveVotes` takes the 2 newest entries with an alignment in each chamber (`src/CivicLens.Application/Publication/HomeHighlights.cs:12`, `:22-27`). So the home page uses "substantive and both parties voted", not the kind alone. Doc: `docs/architecture/publication.md:267`.
- Method page sections: "The voting boards", "Substantive votes" (no `Id` yet), "The picks on the home page" (`Id="home-picks"`), "Vote badges in lists" (`Id="vote-badges"`) (`PublishedMethodPage.razor:28-54`).

### 1.6 Manifest entry fields

- A vote entry may hold `yeaCount`, `nayCount`, `alignment`, and `chamber` (`src/CivicLens.Publication.Contracts/PublishedRecordEntry.cs:27-30`, `docs/architecture/publication.md:254`). It holds **no vote kind and no Congress**.
- `releases publish --rewrite-entries` rebuilds carried vote and action entries with `ToEntry` (`docs/architecture/publication.md:259`). `ReleaseEntryComparison` lists the fields that a rewrite may change (`src/CivicLens.Application/Publication/ReleaseEntryComparison.cs:13`).
- The `published_entries` table holds "no name and no vote figure", so a rewrite cannot conflict (`docs/architecture/publication.md:80`).

## 2. The insights

### 2.1 Insight A: "Your three members"

#### Data needed and where it lives

| Fact | Source | Read path |
|---|---|---|
| Seat holders | `release_official_terms`, `release_officials` | view, already read by `ZipDistrictStore` |
| Term end date | `release_official_terms.end_on` | view, same row as the seat |
| The 4 newest substantive votes of each chamber | `public_api.votes` (`chamber`, `kind`, `voted_on`, `session_number`, `roll_call_number`) | view |
| Title, label, result, bill | `public_api.votes` (`headline`, `label`, `result`, `bill_type`, `bill_number`) | view |
| The cast of each member | `public_api.vote_members` (`official_id`, `vote_cast`) | view |
| Kind label text | `PublishedRecordLabels.VoteKind` in Host | static page data attribute |

No new view is needed. No new column is needed. The substantive kinds go into the SQL as a parameter that C# builds from `IsSubstantive`, so the rule keeps one owner. Precedent: `VoteStore.cs:128` passes the kind enum as text, and `PublishedMethodPage.razor:71` lists the kinds from the owner.

#### Read path

The R-68 direction is views, one endpoint, and one shared component. This insight uses the existing views. It adds one endpoint (decision D-A1) and one shared script component in `public.js`.

Proposed endpoint (recommended option of D-A1):

`GET /v1/member-votes?official={id}&official={id}...`

- Takes 1 to 8 official IDs. A ZCTA can lie in up to 4 districts in up to 2 states, so 4 representatives and 4 senators is the most.
- Reads, in one `RepeatableRead` transaction: the current term of each official (to find the chamber), the 4 newest active substantive votes of each chamber that the officials sit in, and the casts of those officials on those votes.
- Answer:

```json
{
  "release": 412,
  "chambers": [
    { "chamber": "house", "votes": [
      { "recordId": "…", "votedOn": "2026-10-30", "congress": 119, "session": 2, "rollCall": 512,
        "kind": "passage", "billType": "hr", "billNumber": 1234, "headline": "…", "label": "An AI summary of the official record", "result": "Passed" } ] }
  ],
  "members": [ { "officialId": "…", "chamber": "house", "casts": [ { "recordId": "…", "cast": "Yea" }, { "recordId": "…", "cast": null } ] } ]
}
```

- `cast` is null when the vote has no member row for the official. That happens when the member was not in office, or was not tracked on that day.
- An official with no current Senator or Representative term is left out of `members`.
- 400 for zero IDs, more than 8 IDs, or a blank ID. 503 when no release is active. `Cache-Control: public, max-age=3600`, the same as the ZIP route.
- Store: `MemberVoteStore` in `src/CivicLens.PublicApi/Votes/`. It reuses `ViewColumns` and the order of `VoteStore.cs:16`.

#### Contract changes

1. `SeatHolder` gets `TermEndsOn` (`DateOnly?`), from `end_on` of the same term row that the store already selects. Additive JSON field `termEndsOn`. Both SQL queries in `ZipDistrictStore` add `t.end_on`.
2. New answer records `MemberVoteAnswer`, `ChamberVotes`, `RecentVote`, `MemberCasts`, `MemberCast` in PublicApi. Records for wire data, as AGENTS.md requires.
3. No change to Publication.Contracts. No change to any view. `public-api-columns.txt` does not change.
4. The home page form gets two new data attributes: `data-vote-kinds` (the `VoteKind` label of each kind, keyed by its release text form) and `data-record-page` (`PublicationProtocol.RecordPagePath("{id}")`). Precedent: `data-states` and `data-official-page` at `PublishedHomePage.razor:18`.

#### Page placement and layout

Placement: in the existing "Your officials" panel on the home page, below the seat list. Each seat line gets one term line. A new block "How they voted" follows the seat list (decision D-A2).

At 1280 px:

```
Your officials
  Representative for Ohio, district 3
  Joyce Beatty, Democrat
  Current term ends 3 Jan 2027.

  Senator for Ohio                        Senator for Ohio
  ...                                     ...

How they voted
  House: the 4 newest substantive votes on this site
  | Vote                                   | Joyce Beatty |
  | H.R. 1234 headline (link)              | Yes (Yea)    |
  | Passage, 30 Oct 2026. Passed.          |              |
  ...
  Senate: the 4 newest substantive votes on this site
  | Vote                                   | Senator A | Senator B |
  ...
```

At 360 px: each table row stacks. The vote title is first. The kind, date, and result are on the next line. Each member cast is one line below, as "Joyce Beatty: Yes (Yea)". Precedent: the narrow record tables put the date under the text (PR #372, commit `8c443ef9`). No horizontal scroll. A ZCTA with 4 districts gives a House table with 4 member columns at 1280 px. That fits in the panel width **[unverified, check on a real ZCTA such as one that spans 4 districts]**.

#### Proposed visible strings (for owner approval)

| Where | String |
|---|---|
| Term line | `Current term ends {d MMM yyyy}.` |
| Term line, no end date | `Our records show no end date for this term.` |
| Block heading | `How they voted` |
| Chamber heading | `House: the 4 newest substantive votes on this site` / `Senate: the 4 newest substantive votes on this site` |
| Chamber heading, fewer votes | `House: the {n} newest substantive votes on this site` / `House: the newest substantive vote on this site` |
| No votes in a chamber | `This site has no substantive House vote yet.` |
| Vote facts line | `{Kind label}, {d MMM yyyy}. {Result}.` |
| Cast, Yea or Aye | `Yes ({source word})` |
| Cast, Nay or No | `No ({source word})` |
| Cast, Present | `Present` |
| Cast, Not Voting | `Did not vote` |
| Cast, candidate name | `Voted for {name}` (not expected on a substantive vote) |
| No member row | `No vote on record` |
| Note 1 | `A substantive vote is a vote on a bill, an amendment, or a nomination. Votes on rules and other steps are left out.` |
| Note 2 | `"No vote on record" means this site has no vote by this member on that roll call. The member may not have been in office yet.` |
| Note 3 | `A vote title can be an AI summary of the official record. The vote page says so.` |
| Note link | `How we pick these votes` (to `method.html#your-officials`) |
| Vacant seat | existing: `No current member is in our records.` (no votes block row) |

Note 1 is a plain summary. The exact kind list stays on the method page from the owner.

#### Method note text (new panel `Id="your-officials"` on `method.html`)

> **Your officials**
>
> The ZIP lookup uses a Census Bureau file. It maps each ZIP Code Tabulation Area to the House districts of the {congress} Congress. A ZIP Code Tabulation Area is not a USPS ZIP code.
>
> For each member, the page shows the end date of the current term. The date comes from the public member roster. It is the scheduled end of the term. A member can leave office before it. The page makes no claim about an election.
>
> The page shows the 4 newest substantive roll call votes of each chamber on this site. A vote is substantive when its kind is one of these: {SubstantiveKinds}. Code picks the votes by date. When two votes have the same date, the later roll call comes first. No model picks them. All members of a chamber see the same 4 votes.
>
> Each member shows the cast from the official record: Yea, Aye, Nay, No, Present, or Not Voting. Aye counts as Yes and No counts as No. "No vote on record" means that this site has no vote by the member on that roll call. This can be because the member was not in office, or because this site did not follow the member on that day.

`{SubstantiveKinds}` comes from `IsSubstantive` and `VoteKind`, as at `PublishedMethodPage.razor:71`. The "Substantive votes" panel gets `Id="substantive-votes"` so the note can link it.

#### Coverage limits shown

- The 4 votes are the newest on this site, not in Congress. A vote that this site did not collect is missing.
- Only tracked members have a cast row. An untracked member shows "No vote on record" **[unverified: count of untracked current members; the coverage file suggests all members are configured]**.
- A Delegate or the Resident Commissioner usually has no row on these roll calls **[unverified: check one House member list for DC]**. The note 2 wording covers this without a claim about House rules.
- The answer can be up to one hour old, as the ZIP answer is.

#### Tests

- `tests/CivicLens.Tests/PublicApi/Votes/MemberVotesEndpointsTests.cs` with `ViewsSeed`:
  - `OnlySubstantiveKindsAreListedSoARuleVoteNewerThanAPassageVoteIsLeftOut`
  - `EachChamberListsItsFourNewestSubstantiveVotesNewestFirstByDateThenRollCall`
  - `AMemberWithNoRowOnAVoteGetsANullCastAndNotAGuessedOne`
  - `AnOfficialWithNoCurrentChamberTermIsLeftOut`
  - `NineOfficialIdsAreRefusedAndZeroAreRefused`
  - `TheAnswerNamesTheReleaseAndIsCachedForOneHour`
  - `TheSubstantiveKindParameterEqualsIsSubstantiveSoTheRuleHasOneOwner` (enumerates `PublishedVoteKind`)
- `ZipDistrictEndpointsTests`: `ASeatHolderNamesTheEndOfTheCurrentTerm`, `ATermWithNoEndDateGivesANullEnd`.
- `PublishedHomePageTests`: `TheZipFormCarriesTheVoteKindLabelsFromTheirOwnerAndTheRecordPageTemplate`.
- `ReleaseRendererTests`: `TheMethodPageNamesTheSubstantiveKindsInTheYourOfficialsNote`.
- Architecture tests stay green: PublicApi references only Publication.Contracts and Npgsql.
- Script: no automated test covers `public.js` today. A manual check at 360 and 1280 px, with screenshots on the PR, covers the layout. See risk R6.

### 2.2 Insight C: "Parties agree count"

#### Data needed and where it lives

| Fact | Source today | Read path today |
|---|---|---|
| Alignment of each vote (`Split`, `PartiesAgree`, `Mixed`) | manifest entry `alignment`, from `PublishedVote.AlignmentOfParties` | release file only |
| Chamber of each vote | manifest entry `chamber` | release file only |
| Vote kind | `PublishedVote.Kind` in the record file; `vote_revisions.kind` in the view | not on the entry |
| Congress | `PublishedVote.Congress` in the record file | not on the entry |

The party majorities need the roster party of every member on the vote day, including untracked members (`VoteTally.cs:35`, `:44-47`). No table stores them. So a view cannot count agreement today without a new release-bound table (decision D-C3).

#### Read path

Recommended: the release build, from manifest entries, like `FacetSummary` and `ClosestVote`. The count changes only when a release changes. The board pages are static. R-68 does not list the board pages for the API rebuild.

New Application type `ChamberAgreement` (in `src/CivicLens.Application/Publication/`):

- Input: the release entries and the board Congress of the chamber (`OfficialBoardPage.Congress`).
- Counts, for each chamber, the vote entries of that Congress with an alignment. That is the denominator (decision D-C1).
- Splits the count by `alignment` and by vote kind.
- Reads only `entry.Alignment`, so `PublishedVote.PartiesAgree` stays the one owner of party agreement.

#### Contract changes

1. `PublishedRecordEntry` gets two optional vote fields: `voteKind` (`PublishedVoteKind?`) and `congress` (`int?`). `PublishedVote.ToEntry` sets both. Schema version stays 1, as for the R-47 fields.
2. `ReleaseEntryComparison.cs:13` adds `VoteKind = null, Congress = null`, so `--rewrite-entries` may fill them.
3. The operator runs `releases publish <key> --rewrite-entries` once after deploy. Until then, carried entries have no kind (decision D-C4).
4. `published_entries` does not change. It keeps "no vote figure".
5. `IReleaseRenderer.RenderBoardAsync` gets the `ChamberAgreement` of the chamber, beside `ClosestVote` (`src/CivicLens.Application/Publication/IReleaseRenderer.cs:27`).

#### Page placement and layout

Recommended placement (decision D-C2): one panel on each board page, below the closest vote card. Both party pages of a chamber show the same panel. It is a chamber fact, like the closest vote card. One method panel explains it.

At 1280 px: a panel with one lead sentence, then a two-column table: "Kind of vote" and "Both parties agreed". The rows are the substantive kinds that have votes. The last row is the total.

At 360 px: the same table. Two short columns fit at 360 px. The kind label wraps.

```
When both parties agreed (119th Congress, House)
  On 195 of the 449 substantive House roll call votes on this site,
  most Democrats and most Republicans voted the same way.

  | Kind of vote             | Both parties agreed |
  | Passage                  | 40 of 61            |
  | Suspension of the rules  | 120 of 170          |
  | Amendment                | 30 of 200           |
  | ...                      |                     |
  | All substantive votes    | 195 of 449          |

  On 240 of the 449, the two parties took opposite sides.
  On 14 of the 449, one party had no majority, as in a tie.
```

The numbers above are layout samples only. Only the totals 195 of 449 (House) and 30 of 385 (Senate) are owner facts, and their denominator rule is **[unverified]** (see D-C1).

#### Proposed visible strings (for owner approval)

| Where | String |
|---|---|
| Panel title | `When both parties agreed ({ordinal} Congress, {House or Senate})` |
| Lead | `On {a} of the {n} substantive {House or Senate} roll call votes on this site, most Democrats and most Republicans voted the same way.` |
| Lead, one vote | `On {a} of the 1 substantive {chamber} roll call vote on this site, ...` |
| Table caption | `Votes where both parties agreed, by kind of vote` |
| Column 1 | `Kind of vote` |
| Column 2 | `Both parties agreed` |
| Cell | `{a} of {n}` |
| Total row | `All substantive votes` |
| Split line | `On {s} of the {n}, the two parties took opposite sides.` |
| Mixed line | `On {m} of the {n}, one party had no majority, as in a tie.` (hidden when 0) |
| Definition | existing `PublishedOfficialView.PartiesAgreedMeaning` |
| Caution | existing `PublishedFacetSummaryView.PartiesNote`, adapted: `That the parties voted the same way is about the question of each vote. It does not show that they agree on an issue.` |
| Nomination note (Senate only) | `Most Senate roll call votes are on nominations. Look at each kind of vote, not only the total.` (shown when nominations are more than half of `n`) |
| No votes | `This site has no substantive {chamber} roll call vote with both parties in the {ordinal} Congress.` |
| Kind not known | `{k} of the {n} votes are from an older release and have no kind yet.` (see D-C4) |
| Link | `How this count works` (to `method.html#parties-agreed`) |

#### Method note text (new panel `Id="parties-agreed"`)

> **When both parties agreed**
>
> Each voting board page shows how often most Democrats and most Republicans voted the same way in its chamber. Code counts the substantive roll call votes on this site in the Congress of the board. No model is used.
>
> A vote counts in the total when its kind is substantive and both the Democrats and the Republicans voted in it. The two parties agreed when the majority of each party, among members who voted Yea or Nay, was on the same side. Present and Not Voting do not count toward a majority. A tie in a party gives that party no majority, so that vote is in the total but in neither group.
>
> Each member counts with the party of the roster on the day of the vote. An independent who caucuses with a party counts with that party.
>
> The page shows the count for each kind of vote. The kinds differ a lot. Many Senate nominations pass with votes from both parties, and many amendments split the parties. So the total alone can mislead.
>
> Agreement is about the question of each vote. It does not show that the parties agree on an issue. Votes by voice or by unanimous consent have no roll call, so this count leaves them out. This site does not hold every roll call vote.

#### Coverage limits shown

- Only roll call votes on this site. Voice votes and unanimous consent are out (R-69 "How it passed" will show them later).
- Only the board Congress of each chamber.
- A vote without a Democrat or Republican party row is out of the total.
- Carried entries before the rewrite have no kind (D-C4).

#### Tests

- `PublishedVoteTests.ToEntryNamesTheKindAndTheCongressOfTheVote`.
- `ReleaseEntryRewriteTests` (or the existing rewrite tests): `ARewriteFillsTheVoteKindAndCongressAndChangesNoOtherField`.
- `ChamberAgreementTests`:
  - `OnlyEntriesWithAnAlignmentCountSoAProceduralVoteNeverEntersTheTotal`
  - `AgreedPlusSplitPlusMixedEqualsTheTotal`
  - `TheCountReadsTheEntryAlignmentSoPartiesAgreeHasOneOwner`
  - `OnlyTheBoardCongressCounts`
  - `EachKindRowCountsItsOwnVotesAndTheRowsAddUpToTheTotal`
  - `AnEntryWithNoKindIsCountedInTheTotalAndNamedAsKindNotKnown`
- `PublishedBoardPageTests`: `TheAgreementPanelStatesEveryCountWithItsDenominator`, `TheSenateNominationNoteShowsOnlyWhenNominationsAreMoreThanHalf`, `NoPercentShowsInTheAgreementPanel`.
- `ReleaseRendererTests.TheMethodPageExplainsTheAgreementCountAndNamesTheSubstantiveKinds`.

## 3. Decisions for the owner

### D-A1: How the browser gets the casts

- Problem: the ZIP result needs the 4 newest substantive votes of each chamber and the cast of each member. No route gives this in one call.
- Options:
  1. A new endpoint `/v1/member-votes?official=...` with its own store.
  2. Extend `/v1/zip-districts/{zcta}` so each seat holder carries its casts.
  3. Reuse `/v1/votes` with a new `substantive=true` filter, and add an `official` filter to `/v1/votes/{id}/members`.
- Tradeoffs:
  1. One more request after the ZIP answer. The two answers can come from two releases (risk R3). The official page can reuse it later (R-68 "render the official page from the API"). `ZipDistrictStore` stays the owner of seats only.
  2. One request and one transaction, so one release. But `ZipDistrictStore` then owns seats and votes. That mixes two responsibilities. The cache key is the ZCTA, which is fine.
  3. Generic, but 2 list calls and up to 8 member calls for one lookup. Too many requests and too much rate limit use.
- Precedent checked: R-68 "a view, one endpoint, one shared component"; `/v1/votes/{recordId}/members` (#360) for a separate child route; `ZipDistrictStore.FindAsync` for one transaction over two reads.
- Recommendation: option 1. The script compares `release` of both answers. When they differ, it fetches the ZIP answer once more.

### D-A2: Layout of the casts

- Problem: two senators see the same 4 votes. Up to 4 representatives can share a ZCTA.
- Options:
  1. One table for each chamber: rows are votes, columns are members.
  2. One list under each member, which repeats the vote titles.
- Tradeoffs: option 1 shows each vote title once and puts members side by side. At 360 px it stacks. Option 2 is simpler to build but repeats each Senate title twice and makes the panel long.
- Precedent checked: `DataTable` with `Label` cells that stack on narrow screens; the narrow record tables of PR #372.
- Recommendation: option 1.

### D-A3: Cast words

- Problem: the source uses Yea, Nay, Aye, and No. Grade 8 readers know "Yes" and "No".
- Options:
  1. The source word only, as the vote page break list does.
  2. Plain words only, as the vote strip legend does.
  3. Plain word first, then the source word: "Yes (Aye)".
- Tradeoffs: 1 keeps fidelity but uses jargon. 2 is plain but hides the record word that the vote page shows. 3 is longer but keeps both.
- Precedent checked: `PublishedVoteStrip.razor:17-18` ("Yes", "No"); `PublishedOfficialVote.SideOf`; `docs/architecture/publication.md:299` (break rows show the source word).
- Recommendation: option 3. The mapping uses the `SideOf` rule, copied into the script with a test of the home page data (see risk R5).

### D-A4: Which substantive rule

- Problem: the home "Votes" list uses entries with an alignment (substantive kind, totals, and both major parties). The views know only the kind.
- Options:
  1. Kind only, from `IsSubstantive`.
  2. Add an alignment column to a view, so the rule matches the home page.
- Tradeoffs: 1 needs no new data. A substantive vote with no Democrat or Republican row would show in the ZIP block but not in the home list. That is rare **[unverified]**. 2 needs the party data that D-C3 discusses.
- Precedent checked: `TalkAndActionProfile.cs:40` counts by kind only; `HomeHighlights.cs:24` uses the alignment.
- Recommendation: option 1. The method note says "kind" and lists the kinds.

### D-A5: Term end wording

- Problem: the owner allows dates only, no election claims.
- Options: 1. "Current term ends 3 Jan 2027." 2. "Term ends 3 Jan 2027." 3. Show no term line when `end_on` is null.
- Recommendation: option 1, and the explicit null line "Our records show no end date for this term." The method note says the date is the scheduled end and makes no claim about an election.

### D-C1: The denominator

- Problem: "30 of 385" and "195 of 449" need one stated rule. Three rules are possible.
- Options:
  1. Every substantive vote by kind (includes votes with no party row and candidate votes).
  2. Substantive votes with an alignment: both parties voted, with totals. The total is agreed plus split plus mixed.
  3. Only agreed plus split (drop mixed).
- Tradeoffs: 1 counts votes where agreement cannot be known. 2 matches the home list and the closest vote card, and the mixed line explains the rest. 3 hides ties.
- Precedent checked: `HomeHighlights.cs:24`, `ClosestVote.cs:21`, `FacetSummary.cs:8` (it states the "other" group).
- Recommendation: option 2. The owner should confirm which rule gave 30 of 385 and 195 of 449 **[unverified]**.

### D-C2: Where the count shows

- Problem: the count is a chamber fact. The boards are per chamber and party.
- Options:
  1. A panel on each board page, the same on both party pages of a chamber.
  2. The officials page, beside the board links.
  3. A home card.
  4. The method page only.
- Tradeoffs: 1 puts it where party voting lives and follows the closest vote card. It shows the same panel twice per chamber. 2 is one place but the officials page is a list page. 3 adds to a busy home page that R-68 will rebuild. 4 hides it.
- Precedent checked: `PublishedBoardPage.razor:7-20` (closest vote card on every board); `src/CivicLens.Host/Publication/PublishedFacetPage.razor:28-55` (counts on issue pages).
- Recommendation: option 1, with the method panel. A home card can follow after the R-68 home rebuild.

### D-C3: Static count or view

- Problem: R-68 wants views. The party data is not in any table.
- Options:
  1. Static count in the release build from entries, with `voteKind` and `congress` on the entry.
  2. A release-bound table `release_vote_alignments (release_number, record_id, revision_number, alignment)` written in the publish transaction, a view, and an endpoint.
  3. Add `alignment` to `published_entries`.
- Tradeoffs: 1 is small and follows `FacetSummary`. It adds two entry fields and one operator rewrite. 2 follows the R-68 direction and the #370 name rows, but writes about one row per vote per release each hour, and the board page is static anyway. 3 breaks the rule that the entry rows hold no vote figure (`docs/architecture/publication.md:80`). A roster change could then make a rewrite conflict.
- Precedent checked: R-47 contract PR (entry fields plus `--rewrite-entries`); #370 release name tables; `published_entries`.
- Recommendation: option 1 now. Option 2 when R-68 moves the board pages to the API.

### D-C4: Carried entries with no kind

- Problem: until the operator runs `--rewrite-entries`, old vote entries have no `voteKind` and no `congress`.
- Options:
  1. Hide the panel until every vote entry has both fields.
  2. Count them in the total and state "{k} of the {n} votes are from an older release and have no kind yet."
  3. Derive the Congress from the date with `CongressCalendar.CongressOn`.
- Tradeoffs: 1 is clean but the panel can stay hidden if the rewrite waits. 2 shows the gap. 3 is wrong for votes on 1 to 3 January of an odd year **[unverified: check `CongressCalendar` at noon 3 January]** and gives no kind.
- Precedent checked: R-47 "A reader never guesses a default" (`docs/architecture/publication.md:254`); home list "left out until `--rewrite-entries` fills it" (`publication.md:267`).
- Recommendation: option 1 for a missing Congress, because the board Congress filter needs it. That matches the home list rule. The operator runs the rewrite in the deploy of the contract PR.

## 4. PRs in build order

| # | PR | Size | Precedent |
|---|---|---|---|
| 1 | ZIP term end: `termEndsOn` on `SeatHolder`, both SQL queries, the term line in `public.js`, tests, `search.md` route row, delete the stale sentence at `search.md:208` | S | #374 |
| 2 | Member votes endpoint: `MemberVoteStore`, `/v1/member-votes`, answer records, request parsing, tests, `search.md` API rows and invariants | M | #360 vote members route, `VoteStore` |
| 3 | ZIP casts on the home page: data attributes, "How they voted" block, CSS for 360 and 1280 px, method panel `your-officials`, `Id` on "Substantive votes", tests, `publication.md` | M | #374, #355 |
| 4 | Vote entry fields: `voteKind` and `congress` on `PublishedRecordEntry`, `ToEntry`, `ReleaseEntryComparison`, tests, `publication.md`, runbook line for the rewrite | S | R-47 contract PR |
| 5 | Parties agree panel: `ChamberAgreement`, renderer input, board panel, method panel `parties-agreed`, tests, `publication.md`, roadmap entry | M | `FacetSummary`, `ClosestVote` |

PRs 1 to 3 and PR 4 are independent. PR 5 needs PR 4 and the operator rewrite. Each PR updates its feature doc in place and removes the roadmap item when it is complete.

## 5. Risks

- R1. Query cost. `public_api.votes` computes the four counts with a lateral aggregate over the member list of each row. The view uses `security_barrier`. A query for the 4 newest votes of a chamber may compute the aggregate for many rows before the sort **[unverified]**. Run `EXPLAIN (ANALYZE, BUFFERS)` on a production-size copy in PR 2. If the cost is high, the owner decides between a narrow view without counts and an index. R-68 still has its traffic and load measure open.
- R2. Tracked members only. A member who was not tracked on a vote day shows "No vote on record". The method note says so. The size of this gap is **[unverified]**.
- R3. Two releases. With D-A1 option 1, the ZIP answer and the member votes answer can come from two releases. The script must compare `release` and fetch again once.
- R4. Owner numbers. "Senate 30 of 385" and "House 195 of 449" come from a production check that I cannot repeat. The denominator rule (D-C1) and the Congress scope are **[unverified]**.
- R5. A second owner in the script. The cast word map ("Yes (Aye)") and the kind labels must not get a second owner. The kind labels come from `VoteKind` through a data attribute. The cast map is three lines in the script, the same rule as `SideOf`. A test can check that the home page passes the cast words from `PublishedOfficialVote` constants, so the script holds no list of its own.
- R6. No script test. No automated test covers `public.js`. The new block is the largest script change so far. Screenshots at 360 and 1280 px on PR 3 are the check unless the owner wants a browser test, which would be a new test dependency and needs its own decision.
- R7. Misreading. "Both parties agreed" on many nominations can read as broad agreement on policy. The kind table, the Senate nomination note, and the caution line address this.
- R8. Reading level. "Substantive", "roll call", "nomination", and "cloture" are above grade 8. The strings use plain words first and link the glossary terms that exist. A reading level check of the final strings is **[unverified]**.
- R9. Delegates. A Delegate or the Resident Commissioner can have no rows on these roll calls **[unverified]**. The page would show four "No vote on record" lines. The note covers it, but the owner may prefer one line for the non-state seats from `NonStateSeats`.
- R10. Mid-decade maps. The district file is for the 119th Congress only (`docs/architecture/districts.md`, limits). The term end date of a representative is 3 January 2027 for most members. After that date the lookup depends on the 120th Congress file.
