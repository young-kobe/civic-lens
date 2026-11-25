"""Replay both entries with Python, Playwright and Chromium. Only loopback serves fixtures."""
from functools import partial
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
import json
from pathlib import Path
import sys
from threading import Thread
from urllib.parse import urlsplit

from playwright.sync_api import expect, sync_playwright


DIRECTORY = Path(__file__).resolve().parent
ROOT = DIRECTORY.parents[3]
FONT_FILES = {"archivo.woff2", "figtree.woff2", "gabarito.woff2"}


class AssetHandler(SimpleHTTPRequestHandler):
    def log_message(self, format, *args):
        pass


def nav(page, selector):
    if page.locator("#nav-open").is_visible():
        page.click("#nav-open")
    page.click(selector)


def check_drawer(page):
    if not page.locator("#nav-open").is_visible():
        return
    page.click("#nav-open")
    assert page.locator("#drawer nav").is_visible()
    page.keyboard.press("Escape")
    assert page.locator("#nav-open").evaluate("b => b === document.activeElement")


def check_finder(page):
    page.keyboard.press("/")
    query = page.locator("#finder input")
    query.fill("absentxyz")
    assert page.locator(".hits button").count() == 0
    assert "No matching records" in page.locator(".result-count").inner_text()
    query.fill("member cast")
    query.press("ArrowDown")
    assert page.locator(".hits button").evaluate("b => b === document.activeElement")
    page.keyboard.press("Enter")
    assert page.locator(".inspector h2").inner_text() == "Dean voted Yea"
    assert page.locator(".inspector h2").evaluate("h => h === document.activeElement")
    assert page.locator(".related").get_attribute("aria-expanded") == "true"


def check_review(page):
    nav(page, '[data-case="impact"]')
    expect(page.locator("h1")).to_have_text("Impact on victims")
    page.go_back()
    expect(page.locator("h1")).to_have_text("Platform removal deadline")
    page.select_option(".decision select", label="Accept link")
    note = "The deadline starts on receipt of a valid request."
    page.fill(".decision textarea", note)
    page.locator(".decision textarea").press("End")
    page.keyboard.type("/")
    assert page.locator(".decision textarea").input_value() == note + "/"
    page.locator(".decision textarea").press("Backspace")
    page.click("#find")
    page.locator("#finder input").fill("member cast")
    page.locator("#finder input").press("Enter")
    page.click(".back")
    assert page.locator(".decision textarea").input_value() == note
    page.click(".decision .primary")
    nav(page, '[data-case="impact"]')
    expect(page.locator("h1")).to_have_text("Impact on victims")
    assert page.locator(".decision textarea").input_value() == ""
    page.select_option(".decision select", label="Insufficient evidence")
    page.fill(".decision textarea", " ")
    page.click(".decision .primary")
    assert page.locator(".decision textarea").evaluate("n => n.validity.customError")
    page.fill(".decision textarea", "The reported effect needs an outcome measure.")
    page.click(".decision .primary")
    nav(page, '[data-case="deadline"]')
    expect(page.locator(".receipt")).to_contain_text("Recorded in this tab · Accept link")
    nav(page, '[data-case="impact"]')
    nav(page, 'li [data-section="sources"]')
    page.reload()
    expect(page.locator("h1")).to_have_text("Impact on victims")
    assert page.url.endswith("#case=impact&section=sources")
    assert page.locator(".decision textarea").input_value() == ""
    assert page.locator("#export").count() == 0


def check_reader(page):
    page.goto(page.url.split("#")[0])
    check_finder(page)
    page.go_back()
    expect(page.locator(".inspector h2")).to_have_text("House passage of S. 146")
    page.go_forward()
    expect(page.locator(".inspector h2")).to_have_text("Dean voted Yea")
    assert page.url.endswith("#record=cast")
    assert page.locator(".reading").is_visible()
    # Collapse the active parent. A link opened outside the menu should reveal the ancestor.
    mobile = page.locator("#nav-open").is_visible()
    if mobile:
        page.click("#nav-open")
    toggle = page.locator('[aria-controls="library-nav"]')
    page.click('[aria-controls="legislative-nav"]')
    toggle.focus()
    page.keyboard.press("Space")
    assert toggle.get_attribute("aria-expanded") == "false"
    if mobile:
        page.keyboard.press("Escape")
    page.click(".reading a")
    expect(toggle).to_have_attribute("aria-expanded", "true")
    expect(page.locator('[aria-controls="legislative-nav"]')).to_have_attribute("aria-expanded", "true")
    assert page.locator('[data-record="provision"]').get_attribute("aria-current") == "location"
    page.go_back()
    expect(page.locator(".inspector h2")).to_have_text("Dean voted Yea")
    page.reload()
    assert page.locator('[data-key="cast"]').get_attribute("aria-pressed") == "true"
    nav(page, '[data-record="vote"]')
    assert page.locator(".tally figcaption").inner_text().endswith("433 members")
    assert page.locator(".vote-key li strong").all_text_contents() == ["409", "2", "0", "22"]
    if page.locator(".related").get_attribute("aria-expanded") == "true":
        page.click(".related")
    with page.expect_download() as event:
        page.click("#export")
    download = event.value
    assert download.suggested_filename.endswith(".canvas")
    raw = download.path().read_text()
    canvas = json.loads(raw)
    expected = {"vote", "bill", "cast", "provision"}
    assert {n["id"] for n in canvas["nodes"]} == expected and len(canvas["nodes"]) == 4
    for node in canvas["nodes"]:
        assert node["type"] == "text"
        assert all(type(node[k]) is int for k in ("x", "y", "width", "height"))
        assert node["width"] > 0 and node["height"] > 0
        is_vote = node["id"] in {"vote", "cast"}
        digest = "7e3474ac0c18abefed41bdae758f7ef99d59cfde98d26a52ad169023cdf4da27" if is_vote else "d9426c01521a9087e085bbab2a4b472dd5a2f347514116612635151c8b99f0b4"
        assert digest in node["text"] and ("clerk.house.gov" if is_vote else "govinfo.gov") in node["text"]
    assert {(e["fromNode"], e["label"], e["toNode"]) for e in canvas["edges"]} == {
        ("cast", "recorded in", "vote"), ("vote", "voted on", "bill"), ("provision", "part of", "bill")}
    assert all(e["toEnd"] == "arrow" for e in canvas["edges"])
    ids = [obj["id"] for obj in canvas["nodes"] + canvas["edges"]]
    assert len(set(ids)) == len(ids) == 7
    assert not any(text in raw for text in ("may support", "sparing victims", "Senate office release", "candidate"))
    assert not page.locator("form, .proposed, textarea").count()


def check_pages(browser, address, width, theme):
    page = browser.new_page(viewport={"width": width, "height": 900}, color_scheme=theme)
    page.set_default_timeout(4000)
    errors, requests = [], []
    page.on("pageerror", lambda error: errors.append(str(error)))
    page.on("request", lambda request: requests.append(urlsplit(request.url)))
    for view in ("review", "reader"):
        requests.clear()
        page.goto(f"{address}/{view}.html")
        page.evaluate("document.fonts.ready")
        assert page.evaluate("['Archivo','Gabarito','Figtree'].every(f => document.fonts.check('16px ' + f))")
        check_drawer(page)
        if width <= 760:
            assert page.locator(".work > :first-child").get_attribute("id") == "sources"
            assert page.locator(".work > :last-child").get_attribute("id") == "evidence"
        assert page.locator('[data-mode="list"]').get_attribute("aria-pressed") == str(width <= 760).lower()
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
            check_reader(page)
        names = {Path(u.path).name for u in requests}
        assert names == {f"{view}.html", f"{view}.js", "shell.js", "facts.js", "graph.js", "graph.css"} | FONT_FILES, names
        assert all(u.netloc == urlsplit(address).netloc for u in requests)
        assert not errors, errors
    page.close()


if __name__ == "__main__":
    with ThreadingHTTPServer(("127.0.0.1", 0), partial(AssetHandler, directory=ROOT)) as server:
        Thread(target=server.serve_forever, daemon=True).start()
        address = f"http://127.0.0.1:{server.server_port}/{DIRECTORY.relative_to(ROOT)}"
        try:
            with sync_playwright() as playwright:
                browser = playwright.chromium.launch(executable_path=sys.argv[1] if len(sys.argv) > 1 else None)
                for width, theme in ((1280, "light"), (1280, "dark"), (860, "light"), (390, "dark"), (360, "light"), (360, "dark"), (320, "light")):
                    check_pages(browser, address, width, theme)
                    print(width, theme, "passed", flush=True)
                page = browser.new_page(java_script_enabled=False)
                page.goto(address + "/reader.html")
                assert "House passes" in page.locator("h1").inner_text()
                assert page.locator("noscript a").is_visible()
                browser.close()
        finally:
            server.shutdown()
