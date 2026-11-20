"""Read the four pinned VA workbooks and compare weekly national backlog counts."""
import hashlib
import sys
from pathlib import Path

from openpyxl import load_workbook

# VA reports the inauguration-day count in its November 18, 2025 news release.
# See va-backlog.md for the primary source and its population definition.
INAUGURATION_BACKLOG = 264_717

# The baseline weeks bracket inauguration. Each end week precedes a dated statement.
REPORTS = (
    ("MMWR-01-18-2025.xlsm", "January 18, 2025", "33d85b1ce3941a1509034511c97c9a6e29b6d7861326d32e3ff7de17bb455241"),
    ("MMWR-01-25-2025.xlsm", "January 25, 2025", "e47dd7b93db06eb4b9181a91f11b164aeaa8b430816e7741282f0fd2806f5818"),
    ("MMWR-10-10-2026.xlsx", "October 10, 2026", "3db643b78edf6210f04b52fc4986f529c3c504e089877e2372458f74bef80ebc"),
    ("MMWR-11-07-20261.xlsx", "November 07, 2026", "6f6ef8fa3cc54edba6cc876812288d4dda24d6949eba07fb259818a33699652a"),
)


def read_backlog(path, date, digest):
    if hashlib.sha256(path.read_bytes()).hexdigest() != digest:
        raise ValueError(f"{path.name}: workbook hash differs from the measured version")
    workbook = load_workbook(path, read_only=True, data_only=True)
    try:
        headers, period, totals = workbook["Transformation"].iter_rows(min_row=3, max_row=5)
        if "National View" not in [cell.value for cell in headers]:
            raise ValueError("Missing national scope")
        if f"Reporting through {date}" not in [cell.value for cell in period]:
            raise ValueError("Unexpected reporting period")
        if "Compensation and Pension Rating Bundle" not in [cell.value for cell in totals]:
            raise ValueError("Missing rating bundle total")
        column, = [i for i, cell in enumerate(headers) if cell.value == "# Pending > 125"]
        cell = totals[column]
        if type(cell.value) is not int or cell.value <= 0:
            raise ValueError("Expected a positive backlog count in each pilot workbook")
        print(f"{date}: {cell.coordinate} = {cell.value:,}")
        return cell.value
    finally:
        workbook.close()


def main(directory):
    counts = [read_backlog(directory / name, date, digest) for name, date, digest in REPORTS]
    baselines = [(REPORTS[0][1], counts[0]), ("January 20, 2025 (VA report)", INAUGURATION_BACKLOG),
                 (REPORTS[1][1], counts[1])]
    for date, baseline in baselines:
        for end_date, end_count in ((REPORTS[2][1], counts[2]), (REPORTS[3][1], counts[3])):
            decline = 100 * (baseline - end_count) / baseline
            print(f"{date} to {end_date}: {decline:.2f}% decline")


if __name__ == "__main__":
    main(Path(sys.argv[1]))
