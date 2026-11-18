# Can a statement lead a reader to the right provision and vote?

## Sample

We ran an offline baseline on November 18, 2026. It uses four official bill editions, 163 bill sections, six official statement sources and four roll calls. The cases cover health costs, energy, federal taxes, immigration enforcement and AI. A Thomas Paine memorial provision tests what happens outside the 16 configured issues. The veterans claim is absent.

The [case manifest](issue-cases.json) contains the source URLs, bill edition codes, file hashes, reference topics, questions, expected sections, vote questions and selected member IDs. The questions paraphrase selected statements. A question about a private-insurance cap probes the removed provision that Sen. Wyden discussed. It does not attribute support for an enacted private cap to Wyden.

| Evidence question | Required bill text in this sample | Vote or other evidence |
|---|---|---|
| What insulin limits apply to Medicare? | H.R. 5376 sections 11406 and 11407 distinguish Part D and Part B coverage. A query about a broad private-insurance cap has no matching provision in the enrolled text. | A motion on the insulin provision and a vote on the whole package had different scopes. [Wyden statement](https://www.finance.senate.gov/chairmans-news/wyden-statement-on-republicans-stripping-insulin-provision-from-ira). |
| Which incentives apply to new and used vehicles? | The same H.R. 5376 has separate sections 13401 and 13402. Both have energy and tax implications. | Heinrich's statement and package vote concern the same bill. [Heinrich statement](https://www.heinrich.senate.gov/newsroom/press-releases/heinrich-votes-for-historic-inflation-reduction-act-to-lower-costs-for-americans-invest-in-health-care-and-climate-action). |
| Who comes under a detention requirement, and who can bring a civil action? | S. 5 sections 2 and 3 answer distinct questions under immigration enforcement. Read the operative text and qualifications. | Britt voted for Senate passage as amended. Her office discussed both provisions. [Britt release](https://www.britt.senate.gov/news/press-releases/u-s-senators-katie-britt-ted-budds-landmark-immigration-enforcement-bill-passes-senate/). |
| What starts the removal deadline for an intimate image, including a digital forgery? | S. 146 section 3 sets the deadline after a valid request. Section 4 refers readers to definitions added by section 2. | The House roll call records Rep. Dean's cast. Actual reductions in victim trauma would require outcome evidence. Attribute the quotations in the release to their named speakers. [Dean and Cruz remarks](https://www.klobuchar.senate.gov/public/index.cfm/2025/4/bipartisan-klobuchar-bill-to-protect-online-privacy-and-combat-explicit-deepfakes-passes-congress). |
| Where could the Thomas Paine memorial stand? | H.R. 9600 section 2 in the House-reported edition addresses the location. It can be retrieved even without a fitting tracked issue. | Check the text before the chamber for any claim about the precise effect of the vote. [Sponsors' account](https://raskin.house.gov/2026/11/raskin-spartz-legislation-finalizing-details-for-national-memorial-honoring-indispensable-american-patriot-thomas-paine-passes-house). |

## Three checks

1. **Topic candidates.** For each of eight selected section headings, query all configured issue names, definitions and inclusion examples. Use SQLite FTS5 with English Porter stemming and BM25 ranking. Keep up to three candidates and compare with the manually assigned topic set. This deliberately simple baseline omits exclusion rules, the bill's primary policy area and its body text.
2. **Provision retrieval.** Search the complete text of all 163 outer sections using the nine questions in the manifest. Keep embedded changes to other statutes inside their enclosing section. Keep up to three candidates across all four bills. Issue labels, bill IDs and expected answers are withheld from ranking. Six questions have a required section set. Three have no sufficient provision in this corpus. This is retrospective discovery. The enrolled editions can postdate the statement or vote.
3. **Vote facts.** Read the official roll-call XML, check the question and match selected member IDs to casts. The checks bind each chamber's file by digest. They test membership and motion identity separately from the human assessment of what a vote establishes.

## Results

| Check | Observed result | Consequence for the design |
|---|---|---|
| All expected issues in the first three candidates | 5 of 7 labeled sections. Both vehicle-credit headings returned the tax topic and missed energy. The memorial control also returned three topic candidates. | Read the operative text. Allow multiple labels and an explicit unmapped result. These counts measure candidate coverage only. |
| All required sections in the first three candidates | 4 of 6 answerable questions. The vehicle query missed the used-vehicle credit. The platform query found the deadline and the reference to definitions, but missed the section that supplies them. | Decompose multipart claims and follow explicit references before showing a conclusion. |
| Unanswerable questions that still returned candidates | 3 of 3. Private-insurance wording returned Medicare sections. Historical insulin prices and a victim-outcome question also returned legislation. | Check population, mechanism and evidence type. A reader needs price series or outcome evidence for the latter questions. |
| Recorded actions | 7 selected member casts across 4 roll calls matched their references. On August 7, 2022, Susan Collins voted Yes on the insulin budget waiver and No on final passage of H.R. 5376. | Preserve both actions. The waiver failed despite 57 Yes votes under a three-fifths requirement. Inferring her insulin position from final passage alone loses the distinction. [Insulin motion](https://www.senate.gov/legislative/LIS/roll_call_votes/vote1172/vote_117_2_00314.htm) and [package vote](https://www.senate.gov/legislative/LIS/roll_call_votes/vote1172/vote_117_2_00325.htm). |

Sen. Collins and Sen. Shaheen publicly proposed a different insulin bill in June. That gives readers another connection by mechanism and population. It does not turn the August vote into a vote on the June proposal. Likewise, S. 146 passed the Senate by unanimous consent. Its House roll call cannot furnish an individual senator's vote. [June proposal](https://www.collins.senate.gov/newsroom/diabetes-caucus-co-chairs-collins-shaheen-release-bipartisan-legislation-to-lower-insulin-costs), [Senate proceedings](https://www.govinfo.gov/content/pkg/CREC-2025-02-13/html/CREC-2025-02-13-pt1-PgS988-3.htm) and [House roll call](https://clerk.house.gov/Votes/2025104).

## Check against the published site

A separate read-only query on the Orin sampled release **1421**. S. 5 revision 4 carried the Immigration policy area and the tracked issue `immigration-enforcement`. S. 146 revision 2 carried the Science, Technology, Communications policy area and no tracked issue. Yet its enrolled text explicitly defines digital forgery and its sponsors discuss deepfakes. This is a candidate for a missed AI tag. The public views cannot tell us which inputs or model decision led to that result. These two observations are a coverage check, separate from the offline retrieval scores.

Replay with a published-view connection. Later releases can return other revisions or tags. An absent bill yields a null revision.

```sql
BEGIN TRANSACTION ISOLATION LEVEL REPEATABLE READ READ ONLY;
SET LOCAL statement_timeout='4s';
SET LOCAL lock_timeout='500ms';
SELECT a.release_number, k.n, b.revision_number, b.policy_area,
       COALESCE((
           SELECT string_agg(i.issue_id, ',' ORDER BY i.issue_id)
           FROM public_api.bill_issues i
           WHERE (i.record_id, i.revision_number) = (b.record_id, b.revision_number)
       ), '') AS issue_ids
FROM public_api.active_release a
CROSS JOIN (VALUES (5), (146)) k(n)
LEFT JOIN public_api.bills b
    ON b.congress = 119 AND b.bill_type = 's' AND b.bill_number = k.n
ORDER BY k.n;
ROLLBACK;
```

## Replay and limits

Run from the repository root with Python 3 and SQLite built with FTS5. The measured run used SQLite 3.37.2. The download step uses curl. It fetches eight public XML files into a temporary directory. The [runner](map-issues.py) works offline after that and checks the input hashes and repository issue catalog. Changed source bytes require a new comparison.

```bash
issue_inputs=$(mktemp -d)
python3 - "$issue_inputs" <<'PYTHON'
import json
from pathlib import Path
import subprocess
import sys

manifest = Path("docs/post-r68/experiments/issue-cases.json")
for key, source in json.loads(manifest.read_text())["inputs"].items():
    path = Path(sys.argv[1]) / f"{key}.xml"
    subprocess.run(["curl", "-fsSL", "--max-time", "30", "-o", path, source["url"]], check=True)
PYTHON
python3 docs/post-r68/experiments/map-issues.py "$issue_inputs"
```

The small reference set was selected and labeled for this design discussion. The manifest freezes analyst paraphrases and links to six office releases. It does not freeze those pages or quotation offsets. Replay reproduces retrieval for the saved questions. Testing speaker attribution and extraction would require dated original captures and exact spans. The vote files independently establish the casts. This run does not validate the statutory equivalence of a voted amendment and the later enrolled edition, estimate classification accuracy on unseen issues, or benchmark an LLM. A production link would require an exact version and passage, date-appropriate evidence, and review of any claimed policy alignment. The [multi-issue trial](../claims.md#how-to-add-an-issue) defines the next test.
