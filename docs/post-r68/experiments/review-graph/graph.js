// Presentation only. Each entry point supplies the graph it is allowed to display.
function mountEvidence(options) {
    const host = document.getElementById("workspace");
    const { nodes, edges, core, slots } = options;
    let selected = options.first,
        expanded = false;
    let mode = matchMedia("(max-width: 680px)").matches ? "list" : "map";
    host.innerHTML =
        '<div class="work"><section class="map-pane"><div class="pane-head"><h2></h2><div class="switch" role="group" aria-label="Evidence display"><button type="button" data-mode="map">Map</button><button type="button" data-mode="list">List</button></div></div><div class="surface"></div><div class="foot"><span class="counts"></span><button class="related" type="button"></button></div></section><aside class="inspector" aria-label="Selected evidence"></aside></div><p class="sr" role="status"></p>';
    const surface = host.querySelector(".surface"),
        inspector = host.querySelector(".inspector");
    const toggle = host.querySelector(".related");
    host.querySelector(".pane-head h2").textContent = options.heading;

    function element(tag, className, content) {
        const el = document.createElement(tag);
        if (className) el.className = className;
        if (content !== undefined) el.textContent = content;
        return el;
    }

    function pick(id) {
        selected = id;
        highlight();
        showDetails();
        if (matchMedia("(max-width: 680px)").matches) inspector.querySelector("h2").focus();
        host.querySelector(".sr").textContent =
            "Evidence details updated. " + inspector.querySelector("h2").textContent;
    }
    function selectButton(id, style, label) {
        const button = element("button", style);
        button.type = "button";
        button.dataset.key = id;
        button.setAttribute("aria-label", label);
        button.addEventListener("click", () => pick(id));
        return button;
    }
    function highlight() {
        const edge = edges.find((e) => e.id === selected);
        for (const el of surface.querySelectorAll("[data-key]")) {
            el.setAttribute("aria-pressed", String(el.dataset.key === selected));
            const link = edges.find((e) => e.id === el.dataset.key);
            el.classList.toggle("connected", !!link && (link.from === selected || link.to === selected));
            el.classList.toggle("endpoint", !!edge && [edge.from, edge.to].includes(el.dataset.key));
        }
    }
    function place(el, col, row) {
        el.style.gridColumn = col;
        el.style.gridRow = row;
    }
    function renderGraph() {
        const visible = Object.keys(nodes).filter((id) => expanded || core.includes(id));
        const links = edges.filter((e) => visible.includes(e.from) && visible.includes(e.to));
        surface.replaceChildren();
        const map = element("div", mode === "map" ? "map" : "as-list");
        if (mode === "list") map.append(element("h3", "list-head", "Records"));
        for (const id of visible.sort((a, b) => slots[a][1] - slots[b][1] || slots[a][0] - slots[b][0])) {
            const n = nodes[id],
                button = selectButton(id, "node", n.kind + " · " + n.title);
            const kind = element("span", "kind");
            kind.append(element("i", "mark"), document.createTextNode(n.kind));
            button.dataset.kind = n.kind;
            button.append(kind, element("strong", "", n.title), element("span", "meta", n.meta));
            if (mode === "map") place(button, ...slots[id]);
            map.append(button);
        }
        if (mode === "list") map.append(element("h3", "list-head", "Relationships"));
        for (const edge of links) {
            const full = nodes[edge.from].title + " → " + edge.verb + " → " + nodes[edge.to].title;
            const button = selectButton(edge.id, "edge" + (edge.proposed ? " proposed" : ""), full);
            if (mode === "map") {
                const [x1, y1] = slots[edge.from],
                    [x2, y2] = slots[edge.to];
                const vertical = x1 === x2;
                button.classList.add(vertical ? "vertical" : "horizontal");
                button.dataset.direction = vertical ? (y2 > y1 ? "down" : "up") : x2 > x1 ? "right" : "left";
                place(button, (x1 + x2) / 2, (y1 + y2) / 2);
                button.append(element("span", "", edge.verb));
            } else {
                button.append(element("span", "", full));
            }
            map.append(button);
        }
        surface.append(map);
        const switches = host.querySelectorAll("[data-mode]");
        for (const button of switches)
            button.setAttribute("aria-pressed", String(button.dataset.mode === mode));
        toggle.textContent = (expanded ? "Hide " : "Show ") + options.contextLabel;
        toggle.setAttribute("aria-expanded", String(expanded));
        host.querySelector(".counts").textContent = visible.length + " records · " + links.length + " links";
        highlight();
    }

    function sourceItem(n, showDetail = false) {
        const box = element("section", "source");
        box.append(element("h3", "", n.kind + " · " + n.meta));
        if (n.excerpt) box.append(element("blockquote", "", "“" + n.excerpt + "”"));
        if (showDetail || !n.excerpt) box.append(element("p", "", n.detail));
        const link = element("a", "citation", n.source.name + " ↗");
        link.href = n.source.href;
        link.target = "_blank";
        link.rel = "noopener noreferrer";
        link.setAttribute("aria-label", n.source.name + ", " + n.title + ", opens in a new tab");
        box.append(link);
        return box;
    }

    function showDetails() {
        const edge = edges.find((e) => e.id === selected);
        const item = nodes[selected];
        host.querySelector(".work > .decision")?.remove();
        inspector.replaceChildren();
        const badge = element(
            "span",
            "state",
            edge?.proposed ? "Proposed link" : edge ? "Source link" : item.kind,
        );
        if (edge?.proposed) badge.classList.add("proposal");
        inspector.append(
            badge,
            element(
                "h2",
                "",
                edge
                    ? edge.question ||
                          nodes[edge.from].title + " · " + edge.verb + " · " + nodes[edge.to].title
                    : item.title,
            ),
        );
        inspector.querySelector("h2").tabIndex = -1;
        if (edge) inspector.append(element("p", "lead", edge.detail));
        const records = edge
            ? edge.proposed
                ? [nodes[edge.to], nodes[edge.from]]
                : [nodes[edge.from], nodes[edge.to]]
            : [item];
        for (const n of records) inspector.append(sourceItem(n, !edge || n.showQualification));
        const versions = element("details", "versions");
        versions.append(element("summary", "", "Source versions"));
        for (const n of records) {
            versions.append(
                element("p", "", n.meta + " · " + (n.source.revisionType || "Source XML · SHA-256")),
                element("code", "", n.source.revision),
            );
        }
        inspector.append(versions);
        const browse = element("button", "browse", "Browse evidence");
        browse.type = "button";
        browse.onclick = () => host.querySelector("[data-mode][aria-pressed=true]").focus();
        inspector.append(browse);
        if (edge?.proposed && options.decisionPanel)
            host.querySelector(".work").append(options.decisionPanel());
        else if (options.decisionPanel) {
            const back = element("button", "back", "Return to proposed link");
            back.type = "button";
            back.onclick = () => {
                pick(options.first);
                surface.querySelector('[aria-pressed="true"]')?.focus();
            };
            inspector.append(back);
        }
    }
    for (const b of host.querySelectorAll("[data-mode]"))
        b.onclick = () => {
            mode = b.dataset.mode;
            renderGraph();
        };
    toggle.onclick = () => {
        expanded = !expanded;
        const edge = edges.find((e) => e.id === selected);
        if (
            !expanded &&
            (edge ? !core.includes(edge.from) || !core.includes(edge.to) : !core.includes(selected))
        )
            pick(options.first);
        renderGraph();
    };
    renderGraph();
    showDetails();
}
