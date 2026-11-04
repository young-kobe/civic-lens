# ZIP lookup on the home page (roadmap step 23)

This survey only designs. It builds nothing. Paths cite origin/main.

## What exists

- The view `public_api.zip_districts` has `zcta`, `state`, `district`, `land_area_square_meters`, and `land_share` (docs/architecture/districts.md:67). No public page reads it (districts.md:7).
- District 0 means at-large and delegate seats. The parser maps Census codes `00` and `98` to 0 (districts.md:21). The state code has the roster form (districts.md:20).
- Every release official has terms with `State` and `District` (src/CivicLens.Publication.Contracts/PublishedOfficeTerm.cs:8-9). The page for an official is `officials/{id}.html` (PublicationProtocol.cs:48).
- `PublishedOfficialView.LatestTerm` ignores the end date of a term (src/CivicLens.Host/Publication/PublishedOfficialView.cs:141-145). A "current seat" rule needs its own end check.
- `Place` prints "district 0" for an at-large seat (PublishedOfficialView.cs:28). The result must not repeat that text.
- PublicApi reads views only, through `civic_lens_public` (docs/architecture/publication.md:332). Vote routes are in src/CivicLens.PublicApi/Votes/VoteEndpoints.cs:14-30.
- The search page hides its form until JavaScript runs. A `noscript` notice links to static pages (src/CivicLens.Host/Publication/PublishedSearchPage.razor:18-24).
- The home hero has no form yet (src/CivicLens.Host/Publication/PublishedHomePage.razor:7-13).

## How a ZIP becomes officials

1. The browser sends the 5 digits to the API.
2. The API reads `zip_districts` and returns each `(state, district)` pair, largest land share first.
3. The page finds the seats in a seat index. The index maps `(state, district)` to the representative, and `state` to the senators.
4. The page shows one representative for each pair, plus the senators of the state once.

A state needs 2 senators. The six non-state codes (DC, PR, GU, AS, VI, MP) need none. `StateFips` already lists these codes (districts.md:20). A district with no current member is vacant. A state with fewer than 2 current senators has a vacant Senate seat.

## Decisions

### D1. Where the district-to-official join runs

Problem: the view holds districts only. Names and page links live in the release. A link must point to a page that exists in that release.

- **A. Route returns districts. The home page embeds a seat index built from release terms. The browser joins.** Pro: one view read, no migration, no new grant. Links always match the release. Con: the home page grows by about 541 short rows.
- **B. New view `public_api.officials`. The route joins in SQL and returns names.** Pro: fits R-68 (docs/roadmap.md:132). Con: a migration, a grant, and a column list change. The view cannot know which official pages exist. A link can break between releases.
- **C. Static file per state in the release.** Con: R-68 says no JSON file carries data to the public path (roadmap.md:132). It also adds files that must stay in step with the weekly snapshot.

Precedent checked: `vote_members` returns configured official IDs, and the page resolves names from the release (publication.md:339). That is option A.

**Recommend A.** The seat index is page markup from the same release as the links. It is not an API data path.

### D2. Route shape

Problem: the vote routes put an ID in the path and refuse bad IDs with 404 (VoteEndpoints.cs:20-23).

- **A. `GET /v1/zip-districts/{zcta}`.** Matches the vote detail route. A cache key is the full path.
- **B. `GET /v1/zip-districts?zcta=12345`.** Matches `/v1/search` and `QueryValues` (src/CivicLens.PublicApi/Http/QueryValues.cs:121). The key holds a query.
- **C. `GET /v1/zcta/{zcta}`.** Shorter, but it hides what the data is.

**Recommend A.** The answer has no cursor and no `release` field, because the view reads the newest snapshot and not `active_records`.

### D3. Answers and limits

Problem: the vote routes use 400, 404, 409, 429, and 503 (ApiResults.cs:46-72).

- **A. Follow the vote routes.** Not 5 digits: 400 through `ArgumentException` (ApiResults.cs:65). Unknown ZCTA, or no snapshot yet: 404 `ApiResults.RecordNotFound`-style problem with its own title. A statement timeout: 503 (ApiResults.cs:69). Rate: the global 60 a minute for each client (PublicApiApp.cs:55-56, PublicApiOptions.cs:29). 409 does not apply.
- **B. Add a per-route limit like the report policy** (PublicApiApp.cs:57-58). One lookup costs one indexed read, so this adds config for no gain.
- **C. Answer 200 with an empty list for an unknown ZCTA.** The caller cannot tell "unknown" from an error.

**Recommend A.** Add a `Cache-Control` header with a short life. No route sets one today (a grep of src/CivicLens.PublicApi finds none). The data changes weekly at most. Cloudflare then absorbs repeat ZIPs. Ask the owner about the life before the build.

### D4. No JavaScript

Problem: the lookup needs the API call.

- **A. Hide the form. Show a `noscript` notice with links to all officials.** Same as search (PublishedSearchPage.razor:18-24). Cheap. The reader loses the lookup.
- **B. A server-rendered result page in PublicApi.** PublicApi serves JSON only today. It would need HTML and the seat index. Rejected for the launch.
- **C. Static pages per state with all seats.** Works without JavaScript, but a reader still needs the state. It adds 56 pages.

**Recommend A.** C is a later step if reader reports ask for it.

### D5. What a split ZIP shows

- **A. List every district in order, with no percentage.** A note says the first has the most land. Land is not population, so a percent can mislead.
- **B. Show the land percentage.** Exact, but it looks like voter share.
- **C. Show only the first district.** Hides data and fails the earlier decision.

**Recommend A.** It keeps the earlier decision and avoids a false claim.

### D6. Vacant seats and delegates

- **A. Show the seat with the text "This seat is vacant."** Needs the seat index to know the seat exists. For a House seat, the view proves it. For a Senate seat, the rule is 2 per state.
- **B. Hide a vacant seat.** A reader sees fewer officials and cannot tell why.

**Recommend A.** Delegates and the at-large member use the title "Delegate for" or "Representative for ... at large", never "district 0".

## Results by case

| Case | Shown |
|---|---|
| No JavaScript | Hero text and browse link. Notice replaces the form. |
| One district | One representative, then the senators. |
| Split ZIP | Count line, every representative largest land first, senators once. |
| Unknown ZIP | Message with the 5 digits. Browse link. |
| Vacant seat | Seat title and the vacant line. No link. |
| DC or a territory | One delegate. Line that says it has no senators. |

## Tests

- Route, like `VoteEndpointsTests` (tests/CivicLens.Tests/PublicApi/Votes/VoteEndpointsTests.cs:210,269,308): 200 for one district. 200 for a split ZCTA with largest share first and a stable tie order. 400 for `1234`, `123456`, `abcde`, and a repeated parameter. 404 for an unknown ZCTA. 404 before the first snapshot.
- Rate: a `RateLimitTests`-style case on the new path (tests/CivicLens.Tests/PublicApi/Http/RateLimitTests.cs:14-23).
- Seat index (Host tests): at-large, delegate, DC with no senators, vacant House seat, one Senate seat vacant, an ended term excluded, and senators listed once for a split ZIP.
- Home page render: form hidden, `noscript` notice present, no form without the API URL.
- Boundary: PublicApi gains no dependency. The view test in `PublicApiViewsTests` already covers the role (tests/CivicLens.Tests/Infrastructure/PublicApi/PublicApiViewsTests.cs:24).
- Browser check at 360 and 1280 px, in light and dark themes (docs/agent-rules.md, Checks).
- Docs: update districts.md:7 ("No public page reads it yet"), publication.md:625, and roadmap R-68.

## New visible strings

Existing mockup strings: "Who represents you?", the lede, "Your ZIP code", "5 digits", "Find my officials", "Or browse all 541 officials".

New, for owner approval:

1. "Your officials"
2. "Looking up your officials"
3. "Your ZIP area lies in {n} districts. The district with the most land is first."
4. "Representative for {State}, district {n}"
5. "Representative for {State}, at large"
6. "Delegate for {Territory}"
7. "Senator for {State}"
8. "This seat is vacant."
9. "{Place} has no senators."
10. "Source: U.S. Census Bureau"
11. "A ZIP Code Tabulation Area is a Census Bureau area. It is not a USPS ZIP code. Near a border, check your address with your House office."
12. "No match for {zip}. Check the 5 digits. Some ZIP codes, such as a PO box, are not in the Census data."
13. "Enter a ZIP code with 5 digits."
14. "The lookup failed. Try again, or browse all officials."
15. "Too many lookups. Wait one minute and try again."
16. "The ZIP lookup needs JavaScript. Without it, browse all officials."
