(() => {
    const root = document.documentElement;
    const all = (selector, scope = document) => Array.from(scope.querySelectorAll(selector));
    const themeKey = "civic-lens-theme";

    const measureSegmented = group => {
        const selected = group.querySelector("[aria-pressed='true'], [aria-current='true']");
        let thumb = group.querySelector(".segmented-thumb");
        if (!selected || selected.offsetParent === null) return;
        if (!thumb) {
            thumb = document.createElement("span");
            thumb.className = "segmented-thumb";
            group.prepend(thumb);
        }
        thumb.style.width = `${selected.offsetWidth}px`;
        thumb.style.transform = `translateX(${selected.offsetLeft - 2}px)`;
        group.classList.add("is-measured");
    };
    const measureTabs = tabs => {
        const selected = tabs.querySelector("[aria-selected='true']");
        let line = tabs.querySelector(".tabs-line");
        if (!selected || selected.offsetParent === null) return;
        if (!line) {
            line = document.createElement("span");
            line.className = "tabs-line";
            tabs.append(line);
        }
        line.style.width = `${selected.offsetWidth}px`;
        line.style.transform = `translateX(${selected.offsetLeft}px)`;
    };
    let measurePending = false;
    const measureAll = () => {
        if (measurePending) return;
        measurePending = true;
        requestAnimationFrame(() => {
            measurePending = false;
            all(".segmented").forEach(measureSegmented);
            all(".tabs[data-ready]").forEach(measureTabs);
        });
    };

    const showTheme = choice => all("[data-theme-choice]").forEach(button =>
        button.setAttribute("aria-pressed", String(button.dataset.themeChoice === choice)));
    const setTheme = choice => {
        if (choice) root.dataset.theme = choice;
        else delete root.dataset.theme;
        try {
            if (choice) localStorage.setItem(themeKey, choice);
            else localStorage.removeItem(themeKey);
        } catch {}
        showTheme(choice);
        measureAll();
    };

    const selectTab = (tabset, name) => {
        all("[data-tab]", tabset).forEach(tab => tab.setAttribute("aria-selected", String(tab.dataset.tab === name)));
        all("[data-pane]", tabset).forEach(pane => { pane.hidden = pane.dataset.pane !== name; });
        measureAll();
    };
    const initTabs = () => all("[data-tabset]").forEach(tabset => {
        const tabs = tabset.querySelector(".tabs");
        if (!tabs || tabs.hasAttribute("data-ready")) return;
        tabs.setAttribute("data-ready", "");
        selectTab(tabset, tabset.dataset.selected || tabset.querySelector("[data-tab]")?.dataset.tab);
    });

    const setLinked = (key, linked) => all(`[data-link="${CSS.escape(key)}"]`).forEach(element => {
        if (element.offsetParent !== null) element.classList.toggle("is-linked", linked);
    });

    let dirty = false;
    let submitting = false;
    const markDirty = () => {
        dirty = true;
        const controls = document.getElementById("decision-controls");
        const warning = document.getElementById("decision-unsaved-warning");
        if (controls) controls.disabled = true;
        if (warning) warning.hidden = false;
    };

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
        more.addEventListener("click", () => run(true));
    };

    const init = () => {
        all(".theme-toggle").forEach(toggle => { toggle.hidden = false; });
        all("[data-layout-switch]").forEach(control => { control.hidden = false; });
        showTheme(root.dataset.theme || "");
        initTabs();
        all("[data-search-api]").forEach(initSearchService);
        const unsaved = document.querySelector("[data-unsaved-form]");
        if (unsaved?.dataset.recoveredUnsaved === "true") markDirty();
        measureAll();
    };

    document.addEventListener("click", event => {
        const target = event.target instanceof Element ? event.target : null;
        const theme = target?.closest("[data-theme-choice]");
        if (theme) { setTheme(theme.dataset.themeChoice); return; }
        const tab = target?.closest("[data-tab]");
        if (tab) { selectTab(tab.closest("[data-tabset]"), tab.dataset.tab); return; }
        const layout = target?.closest("[data-layout-choice]");
        if (layout) {
            const view = layout.closest(".comparison");
            view.dataset.layout = layout.dataset.layoutChoice;
            all("[data-layout-choice]", view).forEach(button =>
                button.setAttribute("aria-pressed", String(button === layout)));
            measureAll();
            return;
        }
        const dismiss = target?.closest("[data-dismiss]");
        if (dismiss) dismiss.closest(".notice")?.remove();
    });
    for (const [type, linked] of [["mouseover", true], ["focusin", true], ["mouseout", false], ["focusout", false]]) {
        document.addEventListener(type, event => {
            const element = event.target instanceof Element ? event.target.closest("[data-link]") : null;
            if (element) setLinked(element.dataset.link, linked);
        });
    }
    const focusable = "a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), summary, [tabindex]:not([tabindex='-1'])";
    const openDialog = () => document.querySelector("[role='dialog'][aria-modal='true']");
    document.addEventListener("keydown", event => {
        const dialog = openDialog();
        if (event.key !== "Tab" || !dialog) return;
        const stops = all(focusable, dialog).filter(element => element.offsetParent !== null);
        const active = document.activeElement;
        if (stops.length === 0) {
            event.preventDefault();
            dialog.focus();
        } else if (event.shiftKey && (active === stops[0] || active === dialog || !dialog.contains(active))) {
            event.preventDefault();
            stops.at(-1).focus();
        } else if (!event.shiftKey && (active === stops.at(-1) || !dialog.contains(active))) {
            event.preventDefault();
            stops[0].focus();
        }
    });
    document.addEventListener("focusin", event => {
        const dialog = openDialog();
        if (dialog && event.target instanceof Node && !dialog.contains(event.target)) dialog.focus();
    });
    document.addEventListener("scroll", event => {
        const wrap = event.target;
        if (wrap instanceof Element && wrap.classList.contains("table-wrap-scroll"))
            wrap.classList.toggle("is-scrolled", wrap.scrollTop > 0);
    }, true);
    document.addEventListener("input", event => {
        if (event.target instanceof Element && event.target.closest("[data-unsaved-form]")) markDirty();
    });
    document.addEventListener("submit", event => {
        const form = event.target;
        if (form.matches("[data-unsaved-form]")) submitting = true;
        if (form.id === "decision-form" && (dirty || document.getElementById("decision-controls")?.disabled))
            event.preventDefault();
    });
    window.addEventListener("beforeunload", event => {
        if (!dirty || submitting) return;
        event.preventDefault();
        event.returnValue = "";
    });
    window.addEventListener("resize", measureAll);
    document.fonts?.ready.then(measureAll);
    new MutationObserver(() => { initTabs(); measureAll(); })
        .observe(document.body, { subtree: true, childList: true, attributes: true, attributeFilter: ["aria-pressed", "aria-current"] });

    if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", init);
    else init();

    if (window.Blazor?.start) window.Blazor.start({ ssr: { disableDomPreservation: true } });
})();
