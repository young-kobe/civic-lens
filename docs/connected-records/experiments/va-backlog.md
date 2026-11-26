# Checking the veterans backlog claim against weekly data

The November 10 proclamation attributes a decline of more than 70% in the VA claims backlog to the president's second term. An October 15 VA announcement also used that threshold. Can a reader reproduce the number at the time of each statement? This is a research calculation, pending editorial review. [Proclamation](https://www.govinfo.gov/content/pkg/FR-2026-11-16/pdf/2026-23348.pdf) and [VA announcement](https://news.va.gov/press-room/va-in-fy-2026-processes-the-most-claims-ever/).

## Evidence and calculation

VA defines the national backlog as rating claims pending more than 125 days. We took the national Compensation and Pension Rating Bundle total from the Transformation worksheet. VA separately reports **264,717 claims on January 20, 2025**. We used this as the primary baseline and the closest weekly observations on either side to test sensitivity. The inauguration-day value comes from the agency's release. The other four counts come from its workbooks. [VA's dated baseline](https://news.va.gov/press-room/va-reduces-backlog-of-veterans-waiting-for-va-benefits-by-57/). For each statement we used the last listed weekly report before the statement date. Dates below are observation dates in the workbooks. VA lists distribution dates separately. These are agency-reported counts. [Archive](https://department.va.gov/vba/performance-analysis-integrity/detailed-claims-data/) and [VA guide to the measure](https://department.va.gov/vba/performance-analysis-integrity/detailed-claims-data/monday-morning-workload-report-user-guide/).

| Observation | Cell | Claims pending over 125 days | Pinned workbook source |
|---|---|---:|---|
| 2025-01-18 | I5 | 252,406 | [Baseline before inauguration](https://department.va.gov/vba/wp-content/uploads/sites/78/2026/07/MMWR-01-18-2025.xlsm) |
| 2025-01-25 | I5 | 257,253 | [Baseline after inauguration](https://department.va.gov/vba/wp-content/uploads/sites/78/2026/07/MMWR-01-25-2025.xlsm) |
| 2026-10-10 | K5 | 73,948 | [Week before the VA announcement](https://department.va.gov/vba/wp-content/uploads/sites/78/2026/10/MMWR-10-10-2026.xlsx) |
| 2026-11-07 | K5 | 79,546 | [Week before the proclamation](https://department.va.gov/vba/wp-content/uploads/sites/78/2026/11/MMWR-11-07-20261.xlsx) |

Percent decline = 100 × (baseline count minus end count) / baseline count.

| Baseline | By Oct 10 | By Nov 7 |
|---|---:|---:|
| Jan 18 | 70.70% | 68.48% |
| Jan 20, agency daily figure | 72.07% | 69.95% |
| Jan 25 | 71.25% | 69.08% |

**The end week changes the threshold result under all three baselines.** Using VA's inauguration-day figure gives a 72.07% decline by October 10 and a 69.95% decline by November 7. Rounding to a whole percent hides the difference between reaching 70% and exceeding it. The proclamation supplies no exact cutoff. On July 7 VA had already reported a decline above 74% since January 20. A historical milestone and a current level answer different questions. [VA's July report](https://news.va.gov/press-room/va-benefits-claims-backlog-drops-below-70k-for-first-time-since-2020/).

The weekly observations cannot establish the exact daily backlog on November 10. VA supplies both the workbooks and the baseline, so this checks consistency across its publications. The change in the reported stock of pending claims does not identify its cause or the experience of individual veterans.

## Reproduce

Download the four workbooks above to one directory under their original filenames. The [calculation](va-backlog.py) runs offline with Python 3.10 and openpyxl 3.1.5. It checks each workbook's SHA-256, worksheet labels, report date and cached numeric cell. It handles the move from column I to K by reading the header. A changed workbook requires investigation as a new source version. We read archived files on 2026-11-18. The current archive verifies the observation dates, but cannot establish byte-for-byte availability on the original distribution dates.

```sh
uv run --no-project --with openpyxl==3.1.5 python \
  docs/connected-records/experiments/va-backlog.py /path/to/workbooks
```

For a production assessment, bind each cell, workbook digest, population definition, source release date and calculation version. Confirm that VA used the same included claim categories across the comparison, and seek the cutoff and definition used by the speaker. Preserve the October and November observations as separate results. Deduplicate reports that repeat the agency figure. This is one worked case with no model calls or measured classifier accuracy.
