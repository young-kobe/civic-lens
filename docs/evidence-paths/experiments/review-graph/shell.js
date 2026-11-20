// Shared navigation for the two local prototypes. Each entry supplies its own record set.
const paths = {
    review: "M3 13h5l1.5 3h5L16 13h5 M5 5h14l2 8v6H3v-6z",
    links: "M9.5 14.5l5-5 M8 11l-2 2a3.5 3.5 0 0 0 5 5l2-2 M16 13l2-2a3.5 3.5 0 0 0-5-5l-2 2",
    bill: "M6 3h9l4 4v14H6z M14 3v5h5 M9 13h7 M9 17h7",
    overview: "M3 10 12 3l9 7 M5 9v12h14V9 M10 21v-7h4v7",
    sources: "M4 5h6l2 2 2-2h6v14h-6l-2 2-2-2H4z M12 7v14",
    search: "M18 11a7 7 0 1 1-14 0 7 7 0 0 1 14 0 M20 20l-4-4",
    menu: "M3 6h18 M3 12h18 M3 18h18",
    close: "m6 6 12 12 M18 6 6 18",
    export: "M12 3v12 M7 10l5 5 5-5 M4 15v5h16v-5",
    vote: "M4 4h16v16H4z M8 12l3 3 5-6",
    statement: "M5 5h14v10H9l-4 4z M9 9h6 M9 12h4",
    provision: "M4 4h16v16H4z M8 8h8 M8 12h8 M8 16h4",
    member: "M16 7a4 4 0 1 1-8 0 4 4 0 0 1 8 0 M4 21v-2a8 6 0 0 1 16 0v2",
};
const recordIcons = {
    "Bill edition": "bill",
    Provision: "provision",
    "Roll call": "vote",
    "Member cast": "member",
    Statement: "statement",
};
function icon(name) {
    const ns = "http://www.w3.org/2000/svg";
    const svg = document.createElementNS(ns, "svg");
    svg.setAttribute("viewBox", "0 0 24 24");
    svg.setAttribute("aria-hidden", "true");
    svg.classList.add("glyph");
    const path = document.createElementNS(ns, "path");
    path.setAttribute("d", paths[name]);
    svg.append(path);
    return svg;
}
function decorateIcons(root) {
    for (const slot of root.querySelectorAll("[data-icon]")) slot.replaceWith(icon(slot.dataset.icon));
}
decorateIcons(document);

const rail = document.getElementById("rail"),
    drawer = document.getElementById("drawer");
const opener = document.getElementById("nav-open"),
    small = matchMedia("(max-width: 1023px)");
function placeRail() {
    if (small.matches) drawer.append(rail);
    else {
        if (drawer.open) drawer.close();
        drawer.before(rail);
    }
}
placeRail();
small.addEventListener("change", placeRail);
opener.onclick = () => drawer.showModal();
document.getElementById("nav-close").onclick = () => drawer.close();
drawer.addEventListener("close", () => {
    if (small.matches) opener.focus({ preventScroll: true });
});
for (const button of rail.querySelectorAll(".chevron")) {
    button.onclick = () => {
        const children = document.getElementById(button.getAttribute("aria-controls"));
        children.hidden = !children.hidden;
        button.setAttribute("aria-expanded", String(!children.hidden));
    };
}
function revealNav(link) {
    for (let branch = link?.closest("ul"); branch; branch = branch.parentElement.closest("ul")) {
        if (!branch.id) continue;
        branch.hidden = false;
        rail.querySelector('[aria-controls="' + branch.id + '"]').setAttribute("aria-expanded", "true");
    }
}
function updatePath() {
    const links = [...rail.querySelectorAll("li a")];
    const target = links.find((a) => a.hash === (location.hash || "#top"));
    const library = location.hash.startsWith("#link=") ? document.getElementById("library-nav") : null;
    const selected = target || library?.querySelector("a") || rail.querySelector("[data-case][aria-current]");
    for (const group of rail.querySelectorAll(".nav-group"))
        group.classList.toggle("on-path", group.contains(selected));
    revealNav(selected);
}
rail.addEventListener("click", (e) => {
    if (e.target.closest("a") && drawer.open) drawer.close();
});
window.addEventListener("hashchange", updatePath);
const finder = document.getElementById("finder"),
    findButton = document.getElementById("find");
const query = finder.querySelector("input"),
    hits = finder.querySelector(".hits");
findButton.onclick = () => {
    query.value = "";
    query.oninput();
    finder.showModal();
    query.focus();
};
finder.querySelector("[data-close]").onclick = () => finder.close();
for (const dialog of [finder, drawer]) {
    dialog.addEventListener("click", (e) => {
        if (e.target !== dialog) return;
        const bounds = dialog.getBoundingClientRect();
        if (
            e.clientX < bounds.left ||
            e.clientX > bounds.right ||
            e.clientY < bounds.top ||
            e.clientY > bounds.bottom
        )
            dialog.close();
    });
}
document.addEventListener("keydown", (e) => {
    if (
        e.key === "/" &&
        !e.ctrlKey &&
        !e.altKey &&
        !e.metaKey &&
        !document.querySelector("dialog[open]") &&
        !e.target.matches("input,textarea,select,[contenteditable=true]")
    ) {
        e.preventDefault();
        findButton.click();
    }
});
finder.addEventListener("keydown", (e) => {
    const buttons = [...hits.querySelectorAll("button")];
    if (!buttons.length) return;
    const index = buttons.indexOf(document.activeElement);
    if (e.key === "ArrowDown" || e.key === "ArrowUp") {
        e.preventDefault();
        const next =
            index < 0
                ? e.key === "ArrowDown"
                    ? 0
                    : buttons.length - 1
                : (index + (e.key === "ArrowDown" ? 1 : -1) + buttons.length) % buttons.length;
        buttons[next].focus();
    } else if (e.key === "Enter" && document.activeElement === query) {
        e.preventDefault();
        buttons[0].click();
    }
});
function setupFinder(nodes, openRecord) {
    query.oninput = () => {
        const words = query.value.toLowerCase().trim().split(/\s+/);
        hits.replaceChildren();
        for (const [id, n] of Object.entries(nodes)) {
            const search = [n.kind, n.title, n.meta, n.detail, n.excerpt].filter(Boolean).join(" ").toLowerCase();
            if (!words.every((word) => search.includes(word))) continue;
            const button = document.createElement("button"),
                label = document.createElement("strong"),
                meta = document.createElement("small");
            label.textContent = n.title;
            meta.textContent = n.kind + " · " + n.meta;
            button.append(icon(recordIcons[n.kind]), label, meta);
            button.onclick = () => {
                finder.close();
                openRecord(id);
            };
            hits.append(button);
        }
        const count = hits.childElementCount;
        finder.querySelector(".result-count").textContent = count
            ? count + (count === 1 ? " matching record" : " matching records")
            : "No matching records. Try a title, person or record type.";
    };
}
