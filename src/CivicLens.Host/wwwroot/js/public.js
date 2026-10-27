(() => {
    const markStart = "\uE000", markEnd = "\uE001";
    const snippetNodes = snippet => snippet.split(markStart).flatMap((part, index) => {
        if (index === 0) return [part];
        const [match, ...rest] = part.split(markEnd);
        const mark = document.createElement("mark");
        mark.textContent = match;
        return [mark, rest.join(markEnd)];
    });
    const initSearchService = form => {
        const api = form.dataset.searchApi;
        const status = form.querySelector("[data-search-status]");
        const results = form.querySelector("[data-search-results]");
        const more = form.querySelector("[data-search-more]");
        const kindLabels = new Map(Array.from(form.elements.namedItem("kind").options, option => [option.value, option.text]));
        let nextCursor = null;
        let running = 0;
        const resultItem = hit => {
            const item = document.createElement("li");
            const link = document.createElement("a");
            link.className = "cell-title";
            link.href = hit.pagePath;
            link.textContent = hit.headline;
            const facts = document.createElement("span");
            facts.className = "cell-sub";
            facts.textContent = [kindLabels.get(hit.kind), ...hit.officials.map(official => official.name), ...hit.issueIds, hit.eventDate]
                .filter(Boolean).join(" \u00B7 ");
            item.append(link, facts);
            if (hit.snippet) {
                const snippet = document.createElement("p");
                snippet.className = "cell-sub";
                snippet.append(...snippetNodes(hit.snippet));
                item.append(snippet);
            }
            return item;
        };
        const failure = response => response.status === 429 ? "Too many searches. Try again in a minute."
            : response.status === 404 ? "This release is not indexed yet."
            : "Search is not available now. Try again later.";
        const run = async append => {
            const params = new URLSearchParams();
            for (const [name, value] of new FormData(form)) if (value) params.set(name, value);
            if (append) params.set("cursor", nextCursor);
            const mine = ++running;
            status.textContent = "Searching.";
            let response;
            try { response = await fetch(`${api}/v1/search?${params}`); }
            catch { if (mine === running) status.textContent = failure({ status: 0 }); return; }
            if (mine !== running) return;
            if (!response.ok) { status.textContent = failure(response); return; }
            const page = await response.json();
            if (mine !== running) return;
            const items = page.hits.map(resultItem);
            if (append) results.append(...items); else results.replaceChildren(...items);
            nextCursor = page.nextCursor;
            more.hidden = nextCursor === null;
            status.textContent = results.children.length === 0 ? "No records match." : `Showing ${results.children.length} records from release ${page.release}.`;
        };
        form.closest("section").hidden = false;
        form.addEventListener("submit", event => { event.preventDefault(); run(false); });
        form.addEventListener("input", () => { nextCursor = null; more.hidden = true; });
        more.addEventListener("click", () => run(true));
    };

    const reportMessages = {
        202: ["ok", "Your report is saved. A person will check the record. You can go back to the record now."],
        400: ["bad", "The service did not accept the report. Choose a category, add details if the category needs them, complete the check that you are not a robot, and send the report again."],
        404: ["bad", "The service does not know this version of the record. A new release may not be indexed yet. Try again later."],
        413: ["bad", "The report is too large. Shorten the details and send the report again."],
        429: ["bad", "You sent too many reports. Wait one hour and try again."],
        503: ["bad", "Reports are not available now. The service may be closed for today, or the check that you are not a robot may be down. Try again tomorrow."]
    };
    const initReportForm = form => {
        const query = new URLSearchParams(location.search);
        const queryInputs = Array.from(form.querySelectorAll("[data-report-query]"));
        const [recordId, revision] = queryInputs.map(input => query.get(input.name) ?? "");
        const panel = form.closest("[data-report-form-panel]");
        const messages = { ok: document.querySelector("[data-report-result]"), bad: document.querySelector("[data-report-failure]") };
        const category = form.querySelector("#report-category");
        const text = form.querySelector("#report-text");
        const submit = form.querySelector("[type=submit]");
        const show = (tone, message) => {
            for (const [key, region] of Object.entries(messages)) {
                region.className = key === tone ? `notice notice-${tone}` : "";
                region.textContent = key === tone ? message : "";
            }
            messages[tone].focus();
        };
        const validRecord = /^([0-9a-f]{32}|[0-9a-f]{64})$/.test(recordId) && /^[0-9]+$/.test(revision)
            && Number(revision) >= 1 && Number(revision) <= 2147483647;
        if (!validRecord) { document.querySelector("[data-report-missing]").hidden = false; return; }
        const back = document.querySelector("[data-report-record]");
        back.href = form.dataset.recordPage.replace("{id}", recordId);
        back.textContent = "Back to the record";
        queryInputs.forEach(input => { input.value = query.get(input.name); });
        const checkText = () => {
            const required = category.value === category.dataset.textRequired;
            text.required = required;
            text.setCustomValidity(required && text.value.trim() === "" ? "Add details to explain what is wrong." : "");
        };
        category.addEventListener("change", checkText);
        text.addEventListener("input", checkText);
        form.addEventListener("submit", async event => {
            event.preventDefault();
            submit.disabled = true;
            let outcome = ["bad", "The report did not reach the service. Check your connection and send the report again."];
            try {
                const response = await fetch(form.dataset.reportApi, { method: "POST", body: new URLSearchParams(new FormData(form)) });
                outcome = reportMessages[response.status] ?? ["bad", `The service answered with the unexpected status ${response.status}. Try again later.`];
            } catch {}
            show(...outcome);
            if (outcome[0] === "ok") panel.hidden = true;
            else { window.turnstile?.reset(); submit.disabled = false; }
        });
        panel.hidden = false;
    };

    document.querySelectorAll("[data-search-api]").forEach(initSearchService);
    document.querySelectorAll("[data-report-api]").forEach(initReportForm);
})();
