"""Replay the Deerfield award and station reconciliation on selected source fields."""
from datetime import date
from decimal import Decimal
import json
from pathlib import Path
import sys
from urllib.request import Request, urlopen

AWARD = "ASST_NON_693JJ32440370_069"
AWARD_URL = "https://api.usaspending.gov/api/v2/awards/" + AWARD + "/"
TRANSACTIONS = "https://api.usaspending.gov/api/v2/transactions/"
STATIONS = ("https://developer.nlr.gov/api/alt-fuel-stations/v1/nearest.json?"
            "api_key=DEMO_KEY&latitude=42.477&longitude=-72.607&radius=1&fuel_type=ELEC&limit=30")
QUERY = {"award_id": AWARD, "page": 1, "limit": 25, "sort": "action_date", "order": "asc"}


def select(award, transactions, stations, observation_date):
    if award["generated_unique_award_id"] != AWARD or transactions["page_metadata"]["hasNext"]:
        raise ValueError("Wrong award or incomplete transaction page")
    if stations["total_results"] != len(stations["fuel_stations"]):
        raise ValueError("Incomplete station search")
    return {
        "observed": observation_date, "fain": award["fain"], "recipient": award["recipient"]["recipient_name"],
        "project": award["description"], "place": award["place_of_performance"]["city_name"],
        "obligation": str(award["total_obligation"]), "nonfederal": str(award["non_federal_funding"]),
        "total_funding": str(award["total_funding"]), "outlay": str(award["total_outlay"]),
        "transactions": [[t["id"], t["action_date"], str(t["federal_action_obligation"])] for t in transactions["results"]],
        "stations": [{k: s[k] for k in ["id","station_name","street_address","status_code","open_date",
                                         "ev_dc_fast_num","ev_level2_evse_num"]} for s in stations["fuel_stations"]]
    }


def report(data):
    obligation = Decimal(data["obligation"])
    if obligation != sum(Decimal(t[2]) for t in data["transactions"]):
        raise ValueError("Transactions do not reconcile to award")
    if obligation + Decimal(data["nonfederal"]) != Decimal(data["total_funding"]):
        raise ValueError("Funding amounts do not reconcile")
    print("Observation, FAIN, obligated, reported outlay:", data["observed"], data["fain"], obligation, data["outlay"])
    selected = [s for s in data["stations"] if s["street_address"].lower() in {"59 n main st", "59 north main street"}
                and s["status_code"] == "E"]
    print("59 North Main inventory IDs:", sorted(s["id"] for s in selected))
    print("DC fast and Level 2 ports:", *(sum(s[key] or 0 for s in selected)
                                          for key in ["ev_dc_fast_num", "ev_level2_evse_num"]))
    print("Other Leary Lot or Town Hall rows:", [(s["id"],s["open_date"]) for s in data["stations"]
          if s not in selected and ("LEARY" in s["station_name"] or "TOWN HALL" in s["station_name"])])


def fetch(url, body=None):
    request = Request(url, data=json.dumps(body).encode() if body is not None else None,
                      headers={"Content-Type": "application/json", "User-Agent": "CivicLens research replay"})
    with urlopen(request, timeout=30) as response:
        return json.load(response)


if __name__ == "__main__":
    if sys.argv[1:] == ["--refresh"]:
        print(json.dumps(select(fetch(AWARD_URL), fetch(TRANSACTIONS, QUERY), fetch(STATIONS),
                                date.today().isoformat()), indent=2))
    else:
        report(json.loads(Path(__file__).with_name("deerfield-sample.json").read_text()))
