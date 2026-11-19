"""Replay the reviewer interaction and check the reader's loaded resources."""
from pathlib import Path
import sys

from playwright.sync_api import sync_playwright


def check_review(page):
    page.select_option(".decision select", label="Accept link")
    note = "The deadline starts on receipt of a valid request."
    page.fill(".decision textarea", note)
    page.locator('[data-key="vote"]').focus()
    page.keyboard.press("Enter")
    page.click(".back")
    assert page.locator(".decision textarea").input_value() == note
    page.click(".decision .primary")
    page.select_option("#case", "impact")
    assert page.locator(".decision textarea").input_value() == ""
    page.select_option(".decision select", label="Insufficient evidence")
    page.fill(".decision textarea", " ")
    page.click(".decision .primary")
    assert page.locator(".decision textarea").evaluate("n => n.validity.customError")
    page.fill(".decision textarea", "The reported effect needs an outcome measure.")
    page.click(".decision .primary")
    page.select_option("#case", "deadline")
    assert "Recorded in this tab · Accept link" in page.locator(".receipt").inner_text()
    page.reload()
    assert page.locator(".decision textarea").input_value() == ""


def check_pages(browser, width, theme):
    directory = Path(__file__).resolve().parent
    page = browser.new_page(viewport={"width": width, "height": 900}, color_scheme=theme)
    requests, errors = [], []
    page.on("request", lambda request: requests.append(request.url))
    page.on("pageerror", lambda error: errors.append(str(error)))
    for view in ("review", "reader"):
        requests.clear()
        page.goto((directory / f"{view}.html").as_uri())
        for mode in ("map", "list"):
            page.click(f'[data-mode="{mode}"]')
            page.click(".related")
            page.click('[data-key="cast"]')
            assert page.locator(".inspector h2").inner_text() == "Dean voted Yea"
            page.click(".related")
            assert page.locator('[data-key="cast"]').count() == 0
            assert page.evaluate("document.documentElement.scrollWidth <= innerWidth")
        if view == "review":
            check_review(page)
        else:
            assert {Path(url).name for url in requests} == {"reader.html", "graph.css", "graph.js", "facts.js"}
            assert page.locator("form, .proposed, textarea").count() == 0
        assert all(url.startswith("file:") for url in requests)
        assert not errors, errors
    page.close()


with sync_playwright() as playwright:
    browser = playwright.chromium.launch(executable_path=sys.argv[1] if len(sys.argv) > 1 else None)
    for width, theme in ((1380, "light"), (860, "light"), (390, "dark"), (320, "light")):
        check_pages(browser, width, theme)
        print(width, theme, "passed")
    browser.close()
