"""Measure time from bill introduction to the first coded House committee report."""
from datetime import date
from pathlib import Path
import sys
from xml.etree import ElementTree

from scipy.stats import CensoredData, ecdf

from inputs import verified


def report_dates(directory):
    cohort = []
    for first in (1001, 4001):
        for number in range(first, first + 32):
            key = f"status{number}.xml"
            bill = ElementTree.fromstring(verified(directory, key)).find("bill")
            if (bill.findtext("congress"), bill.findtext("type"), bill.findtext("number")) != ("118", "HR", str(number)):
                raise ValueError(f"Unexpected bill in {key}")
            start = date.fromisoformat(bill.findtext("introducedDate"))
            reports = {date.fromisoformat(a.findtext("actionDate")) for a in bill.findall("actions/item")
                       if (a.findtext("sourceSystem/code"), a.findtext("actionCode")) == ("9", "5000")}
            if any(day < start for day in reports):
                raise ValueError(f"Report before introduction in {key}")
            cohort.append((number, start, min(reports) if reports else None))
    return cohort


def summarize(cohort, cutoff):
    events, censored = [], []
    for number, start, report in cohort:
        if start > cutoff:
            continue
        if report is not None and report <= cutoff:
            days = (report - start).days
            events.append(days)
            print("Observed report", number, report, days)
        else:
            censored.append((cutoff - start).days)
    size = len(events) + len(censored)
    if size == 0:
        raise ValueError(f"No bills introduced by {cutoff}")
    curve = ecdf(CensoredData(uncensored=events, right=censored)).cdf
    for horizon in (30, 180, 365):
        observed = sum(t <= horizon for t in events)
        unknown = sum(t < horizon for t in censored)
        at_risk = sum(t >= horizon for t in events + censored)
        estimate = f"{curve.evaluate(horizon):.4f}" if at_risk or observed == size else "unavailable"
        print(cutoff, horizon, "days", "events", observed, "unknown", unknown,
              "cohort", size, "at risk at start of day", at_risk,
              "possible event counts", (observed, observed + unknown), "Kaplan-Meier CDF", estimate)


if __name__ == "__main__":
    bills = report_dates(Path(sys.argv[1]))
    for as_of in (date(2023, 9, 30), date(2024, 12, 31)):
        summarize(bills, as_of)
