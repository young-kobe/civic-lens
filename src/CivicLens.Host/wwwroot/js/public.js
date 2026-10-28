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
            : response.status === 400 ? "This search link has words or filters that search cannot use. Change them and search again."
            : "Search is not available now. Try again later.";
        const formParams = () => {
            const params = new URLSearchParams();
            for (const [name, value] of new FormData(form)) if (value) params.set(name, value);
            return params;
        };
        const reset = () => {
            nextCursor = null;
            more.hidden = true;
            results.replaceChildren();
        };
        const run = async append => {
            const params = formParams();
            if (append) params.set("cursor", nextCursor); else reset();
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
        let address = location.search;
        const writeAddress = (params, replace) => {
            const text = params.toString();
            const search = text ? `?${text}` : "";
            if (replace || search === location.search) history.replaceState(null, "", location.pathname + search);
            else history.pushState(null, "", location.pathname + search);
            address = search;
        };
        const restoreFromAddress = () => {
            address = location.search;
            const query = new URLSearchParams(address);
            for (const field of Array.from(form.elements).filter(element => element.name)) {
                field.value = query.get(field.name) ?? "";
                if (field instanceof HTMLSelectElement && field.selectedIndex === -1) field.value = "";
            }
            if (formParams().toString() !== "") run(false);
            else { running++; reset(); status.textContent = ""; }
        };
        form.closest("section").hidden = false;
        restoreFromAddress();
        writeAddress(formParams(), true);
        window.addEventListener("popstate", () => { if (location.search !== address) restoreFromAddress(); });
        form.addEventListener("submit", event => { event.preventDefault(); writeAddress(formParams()); run(false); });
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

    const initOfficialsFilter = controls => {
        const list = document.querySelector("[data-officials]");
        const cards = Array.from(list.children);
        const buttons = Array.from(controls.querySelectorAll("[data-filter-value]"));
        const state = controls.querySelector("[data-officials-state]");
        const name = controls.querySelector("[data-officials-name]");
        const status = document.querySelector("[data-officials-status]");
        const plain = text => text.normalize("NFD").replace(/\p{M}/gu, "").toLowerCase();
        let office = "";
        const apply = () => {
            const wanted = plain(name.value.trim());
            let shown = 0;
            for (const card of cards) {
                card.hidden = (office !== "" && card.dataset.office !== office)
                    || (state.value !== "" && card.dataset.state !== state.value)
                    || !plain(card.dataset.name).includes(wanted);
                if (!card.hidden) shown++;
            }
            for (const button of buttons) button.setAttribute("aria-pressed", String(button.dataset.filterValue === office));
            status.textContent = shown === 0 ? "No officials match. Change the office, the state, or the name."
                : `Showing ${shown} of ${cards.length} officials`;
        };
        for (const button of buttons) button.addEventListener("click", () => { office = button.dataset.filterValue; apply(); });
        state.addEventListener("change", apply);
        name.addEventListener("input", apply);
        controls.hidden = false;
        apply();
    };

    document.querySelectorAll("[data-officials-filters]").forEach(initOfficialsFilter);
    document.querySelectorAll("[data-search-api]").forEach(initSearchService);
    document.querySelectorAll("[data-report-api]").forEach(initReportForm);
})();
