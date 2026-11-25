# Five trials of linked civic evidence

We ran the computational studies on November 18, 2026 using public records. All fitting, ranking and browser checks ran locally. The charging study used public APIs. The reader instrument was exercised with automation. There were zero human participants.

## 1. From a quotation to a decision

We took seven short spans from four original sources. Each span retains its speaker or office, date and source in the [case file](cases.json). The immigration amendment description comes from the office's narrative and the projected safety claim from the Riley family. The platform statement comes from Rep. Dean and the victim-outcome claim from Sen. Cruz. Two Senate letters supplied the food-assistance cases, including an explicit question about enrollment. The letter writers asked for an answer that these legislative records could not supply. [Office release on S. 5](https://www.britt.senate.gov/news/press-releases/u-s-senators-katie-britt-ted-budds-landmark-immigration-enforcement-bill-passes-senate/), [release on S. 146](https://www.klobuchar.senate.gov/public/index.cfm/2025/4/bipartisan-klobuchar-bill-to-protect-online-privacy-and-combat-explicit-deepfakes-passes-congress), [June 5 letter](https://www.agriculture.senate.gov/imo/media/doc/letter_to_usda_va_hud_and_hhs_re_debt_limit_abawd_exemption_6523.pdf) and [June 26 letter](https://www.fetterman.senate.gov/wp-content/uploads/2023/06/SNAP-demographics_FINAL.pdf).

Each query asks which provision addresses the quoted mechanism and which recorded decision concerned it. We selected the reference pairs before scoring. A sufficient candidate list covers both exact IDs in its first five distinct records. The immigration reference uses the Cornyn amendment, the platform reference uses House passage, and both SNAP references use section 311 and Senate passage of the House text. The SNAP amendment to remove the sunset failed and remains a relevant competing question. An outcome claim needs different evidence. [SNAP amendment](https://www.senate.gov/legislative/LIS/roll_call_votes/vote1181/vote_118_1_00141.htm) and [final passage](https://www.senate.gov/legislative/LIS/roll_call_votes/vote1181/vote_118_1_00146.htm).

| Exact evidence sets in first five | Keyword search (BM25) | Semantic search (MiniLM) | BM25 plus explicit bill-to-vote links |
|---|---:|---:|---:|
| Immigration and digital privacy | 1/2 | 1/2 | 2/2 |
| Food assistance, withheld from earlier issue pilots | 0/2 | 0/2 | 2/2 |
| Insufficient-evidence cases returning candidates | 3/3 | 3/3 | 3/3 |

Both stand-alone methods found the SNAP provision on both answerable queries. Adding the official bill link supplied the passage vote. The same expansion attached two SNAP votes to a projected safety claim about immigration when the leading text hit came from the wrong bill. An exact edge can extend an incorrect starting point. This warrants testing typed links as a source of candidates and measuring the reviewer's rejection of wrong paths.

**Method.** The six bill editions and five roll calls were eligible from the recorded legislative date through the end of the quote's day. This is an event-date replay. It does not establish when a file reached an operator that day. The index uses windows of 120 whitespace words with a stride of 100. Both methods rank the best window for each distinct record. BM25 uses SQLite FTS5 and Porter stemming. The dense baseline uses frozen all-MiniLM-L6-v2 on CPU with a 256-token limit, mean pooling, normalization and cosine similarity. The graph variant takes two leading lexical sections, adds eligible votes on the first section's bill using the roll call's measure reference, and fills the remaining slots by lexical rank. No method receives the reference IDs or issue labels. [Model card](https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2).

The two SNAP positives share one bill, provision and vote. The five roll calls form a narrow search space. These results measure exact candidate coverage on analyst-selected cases. We did not generate verdicts or measure a reviewer's acceptance time, attribution error rate or human agreement. Extending the test to another bill family and labeling related but insufficient votes independently would test whether the gain transfers.

## 2. Following a provision between editions

Ten reference transitions cover four bill families. Each reference asks if the specified policy mechanism survived in a target section, allowing its details to change. Eight continued, including two with new numbers. Two selected proposals dropped out. We compared an exact printed section number with the most similar target section using word-level TF-IDF, sublinear frequency, English stop words and a fixed cosine cutoff of 0.30. Both methods can return no continuation. Text search considers every outer section of the target edition. Repeated printed numbers carry an ordinal to keep their text distinct. [Reference cases](cases.json) and [official edition definitions](https://www.govinfo.gov/help/bills).

| Method | Continued provisions found | Dropped provisions correctly left unmatched | Total correct |
|---|---:|---:|---:|
| Same section number | 6/8 | 1/2 | 7/10 |
| Most similar section above cutoff | 8/8 | 1/2 | 9/10 |

The House's H.R. 5376 section 139401 mapped to enrolled section 11406 on Medicare Part D insulin, and section 136402 mapped to 13402 on previously owned vehicles. Reusing number 23002 connected universal preschool to grants for forest landowners. Text matching avoided that error but mapped the House's private-insurance cap in section 30604 to the enrolled deductible safe harbor in section 11408. Their provisions both mention insulin and private plans. They have different legal effects. Five transitions scored 1.000 at the reported precision. Repeated wording makes this small test easier. A public account of what survived needs the old and new passages and a check of the mechanism. [House text](https://www.govinfo.gov/app/details/BILLS-117hr5376eh) and [enrolled text](https://www.govinfo.gov/app/details/BILLS-117hr5376enr).

## 3. From a charging grant to an operating site

We chose Deerfield, Massachusetts because a federal opening report, an identifiable grant and a nearby station with a similar name permitted a test of mistaken identity. The observation cutoff is November 18, 2026. Financial amounts below are the Treasury values returned at that cutoff.

| Step | Observed evidence | Link strength |
|---|---|---|
| Authority to program | Section 11401 of Public Law 117-58 established charging and fueling grants. FHWA identifies 23 U.S.C. 151(f) as the program authority. | Explicit statutory citation. [FHWA](https://highways.fhwa.dot.gov/iija/fact-sheets/charging-and-fueling-infrastructure-discretionary-grant-program). |
| Award to transaction | FAIN 693JJ32440370 named the Town of Deerfield and its public access EV charging project. One July 9, 2024 transaction obligated $2,462,612.09. Treasury reported $615,653 in nonfederal funding and $1,578,890.45 in outlays. | Exact award identifier and reconciled amounts. The reported outlay has no station-level payment allocation. [Treasury award](https://www.usaspending.gov/award/ASST_NON_693JJ32440370_069). |
| Program and project to delivery | On November 15, 2024, the Joint Office reported the Leary Public Parking Lot had four DC fast and four Level 2 ports funded by the CFI program. | Named site, recipient, rounded amount and program agree. The opening report omits the FAIN. [Federal opening report](https://driveelectric.gov/news/deerfield-ma-cfi-charging-station). |
| Named site to inventory | Three available station records at 59 North Main Street, IDs 367915, 367916 and 371494, account for four Level 2 and four DC fast ports. Their inventory open dates are November 14 or 15, 2024. | Site, date and port mix corroborate delivery. Inventory rows do not identify the award. [Station data definitions](https://afdc.energy.gov/data_download/alt_fuel_stations_format). |

A name-only match also finds station 205129, “LEARY LOT #1,” at 55c North Main Street, with a January 2022 open date. Two Town Hall stations on Conway Street have April 2024 open dates. These prior records cannot establish newly opened ports under the July award. The federal opening report followed the award transaction by 129 days. The evidence supports a reported opening. Utilization, access for particular groups and a causal benefit remain open. A dated payment series or final expenditure would require more than the cumulative reported outlay. We did not time human verification. [Frozen selected fields](deerfield-sample.json) and [reconciliation and live request definitions](delivery.py).

## 4. Do shared sponsors recur?

We selected consecutive H.R. numbers 1001 through 1032 in the 118th Congress and 4001 through 4032 as a later window. They were introduced on February 14 and June 12, 2023 respectively. We took sponsors and original cosponsors on introduction and restricted both windows to the 432 House IDs appearing on both bracketing Clerk rosters. One June bill, H.R. 4027, shared a normalized title or an official related-bill key with the February sample and was excluded. That left 32 training bills with 206 edges and 31 later bills with 165. Names, parties and issue labels played no role. An official cross-reference or title will miss some related proposals. [GovInfo bill status](https://www.govinfo.gov/bulkdata/BILLSTATUS), [February roster](https://clerk.house.gov/evs/2023/roll118.xml) and [July roster](https://clerk.house.gov/evs/2023/roll290.xml).

A pair is “repeated” if its members backed at least two early bills. It recurs later if the two backed any retained June bill together. Each of 99 seeded random graphs starts from the observed early graph. We swap two member endpoints on distinct bills, rejecting duplicate edges and self-swaps, until ten swaps per edge succeed. This preserves every member's degree and every bill's degree. The later graph stays fixed. The ranges below describe those simulations. They supply no proof of uniform sampling or party-independent coordination.

| Early sample | Repeated pairs | Null min / median / max | Null draws at least as large | Observed repeated pairs seen later | Later overlap under null, min / median / max |
|---|---:|---|---:|---:|---|
| All 32 bills | 58 | 20 / 38 / 91 | 5/99 | 22/58 | 0 / 1 / 5 |
| Omit H.R. 1018 and its 56 backers | 15 | 1 / 9 / 18 | 4/99 | 1/15 | 0 / 0 / 2 |

The apparent later recurrence depends strongly on the most widely backed early bill. A public coalition label would overstate these two days of activity. Expand the time windows, preserve parties or committees in a second comparison, and show the particular bills behind any recurring pair. [Runner](coalitions.py) and [H.R. 1018](https://www.govinfo.gov/app/details/BILLS-118hr1018ih).

## 5. Can readers explain what happened?

**Participants: 0.** No readers were available. We prepared a local [two-case reader instrument](reader-study.html) and ran all four assignments in headless Chromium at 390 and 1,200 pixels. Each assignment completed at both widths. Automation verified identical evidence across layouts, the assignment balance at each position, no horizontal overflow, focus on each new case, required answers, and the two local time and confidence records. These checks measure the instrument's behavior. Comprehension, speed differences and confidence calibration remain unmeasured.

As a separate page-content check, the live House roll 325 record named H.R. 9600, explained the two-thirds threshold and reported passage. Its main text contained none of “Paine,” “memorial” or “location.” A reader following the bill link can seek that context. This check concerned the fetched record page only. Its HTML digest was `f469a6fb3e57518e229c9258c94e6092c6c34f28df2d55b852160a5362c083a0`. [House roll 325 record](https://civic-lens.info/records/e8e1d2f25200157330d663eab65e2dbaff4219ca5ef6dcfaa82386c5f64e9161.html).

The instrument uses controlled excerpts in both layouts. The record view presents newest first. The timeline presents oldest first with a timeline marker. This tests the two presentations together. Every reader sees both layouts on different cases. Rotate A through D across participants and assign before the task. The vote case asks about Sen. Baldwin's two decisions and her reasons. The award case includes events through July 9, 2024. Later opening evidence is absent. Responses stay in memory and the page sends no submissions. Download the HTML and open it in a browser to run it.

Score anonymized answers without the form label. For the vote case, award one point each for the amendment Yea, the passage Nay, the unknown motive and citing both rolls. For the grant case, award one point each for the federal obligation, its amount, the unknown opening at the cutoff and citing the award transaction. Report correctness by case and layout alongside time and confidence. A claim that the site was already operational or that the senator explained her motive is a separate error. [Instrument preflight](reader-preflight.py).

## Replay

Use Python 3.10 with HTTPS access, Poppler's pdftotext for the two source PDFs, and uv. The [manifest](sources.json) pins XML, PDF, model and tokenizer bytes. For the two HTML releases it records the observed full-page digest and verifies the normalized paragraphs containing the quotes, since the sites change unrelated markup. Downloads stay in a temporary directory. The Treasury and station responses can change. The small field selection in Git preserves the measured values.

```bash
set -e
study=docs/connected-records/experiments/evidence-trials
inputs=$(mktemp -d)
python3 "$study/inputs.py" "$inputs"
export OPENBLAS_NUM_THREADS=1 TOKENIZERS_PARALLELISM=false
uv venv --python 3.10 "$inputs/env"
uv pip install --python "$inputs/env/bin/python" numpy==2.2.6 scipy==1.15.3 scikit-learn==1.7.2 tokenizers==0.21.4 onnxruntime==1.22.1
"$inputs/env/bin/python" "$study/text-trials.py" "$inputs" retrieval
"$inputs/env/bin/python" "$study/text-trials.py" "$inputs" lineage
python3 "$study/coalitions.py" "$inputs"
python3 "$study/delivery.py"
```

For a new dated financial and station observation, run `python3 "$study/delivery.py" --refresh` and compare the output with the fixture. For the optional browser check, install Playwright 1.58.0 into the study environment, install its Chromium or pass a local Chromium executable as the first argument to [reader-preflight.py](reader-preflight.py). Browser checks used the local Chromium headless shell.
