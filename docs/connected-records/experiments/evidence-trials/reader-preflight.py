"""Exercise all four assignments locally, without collecting participant data."""
import json
from pathlib import Path
import sys

from playwright.sync_api import sync_playwright


with sync_playwright() as tools:
    browser = tools.chromium.launch(executable_path=sys.argv[1] if len(sys.argv) > 1 else None)
    for width in (390, 1200):
        seen = {}
        assignments = []
        for form in "ABCD":
            page = browser.new_page(viewport={"width": width, "height": 800})
            page.goto(Path(__file__).with_name("reader-study.html").resolve().as_uri())
            page.select_option("#form", form)
            page.click("#begin")
            for step in range(2):
                title = page.locator("#title").inner_text()
                facts = sorted(page.locator("article").all_inner_texts())
                assert seen.setdefault(title, facts) == facts
                assert page.evaluate("document.documentElement.scrollWidth <= innerWidth")
                assert page.locator("#title").evaluate("node => node === document.activeElement")
                page.click("#advance")
                assert not page.locator("#reply").evaluate("node => node.validity.valid")
                page.fill("#reply", "   ")
                page.select_option("#confidence", "Low")
                page.click("#advance")
                assert page.locator("#reply").evaluate("node => node.validity.customError")
                page.fill("#reply", "Automation check")
                page.click("#advance")
            answers = json.loads(page.locator("#summary").inner_text())
            assert len(answers) == 2 and all(a["form"] == form and a["seconds"] >= 0
                                             and a["answer"] == "Automation check" for a in answers)
            assignments.append(tuple(tuple(a["task"]) for a in answers))
            page.close()
        assert len(set(assignments)) == 4
        for step in range(2):
            assert {a[step] for a in assignments} == {(c, l) for c in ("grant","vote") for l in ("records","timeline")}
        print(width, "pixels: four balanced assignments passed with identical case facts")
    browser.close()
