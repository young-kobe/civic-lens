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

    const init = () => {
        all(".theme-toggle").forEach(toggle => { toggle.hidden = false; });
        all("[data-layout-switch]").forEach(control => { control.hidden = false; });
        showTheme(root.dataset.theme || "");
        initTabs();
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
