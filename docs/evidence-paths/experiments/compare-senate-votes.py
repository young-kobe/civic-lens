"""Compare Senate member casts on two different questions about the same bill."""
import json
import sys
from collections import Counter
from pathlib import Path

from bill_xml import read_xml


def member_casts(roll):
    casts = {}
    for member in roll.findall("members/member"):
        identifier = member.findtext("lis_member_id")
        cast = member.findtext("vote_cast")
        if not identifier or identifier in casts or cast not in {"Yea", "Nay", "Present", "Not Voting"}:
            raise ValueError("Invalid or repeated Senate member row")
        casts[identifier] = cast
    totals = Counter(casts.values())
    for tag, cast in [("yeas", "Yea"), ("nays", "Nay"), ("present", "Present"), ("absent", "Not Voting")]:
        if totals[cast] != int(roll.findtext(f"count/{tag}") or 0):
            raise ValueError(f"Member rows disagree with the Senate {tag} count")
    return casts


def compare(first, passage):
    bill = passage.findtext("document/document_name")
    if (not bill or bill != first.findtext("amendment/amendment_to_document_number")
            or first.findtext("congress") != passage.findtext("congress")
            or first.findtext("session") != passage.findtext("session")
            or first.findtext("question") not in {"On the Motion", "On the Amendment"}
            or passage.findtext("question") != "On Passage of the Bill"):
        raise ValueError("Expected a bill-related motion or amendment and passage of the same bill")
    earlier, later = member_casts(first), member_casts(passage)
    pairs = Counter((earlier.get(i, "No member row"), later.get(i, "No member row"))
                    for i in earlier.keys() | later.keys())
    for roll in [first, passage]:
        print(roll.findtext("vote_date"), roll.findtext("vote_question_text"), roll.findtext("vote_result_text"))
    for (a, b), count in sorted(pairs.items()):
        print(f"{a} to {b}: {count}")
    comparable = sum(pairs[a, b] for a in ("Yea", "Nay") for b in ("Yea", "Nay"))
    different = pairs["Nay", "Yea"] + pairs["Yea", "Nay"]
    print(f"Different among senators casting Yea or Nay on both: {different}/{comparable}")


def main(directory):
    study = json.loads(Path(__file__).with_name("issue-cases.json").read_text())
    hashes = {key: study["inputs"][key]["sha256"] for key in ("senate-314", "senate-325", "senate-7")}
    hashes["senate-3"] = "f099e753743dc41ebd73aeb6164bb69cb1a52033859c1123fc8ea9ffe77fe94d"
    rolls = {key: read_xml(directory, key, digest) for key, digest in hashes.items()}
    compare(rolls["senate-314"], rolls["senate-325"])
    compare(rolls["senate-3"], rolls["senate-7"])


if __name__ == "__main__":
    main(Path(sys.argv[1]))
