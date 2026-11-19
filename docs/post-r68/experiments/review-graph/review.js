const statementSource = {
    name: "Senate office release",
    href: "https://www.klobuchar.senate.gov/public/index.cfm/2025/4/bipartisan-klobuchar-bill-to-protect-online-privacy-and-combat-explicit-deepfakes-passes-congress",
    revision: "384c0da993db7d1c0834a53661bd8bf0983e147a379db4f02b65a4752819e60f",
    revisionType: "Selected paragraphs · SHA-256",
};
const cases = {
    deadline: {
        title: "Removal deadline",
        speaker: "Rep. Madeleine Dean",
        excerpt:
            "Our bill requires platforms to remove these horrifying images and videos from the Internet within 48 hours",
        question: "Does the bill set the deadline Dean describes?",
        detail: "Check the trigger for the 48-hour clock and which platforms the provision covers.",
    },
    impact: {
        title: "Impact on victims",
        speaker: "Sen. Ted Cruz",
        excerpt: "we are sparing victims from repeated trauma",
        question: "Does this provision establish the effect Cruz describes?",
        detail: "Check what the legislative record measures about victims' experiences after a removal request.",
    },
};
const decisions = new Map();
const drafts = new Map();

function showCase() {
    const key = document.querySelector("#case").value;
    const item = cases[key];
    const claim = {
        kind: "Statement",
        title: item.title,
        meta: item.speaker,
        excerpt: item.excerpt,
        detail: "The office release attributes this statement to " + item.speaker + ".",
        source: statementSource,
    };
    const candidate = {
        id: "candidate",
        from: "provision",
        to: "claim",
        verb: "may support",
        proposed: true,
        question: item.question,
        detail: item.detail,
    };
    // The locator distinguishes claims that share a source snapshot.
    const binding = JSON.stringify([
        key,
        candidate.from,
        candidate.to,
        claim.excerpt,
        claim.source.revision,
        sourceNodes.provision.meta,
        sourceNodes.provision.source.revision,
    ]);
    mountEvidence({
        nodes: { ...sourceNodes, claim },
        edges: [candidate, ...sourceEdges],
        first: candidate.id,
        slots: { claim: [1, 1], provision: [3, 1], vote: [1, 3], bill: [3, 3], cast: [1, 5] },
        core: ["claim", "provision", "vote", "bill"],
        heading: "Evidence map",
        contextLabel: "member cast",
        decisionPanel: () => decisionPanel(binding),
    });
}

function decisionPanel(binding) {
    const panel = document.createElement("section");
    panel.className = "decision";
    const previous = decisions.get(binding);
    panel.innerHTML =
        '<h3>Your decision</h3><form><label>Disposition<select required><option value="">Choose a decision</option><option>Accept link</option><option>Reject link</option><option>Insufficient evidence</option></select></label><label>Reason<textarea required maxlength="700" rows="2" placeholder="Which evidence informed your decision?"></textarea></label><button class="primary" type="submit">Record decision</button><p class="fine">This practice decision lasts until you reload.</p></form><p class="receipt" role="status"></p>';
    const form = panel.querySelector("form"),
        select = form.querySelector("select");
    const note = form.querySelector("textarea"),
        receipt = panel.querySelector(".receipt");
    function showDecision(decision) {
        select.value = decision.value;
        note.value = decision.note;
        receipt.textContent = "Recorded in this tab · " + decision.value;
        form.querySelector("button").textContent = "Update decision";
    }
    if (previous) showDecision(previous);
    const draft = drafts.get(binding);
    if (draft) {
        select.value = draft.value;
        note.value = draft.note;
        receipt.textContent = "Draft changes · record when ready";
    }
    function keepDraft() {
        drafts.set(binding, { value: select.value, note: note.value });
        receipt.textContent = "Draft changes · record when ready";
    }
    select.addEventListener("change", keepDraft);
    note.addEventListener("input", keepDraft);
    form.addEventListener("submit", (event) => {
        event.preventDefault();
        if (!note.value.trim()) {
            note.setCustomValidity("Enter a reason for this decision.");
            note.reportValidity();
            return;
        }
        const result = { value: select.value, note: note.value.trim() };
        decisions.set(binding, result);
        drafts.delete(binding);
        showDecision(result);
    });
    note.addEventListener("input", () => note.setCustomValidity(""));
    return panel;
}
document.querySelector("#case").addEventListener("change", showCase);
showCase();
