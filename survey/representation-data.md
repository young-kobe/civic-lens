# Representation data survey

This survey only researches. It changes no code. Repository paths cite origin/main.

## Access note

The session network blocked almost every official host. DNS lookups failed, or the proxy refused the connection. The blocked hosts were congress.gov, api.congress.gov, clerk.house.gov, docs.house.gov, census.gov, api.usaspending.gov, api.govinfo.gov, fec.gov, lda.senate.gov, efdsearch.senate.gov, disclosures-clerk.house.gov, voteview.com, dataverse.harvard.edu, climatecommunication.yale.edu, ballotpedia.org, votesmart.org, opensecrets.org, govtrack.us, loc.gov, and web.archive.org. We sent no request around a block. We could read github.com, and web search returned summaries.

Labels:

- **Verified** means we read the text at the URL given, or in this repository.
- **Unverified** means a search summary or background knowledge. Read the source again before you rely on it.

Most license claims below are unverified. Decision 1 asks the owner to allow these hosts and run the license pass again.

## 1. Short answer

Civic Lens can audit one half of representation well today. It can measure an official against their own words, their party, and their own record. It cannot yet measure an official against what constituents want. No source gives that directly.

The best proxies are presidential vote share by district, Cooperative Election Study (CES) opinion with model estimates (MRP), and Census need data. Each one has large error margins and a contested validity. Academics agree that a single "represents well" score misleads. A defensible audit shows the evidence side by side, names the error, and gives no grade.

The high-value work is in two layers:

- **Layers we already hold but do not show.** These are voice votes, unanimous consent, committee deaths, cosponsor timing, missed votes, and Senate NOMINATE scores.
- **Small public-domain additions.** These are amendments, nominations, ACS district profiles, and Voteview House scores.

Money data (FEC, LDA, disclosures) is legal to show only as aggregates or with care, and it is easy to misread.

## 2. Sources ranked by value divided by effort

Two groups. **Own words** measures an official against their own record. **Constituents** claims to know what constituents want, or what they need.

| Rank | Source | Group | Holds | Coverage and grain | Access | Update | Join | Size | Misleading risk |
|---|---|---|---|---|---|---|---|---|---|
| 1 | Bill actions we already capture | Own words | Action text and codes: voice votes, UC, referrals | Our Congresses, each bill | Held | Daily | Bill ID | Small | Medium |
| 2 | Voteview member files | Own words | DW-NOMINATE dim 1 and 2 per member | 1789 to now, each member | Bulk CSV, no key | Weekly (unverified) | ICPSR, bioguide | Small | High |
| 3 | Congress.gov `/amendment` | Own words | Amendment sponsors, actions, recorded votes | 97th Congress on (unverified), each member | API, api.data.gov key | Daily | Bioguide, amendment ID, roll number | Small | Low |
| 4 | congress-legislators ID crosswalk | Join key | FEC, ICPSR, OpenSecrets, Vote Smart, Ballotpedia IDs | All members | Bulk JSON, no key | Periodic | Bioguide | Small | Low |
| 5 | Census ACS by district | Constituents (need) | Income, health cover, age, jobs, veterans, each with MOE | 119th districts, 1-year and 5-year | API, key | Yearly | State FIPS plus district | Small | Medium |
| 6 | Presidential vote by district (The Downballot) | Constituents (proxy) | 2024 vote share per district | 435 districts | Spreadsheet | Each cycle | State plus district | Small | Medium |
| 7 | Congress.gov `/nomination` | Own words | Nominee, actions, voice votes | Senate, each nomination | API, key | Daily | Roll number, PN ID | Small | Low |
| 8 | CES (CCES) | Constituents | Survey answers on policy, district of each respondent | 2006 to 2024, about 50,000 a year | Dataverse bulk | Yearly | State plus district | Medium | High |
| 9 | FEC OpenFEC | Influence | Receipts with donor ZIP, Schedule E outside money | All federal candidates | API, key, and bulk | Nightly (unverified) | FEC ID to bioguide | Medium | High |
| 10 | House discharge petitions | Own words | Signers and dates | 118th on, House | HTML only | Live | Member list to bioguide | Medium | Low |
| 11 | MRP ideology estimates (Tausanovitch and Warshaw) | Constituents | Mean ideology per district | District, one map vintage | Dataverse | Rare | State plus district | Medium | High |
| 12 | USAspending | Constituents (outcome) | Awards by district | 2007-10-01 on in the API | API, no key (unverified) | Daily | State plus district | Medium | High |
| 13 | LDA lobbying | Influence | Issues, bills named in free text, chambers lobbied | Each filing | API, moving to LDA.gov | Quarterly | Bill ID by text parse | Medium | Medium |
| 14 | Earmark tables | Influence, own words | Member requests for projects | FY2022 on, House and Senate | PDF tables | Yearly | Member name to bioguide | Medium to large | Medium |
| 15 | Committee recorded votes | Own words | Markup votes as attached files | House, partial | PDF mostly | Each meeting | Name to bioguide | Large | High |
| 16 | Financial disclosures and PTRs | Influence | Assets and trades in value ranges | House 2008 on, Senate eFD | Index files, PDFs, click-through | Rolling | Name to bioguide | Large | High |
| 17 | Archived campaign sites | Own words | Promises in the official's words | 2000 on, weekly (unverified) | LoC archive, Wayback CDX | Each cycle | Candidate to bioguide | Large | High |

### Terms, biases, and evidence for each source

**1. Bill actions.** We capture each action line with its date, type, code, and source (`docs/architecture/bills.md:214`, verified). Congress.gov sets `actionCode` only for House and Library of Congress actions. "An authoritative list of action codes where the `<sourceSystem>` is 2 (House) does not exist." Verified: https://github.com/LibraryOfCongress/api.congress.gov/blob/main/Documentation/BillEndpoint.md. Senate actions need text matching. A voice vote names no member.

**2. Voteview.** Our Senate members fixture holds `nominate_dim1` and `nominate_dim2` (`tests/CivicLens.Tests/Fixtures/VoteviewVoteFixtures.cs:37`, verified). We could not find a license. The site asks for a citation of its database by Lewis, Poole, Rosenthal, and others (unverified, https://voteview.com/about). CC BY 4.0 reuse with that citation looks likely but is unverified.

Validity (unverified, background): the first dimension partly measures party pressure and agenda control, not personal belief (Lee 2009). Scores compare members within one chamber only. They do not place voters on the same scale.

**3. Amendments.** Sponsors carry `bioguideId`. Actions carry `recordedVotes` with the roll number. Text shows "agreed to in Senate by Voice Vote." Verified: https://github.com/LibraryOfCongress/api.congress.gov/blob/main/Documentation/AmendmentEndpoint.md. The README says "An API key is required for access" and gives 5,000 requests an hour. It also says: "While not required, the following citation may be used for attribution: Data retrieved from Congress.gov". Verified: https://github.com/LibraryOfCongress/api.congress.gov/blob/main/README.md. As a federal work it is likely public domain under 17 U.S.C. 105 (unverified). CC BY 4.0 reuse looks allowed.

**4. Crosswalk.** The `id` fields include `bioguide`, `fec` (a list), `icpsr`, `opensecrets`, `votesmart`, `ballotpedia`, `maplight`, and `govtrack`. The license is CC0-1.0. Verified: https://github.com/unitedstates/congress-legislators. The roster already captures `legislators-current.json` (`docs/architecture/roster.md:11`, verified). CC BY 4.0 reuse is allowed.

**5. ACS.** A 1-year district profile exists at `api.census.gov/data/2023/acs/acs1/sdataprofile/cd119` (unverified). Every estimate has a margin of error. A key is required (unverified). Terms not read. Federal data is likely public domain (unverified).

**6. Presidential vote.** The Downballot computes 2024 results on 2024 and 2026 lines (unverified, https://www.the-downballot.com/data). Terms not found. These are computed, not official, counts. MEDSL has precinct and county files on Dataverse. Secondary sources report CC0 (unverified). MEDSL would need precinct aggregation, a large effort.

**7. Nominations.** The record holds the nominee, committees, actions, and "Confirmed by the Senate by Voice Vote." Verified: https://github.com/LibraryOfCongress/api.congress.gov/blob/main/Documentation/NominationEndpoint.md. Terms as in 3.

**8. CES.** Search summaries say the 2020 file is CC BY 4.0 and the cumulative file is CC0. The 2024 file needs a Dataverse login (all unverified, https://cces.gov.harvard.edu/). A district holds about 100 to 150 respondents, so raw means are noisy. The panel is opt-in and weighted. A 2026 paper reports non-response bias in 2024 (unverified, https://arxiv.org/pdf/2606.12889).

**9. FEC.** Itemized receipts hold `contbr_zip` with up to 9 digits. Verified in the code of https://github.com/fecgov/openFEC (`webservices/common/models/itemized.py`). The API terms say: "All services which utilize or access the API should identify openFEC API as the data source." Verified: https://github.com/fecgov/FEC/blob/master/TERMS-OF-SERVICE.md.

The law restricts reuse. "Sale or use of publicly disclosed campaign finance information is restricted. 52 U.S.C. §30111(a)(4) and 11 C.F.R. §104.15". Verified: https://github.com/fecgov/fecfile-web-app/blob/develop/front-end/src/app/login/security-notice/prod-notice.component.ts. The statute bars use "for the purpose of soliciting contributions or for commercial purposes" (unverified, https://www.fec.gov/updates/sale-or-use-contributor-information/). Aggregates with no names look safe. Donor names must not go under CC BY 4.0.

Bias: gifts of $200 or less need no itemization (unverified). Conduits such as ActBlue and WinRed, and ZIPs that cross district lines, skew in-district shares.

**10. Discharge petitions.** Each page lists signer, state, district, party, and date, and records withdrawals (unverified, https://clerk.house.gov/DischargePetition/2026072226). No feed found. Robots and terms not read.

**11. MRP ideology.** The data has one mean ideology score per district from about 1 million respondents. Dataverse DOI 10.7910/dvn/bqku4m (unverified). License not found. It gives one latent dimension, not support for a policy.

The Yale climate maps are a better issue example, but their terms forbid reuse. "You may not reproduce, sell, rent, lease, loan, distribute or sublicense or otherwise transfer any Data" (unverified, https://climatecommunication.yale.edu/visualizations-data/climatenews2020). CC BY 4.0 reuse is not allowed.

**12. USAspending.** Filters take `district_original` or `district_current`, by place of performance or recipient location. Verified: https://github.com/fedspendingtransparency/usaspending-api/blob/main/usaspending_api/api_contracts/contracts/v2/search/spending_by_geography.md. The repo is CC0 (verified, https://github.com/fedspendingtransparency/usaspending-api/blob/main/LICENSE). Data terms not read.

GAO-18-138 found only 24 to 34 percent of transactions fully consistent with agency records (unverified, https://www.gao.gov). A recipient district is often a headquarters. Spending follows formulas and agencies, not one member's vote.

**13. LDA.** Filings name bills in free text. Item 17 names only "U.S. House" or "U.S. Senate", never a member (unverified, sample filing at https://lda.senate.gov). The site moves to LDA.gov after 2026-06-30 (unverified). Terms not read.

**14. Earmarks.** The House posts member requests as PDF tables (unverified). The Bipartisan Policy Center built a FY2022 dataset with bioguide IDs (unverified, https://bipartisanpolicy.org/blog/congressionally-directed-spending-fy2022-dataset). A request is not an enacted amount. This is also "own words": a member asks for money for named district projects.

**15. Committee votes.** `/committee-meeting` lists a document type "Committee Recorded Vote" with a file format, mostly PDF. Verified: https://github.com/LibraryOfCongress/api.congress.gov/blob/main/Documentation/CommitteeMeetingEndpoint.md. No endpoint gives member casts. Senate committees have no common store (unverified).

**16. Disclosures.** The law is 5 U.S.C. 13107(c). It bars use "for any commercial purpose, other than by news and communications media for dissemination to the general public" and for credit ratings (unverified, OGE summary). The Senate eFD needs a click-through agreement that we could not read. The House PDFs are often scanned. Amounts are ranges.

**17. Campaign sites.** The Library of Congress US Elections Web Archive crawls campaign sites (unverified, https://data.labs.loc.gov/us-elections/). Rights page not read. Wayback CDX terms not found.

## 3. Hidden layers in current data

None of these needs a new source. Each measures an official against their own record.

1. **Voice votes and unanimous consent.** Bill action text says when a bill passed with no recorded vote. A page can say "passed by voice vote, so no member cast is on record." This tells readers why a statement has no matching roll call.
2. **Death in committee.** A bill whose last action is a referral never moved. A sponsor's record can show how many of their bills got a hearing, a markup, or a floor vote. Base rate: about 11 percent of bills leave committee (unverified, GovTrack). The page must show the base rate.
3. **Cosponsor timing.** We store the original flag, the date, and the withdrawal date (`docs/architecture/bills.md:213`, verified). Code can show "joined after the bill passed the House" or "withdrew 3 days before the vote." Koger (2003) treats cosponsorship as position-taking (unverified).
4. **Missed votes.** "Not Voting" casts already exist. R-47 plans this board (`docs/roadmap.md:41`, verified).
5. **Procedural versus final votes.** The vote kind table separates rule, previous question, motion to recommit, and passage (`docs/architecture/votes.md:45`, verified). A member can vote against a bill's rule and for its passage. Showing both stops a reader from picking one cast as "the position."
6. **Senate NOMINATE.** The Senate members file we capture holds the scores, and the parser ignores them. House scores need one more Voteview file per Congress.
7. **Amendment votes.** 96 of 676 House roll calls were amendments (`docs/architecture/votes.md:462`, verified). Votes with no amendment sponsor link lose the "who offered it" layer. The `/amendment` endpoint fills it.
8. **Statement against bill movement.** Statement records name bills (`BillNumberFinder`). Code can show "said they support H.R. X; H.R. X died in a committee the member sits on." Committee membership needs the planned committees source (R-58).
9. **Shared language.** R-38 plans this (`docs/roadmap.md:127`, verified). It shows coordinated messaging, not constituent interest.

## 4. Candidate features

### F1. How it passed

- **Data:** bill actions we hold, plus `/amendment` and `/nomination`.
- **Method:** code classifies each passage as roll call, voice vote, or unanimous consent from action codes and text. Each bill and nomination page then shows the path. A statement record on that bill links to the path.
- **Reader limit:** "This bill passed by voice vote. The record shows no member's position."
- **Size:** small.

### F2. Bill fate and cosponsor timing on the profile

- **Data:** actions and dated cosponsors that we hold.
- **Method:** for each bill an official sponsors or cosponsors, show the furthest stage reached and when the official joined, relative to stages. Show the chamber base rate beside the official's counts. Show no rank.
- **Reader limit:** "Most bills never leave committee. A dead bill does not show that the sponsor failed."
- **Size:** small.

### F3. District context panel

- **Data:** ACS 1-year district profile, presidential vote share, the ZCTA file we hold.
- **Method:** the official page shows a few district facts with their margins of error. Examples are the share with no health cover, the median income, and the 2024 presidential margin. Votes on bills tagged to a matching issue sit beside it. No code joins need to a cast as "for" or "against."
- **Reader limit:** "These numbers describe the district. They do not say what its residents want. Each has a margin of error."
- **Size:** small to medium.

### F4. Ideology position with honest framing

- **Data:** Voteview NOMINATE for both chambers, plus the 2024 presidential vote by district.
- **Method:** plot each member's first dimension against their district's presidential margin, as in Bafumi and Herron (2010). Mark the member. Show the party fit lines. Name the method on the method page.
- **Reader limit:** "This score summarizes roll calls. It reflects party and agenda as well as belief. It does not measure a member against voters."
- **Size:** small for the Senate (data held), medium with the House file and vote shares.

### F5. Money in and outside the district

- **Data:** FEC itemized receipts with ZIP, Schedule E, the ZCTA file, the FEC IDs in the crosswalk.
- **Method:** show aggregate shares only: in-district, in-state, out-of-state, unitemized, and outside spending for and against. Show no donor names.
- **Reader limit:** "Gifts of $200 or less are not itemized. ZIPs can cross district lines. Shares are estimates."
- **Size:** medium. It needs the legal check in Decision 4.

## 5. Open decisions for the owner

**D1. Network access for verification.**

- Option (a): allow the blocked official hosts and run the license pass again.
- Option (b): accept the unverified terms for now. Verify each source when its PR lands.
- Recommendation: (b), with a terms check as the first step of each source PR. R-58 already demands it.

**D2. Whether Civic Lens shows a constituent-preference estimate at all.**

- Option (a): none. Show own words and district facts only.
- Option (b): presidential vote share only, as a proxy from official counts.
- Option (c): CES and MRP issue estimates with error bars.
- Recommendation: (b) first. Add (c) only after a method note that a person reviews, and only for an issue with a published estimate whose terms allow reuse.

**D3. Order of new sources.**

- Option (a): keep the R-58 order. That is committees, NOMINATE, FEC, LDA, Census, then disclosures.
- Option (b): order by value over effort. That is amendments, House NOMINATE, ACS, presidential vote, FEC, LDA, committees, then disclosures.
- Recommendation: (b). Committee data is large for little member-level signal, because casts are mostly PDF.

**D4. FEC donor detail.**

- Options: (a) aggregates only; (b) named large donors with a non-commercial notice.
- Recommendation: (a). It avoids the 52 U.S.C. 30111(a)(4) risk and the CC BY 4.0 conflict.

**D5. Scores and grades.**

- Options: (a) never publish a composite representation score; (b) publish third-party scorecards with their vote lists; (c) build our own score.
- Recommendation: (a). Scorecards pick votes to make members look more extreme (Snyder 1992, unverified). R-48 and R-64 already reject totals and labels.

**D6. Where district facts live in storage.**

- Options: (a) new tables beside the districts feature that cite one Census capture; (b) values inside each release only.
- Recommendation: (a). It follows the districts precedent of tables that cite a capture (`docs/architecture/districts.md`). The owner must choose before the ACS PR.

## 6. Sources ruled out

- **Yale climate opinion maps.** The terms forbid redistribution (unverified quote above). We may link to them only.
- **OpenSecrets.** The API closed to new users in 2025 (unverified), and bulk sign-up gets no answer. The FEC raw data serves the same need.
- **Ballotpedia.** Data sets are "proprietary and confidential" (unverified, https://developer.ballotpedia.org/dictionaries-and-terms/terms-of-use). Robots reportedly limit crawl hours. Use state election offices for ballot measures.
- **Ballot measure results by district.** State offices use 50 formats, and the district map needs precinct data. This is large, and there is no clean join.
- **Vote Smart ratings and the Political Courage Test.** We could not read the API terms. Many incumbents do not answer the test, and it bars partisan use.
- **Redistricting Data Hub.** Use is reportedly non-commercial only, and some data comes from L2 (unverified). This conflicts with CC BY 4.0.
- **Constituent contact data.** No public data set exists. Congress is not subject to FOIA (unverified). Opt-in apps such as Countable are self-selected.
- **DCinbox newsletters.** These are member words, not constituent views. They duplicate press statements, and the license is unknown. Revisit as a statement source.
- **CDC PLACES and Cancer Statistics.** These have no native district grain. CDC says its district cancer estimates are not for comparing districts (unverified).
- **Senate committee votes and UC agreements.** They exist only as free text in the Congressional Record. Wait for R-17, which collects that text.
- **GovTrack derived data.** We could not read its terms or robots. We use the primary Congress.gov data instead.
