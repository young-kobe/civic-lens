// Only the reader entry imports this export. The four inputs are public source records.
const positions = { vote: [1, 1], bill: [3, 1], cast: [1, 3], provision: [3, 3] };
const readerView = mountEvidence({
    nodes: sourceNodes,
    edges: sourceEdges,
    first: "measure",
    slots: positions,
    core: ["vote", "bill", "provision"],
    heading: "Evidence map",
    contextLabel: "member cast",
    onSelection(id) {
        const prefix = Object.hasOwn(sourceNodes, id) ? "record" : "link";
        const hash = "#" + prefix + "=" + id;
        if (location.hash !== hash) history.pushState(null, "", hash);
        markReaderPath();
    },
});

document.querySelector(".work").prepend(document.getElementById("reading").content.cloneNode(true));
decorateIcons(document.querySelector(".reading"));
orderPanes();
document.querySelector(".reading a").onclick = (event) => {
    if (event.currentTarget.hash === location.hash) readerView.openSelection("provision");
};

function markReaderPath() {
    for (const link of rail.querySelectorAll("nav a")) {
        if (link.closest("li") && link.hash === (location.hash || "#top"))
            link.setAttribute("aria-current", "location");
        else link.removeAttribute("aria-current");
    }
    updatePath();
}
function openReaderRecord(event) {
    const match = /^#(record|link)=([\w-]+)$/.exec(location.hash);
    if (match) {
        const valid =
            match[1] === "record"
                ? Object.hasOwn(sourceNodes, match[2])
                : sourceEdges.some((e) => e.id === match[2]);
        if (valid) readerView.openSelection(match[2], false);
    } else if (event && (!location.hash || location.hash === "#top")) {
        readerView.openSelection("measure", false, false);
    }
    markReaderPath();
}
window.addEventListener("hashchange", openReaderRecord);
openReaderRecord();
for (const link of rail.querySelectorAll("[data-record]")) {
    link.addEventListener("click", () => {
        if (link.hash === location.hash) readerView.openSelection(link.dataset.record);
    });
}

document.getElementById("export").addEventListener("click", () => {
    const nodes = Object.entries(sourceNodes).map(([id, n]) => ({
        id,
        type: "text",
        x: (positions[id][0] - 1) * 300,
        y: (positions[id][1] - 1) * 185,
        width: 420,
        height: 270,
        text: [
            `## ${n.title}`,
            `${n.kind} · ${n.meta}`,
            n.excerpt && `> ${n.excerpt}`,
            n.detail,
            `[${n.source.name}](${n.source.href})`,
            `Source XML SHA-256: \`${n.source.revision}\``,
        ]
            .filter(Boolean)
            .join("\n\n"),
    }));
    const edges = sourceEdges.map((e) => ({
        id: e.id,
        fromNode: e.from,
        toNode: e.to,
        toEnd: "arrow",
        label: e.verb,
    }));
    const blob = new Blob([JSON.stringify({ nodes, edges }, null, 2)], { type: "application/json" });
    const url = URL.createObjectURL(blob);
    const download = document.createElement("a");
    download.href = url;
    download.download = "Civic Lens - S146 House passage.canvas";
    download.click();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
});
