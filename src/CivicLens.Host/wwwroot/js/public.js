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
        const dropCursor = () => {
            nextCursor = null;
            more.hidden = true;
        };
        const reset = () => {
            dropCursor();
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
            if (replace || search === location.search) history.replaceState(null, "", location.pathname + search + location.hash);
            else history.pushState(null, "", location.pathname + search + location.hash);
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
        form.addEventListener("input", dropCursor);
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

    const initRecordFilters = panel => {
        const rows = Array.from(panel.querySelectorAll("tbody tr"));
        if (rows.length === 0) return;
        const tabs = panel.querySelector("[data-record-filters]");
        const kindButtons = tabs ? Array.from(tabs.querySelectorAll("[data-filter-value]")) : [];
        const counts = Array.from(document.querySelectorAll("[data-count-filter]"));
        const status = panel.querySelector("[data-filter-status]");
        const notice = panel.querySelector("[data-filter-notice]");
        const clearButton = notice?.querySelector("[data-filter-clear]");
        let kind = "", issue = null;
        const hides = row => (kind !== "" && !row.classList.contains(kind))
            || (issue !== null && !(row.dataset.filterKeys ?? "").split(" ").includes(issue.dataset.countFilter));
        const apply = () => {
            let shown = 0;
            for (const row of rows) {
                row.hidden = hides(row);
                if (!row.hidden) shown++;
            }
            for (const button of kindButtons) button.setAttribute("aria-pressed", String(button.dataset.filterValue === kind));
            for (const count of counts) count.querySelector("button").setAttribute("aria-pressed", String(count === issue));
            if (notice) {
                notice.querySelector("[data-filter-text]").textContent = issue?.dataset.filterShows ?? "";
                notice.hidden = issue === null;
            }
            const total = rows.length;
            const filtered = kind !== "" || issue !== null;
            status.textContent = !filtered ? "" : `Showing ${shown.toLocaleString("en-US")} of ${total.toLocaleString("en-US")} ${total === 1 ? "record" : "records"}`
                + (shown === 0 ? `. No record matches both filters. Use ${clearButton.textContent} to see every record.` : "");
        };
        for (const count of counts) {
            const button = document.createElement("button");
            button.type = "button";
            button.className = "count-filter";
            button.textContent = count.textContent;
            button.setAttribute("aria-label", count.dataset.filterLabel);
            count.replaceChildren(button);
            button.addEventListener("click", () => { issue = issue === count ? null : count; apply(); });
        }
        for (const button of kindButtons) button.addEventListener("click", () => { kind = button.dataset.filterValue; apply(); });
        clearButton?.addEventListener("click", () => {
            const pressed = issue?.querySelector("button");
            kind = "";
            issue = null;
            apply();
            pressed?.focus();
        });
        if (tabs) tabs.hidden = false;
        apply();
    };

    const codeUnitOrder = (a, b) => (a < b ? -1 : a > b ? 1 : 0);
    const sortKinds = {
        text: { first: "asc", words: { asc: "A to Z", desc: "Z to A" }, compare: (a, b) => codeUnitOrder(a.toLowerCase(), b.toLowerCase()) },
        number: { first: "desc", words: { desc: "most first", asc: "fewest first" }, compare: (a, b) => Number(a) - Number(b) },
        date: { first: "desc", words: { desc: "newest first", asc: "oldest first" }, compare: codeUnitOrder }
    };
    const months = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
    const opposite = direction => (direction === "asc" ? "desc" : "asc");
    let sortableTables = 0;
    const sortFailure = status => status === 429 ? "Too many requests. Try again in a minute." : "Sorting is not available now. Try again later.";
    const hitCells = {
        record: (hit, source) => {
            const cell = document.createElement("td");
            cell.className = "cell-main";
            cell.setAttribute("role", "rowheader");
            const link = document.createElement("a");
            link.className = "cell-title";
            link.href = source.root + hit.pagePath;
            link.textContent = hit.headline;
            const badge = document.createElement("span");
            badge.className = "badge badge-neutral";
            badge.textContent = source.kinds[hit.kind];
            cell.append(link, " ", badge);
            return cell;
        },
        officials: hit => hit.officials.map(official => official.name).join(", "),
        date: hit => hit.eventDate ?? hit.firstPublishedAtUtc.slice(0, 10),
        published: hit => {
            const time = document.createElement("time");
            const [year, month, day] = hit.firstPublishedAtUtc.slice(0, 10).split("-");
            time.dateTime = hit.firstPublishedAtUtc;
            time.title = `${hit.firstPublishedAtUtc.slice(0, 10)} ${hit.firstPublishedAtUtc.slice(11, 16)} UTC`;
            time.textContent = `${Number(day)} ${months[Number(month) - 1]} ${year}`;
            return time;
        }
    };
    const hitRow = (hit, source, headers) => {
        const row = document.createElement("tr");
        row.setAttribute("role", "row");
        source.columns.forEach((column, index) => {
            const content = hitCells[column](hit, source);
            const cell = content instanceof HTMLTableCellElement ? content : document.createElement("td");
            if (cell !== content) {
                cell.className = `${headers[index].className}${column === "published" ? " time" : ""}`.trim();
                cell.setAttribute("role", "cell");
                cell.append(content);
            }
            if (column !== "record") cell.dataset.label = headers[index].textContent.trim();
            row.append(cell);
        });
        return row;
    };
    const initSortable = table => {
        const source = table.dataset.sortSource ? JSON.parse(table.dataset.sortSource) : null;
        const body = table.tBodies[0];
        const headers = Array.from(table.tHead.rows[0].cells);
        const sortable = headers.filter(header => header.dataset.sort);
        const names = new Map(headers.map(header => [header, header.textContent.trim()]));
        const staticRows = Array.from(body.rows);
        const pager = source && table.closest("section").querySelector(".pager");
        if (!source && staticRows.length < 2) return;
        const label = (header, direction) => `${names.get(header)}, ${sortKinds[header.dataset.sort].words[direction]}`;
        const bar = document.createElement("div");
        bar.className = "sort-bar";
        const field = document.createElement("div");
        field.className = "field sort-field";
        const select = document.createElement("select");
        select.id = `sort-by-${++sortableTables}`;
        const selectLabel = document.createElement("label");
        selectLabel.className = "field-label";
        selectLabel.htmlFor = select.id;
        selectLabel.textContent = "Sort by";
        select.add(new Option(table.dataset.defaultOrder, ""));
        for (const header of sortable)
            for (const direction of [sortKinds[header.dataset.sort].first, opposite(sortKinds[header.dataset.sort].first)])
                select.add(new Option(label(header, direction), `${headers.indexOf(header)}:${direction}`));
        field.append(selectLabel, select);
        const status = document.createElement("p");
        status.className = "muted small";
        status.setAttribute("role", "status");
        bar.append(field, status);
        const more = document.createElement("button");
        more.type = "button";
        more.className = "btn btn-small";
        more.textContent = "More results";
        more.hidden = true;
        const wrap = table.closest(".table-wrap");
        wrap.before(bar);
        wrap.after(more);
        let current = null, nextCursor = null, shown = 0, running = 0;
        const commit = state => {
            current = state;
            for (const header of sortable) header.setAttribute("aria-sort", state?.header === header ? { asc: "ascending", desc: "descending" }[state.direction] : "none");
            select.value = state ? `${headers.indexOf(state.header)}:${state.direction}` : "";
        };
        const restore = () => {
            running++;
            body.replaceChildren(...staticRows);
            more.hidden = true;
            if (pager) pager.hidden = false;
            status.textContent = "";
        };
        const sortRows = () => {
            const kind = sortKinds[current.header.dataset.sort];
            const column = headers.indexOf(current.header);
            const value = row => row.cells[column].dataset.sortValue ?? row.cells[column].textContent.trim();
            const sign = current.direction === "asc" ? 1 : -1;
            const order = new Map(staticRows.map((row, index) => [row, index]));
            const compare = (a, b) => {
                const [left, right] = [value(a), value(b)];
                if (left === right) return 0;
                if (left === "" || right === "") return left === "" ? 1 : -1;
                return sign * kind.compare(left, right);
            };
            body.replaceChildren(...[...staticRows].sort((a, b) => compare(a, b) || order.get(a) - order.get(b)));
            status.textContent = `Sorted by ${label(current.header, current.direction)}`;
        };
        const fetchRows = async (state, append) => {
            const params = new URLSearchParams(source.filter ?? "");
            params.set("sort", state.header.dataset.sortKey);
            params.set("direction", state.direction);
            params.set("limit", source.pageSize);
            if (append) params.set("cursor", nextCursor);
            const mine = ++running;
            status.textContent = "Sorting.";
            let page, rows;
            try {
                const response = await fetch(`${source.api}/v1/search?${params}`);
                if (mine !== running) return;
                if (!response.ok) { commit(current); status.textContent = sortFailure(response.status); return; }
                page = await response.json();
                rows = page.hits.map(hit => hitRow(hit, source, headers));
            } catch {
                if (mine === running) { commit(current); status.textContent = sortFailure(0); }
                return;
            }
            if (mine !== running) return;
            if (append) body.append(...rows); else body.replaceChildren(...rows);
            shown = append ? shown + rows.length : rows.length;
            nextCursor = page.nextCursor;
            more.hidden = nextCursor === null;
            if (pager) pager.hidden = true;
            commit(state);
            const total = source.total, count = Math.min(shown, total);
            status.textContent = `Showing ${count.toLocaleString("en-US")} of ${total.toLocaleString("en-US")} ${total === 1 ? "record" : "records"}, sorted by ${label(current.header, current.direction)}.`;
        };
        const choose = state => {
            if (!state) { commit(null); restore(); return; }
            if (source) { fetchRows(state, false); return; }
            commit(state);
            sortRows();
        };
        for (const header of sortable) {
            const button = document.createElement("button");
            button.type = "button";
            button.className = "sort-button";
            button.append(...header.childNodes);
            header.append(button);
            button.addEventListener("click", () => {
                const first = sortKinds[header.dataset.sort].first;
                if (current?.header !== header) choose({ header, direction: first });
                else choose(current.direction === first ? { header, direction: opposite(first) } : null);
            });
        }
        select.addEventListener("change", () => {
            const [index, direction] = select.value.split(":");
            choose(select.value === "" ? null : { header: headers[Number(index)], direction });
        });
        more.addEventListener("click", () => fetchRows(current, true));
        commit(null);
    };

    document.querySelectorAll("[data-records]").forEach(initRecordFilters);
    document.querySelectorAll("[data-officials-filters]").forEach(initOfficialsFilter);
    document.querySelectorAll("[data-search-api]").forEach(initSearchService);
    document.querySelectorAll("[data-report-api]").forEach(initReportForm);
    new Set(Array.from(document.querySelectorAll("th[data-sort]"), header => header.closest("table"))).forEach(initSortable);
})();
