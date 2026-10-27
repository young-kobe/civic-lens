(() => {
    const all = (selector, scope = document) => Array.from(scope.querySelectorAll(selector));

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
    const measureAllTabs = () => {
        if (measurePending) return;
        measurePending = true;
        requestAnimationFrame(() => {
            measurePending = false;
            all(".tabs[data-ready]").forEach(measureTabs);
        });
    };

    const selectTab = (tabset, name) => {
        all("[data-tab]", tabset).forEach(tab => tab.setAttribute("aria-selected", String(tab.dataset.tab === name)));
        all("[data-pane]", tabset).forEach(pane => { pane.hidden = pane.dataset.pane !== name; });
        measureAllTabs();
    };
    const initTabs = () => all("[data-tabset]").forEach(tabset => {
        const tabs = tabset.querySelector(".tabs");
        if (!tabs || tabs.hasAttribute("data-ready")) return;
        tabs.setAttribute("data-ready", "");
        selectTab(tabset, tabset.dataset.selected || tabset.querySelector("[data-tab]")?.dataset.tab);
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
        initTabs();
        const unsaved = document.querySelector("[data-unsaved-form]");
        if (unsaved?.dataset.recoveredUnsaved === "true") markDirty();
        measureAllTabs();
    };

    document.addEventListener("click", event => {
        const target = event.target instanceof Element ? event.target : null;
        const tab = target?.closest("[data-tab]");
        if (tab) { selectTab(tab.closest("[data-tabset]"), tab.dataset.tab); return; }
        const dismiss = target?.closest("[data-dismiss]");
        if (dismiss) dismiss.closest(".notice")?.remove();
    });
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
    window.addEventListener("resize", measureAllTabs);
    document.fonts?.ready.then(measureAllTabs);
    new MutationObserver(() => { initTabs(); measureAllTabs(); })
        .observe(document.body, { subtree: true, childList: true });

    if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", init);
    else init();

    window.Blazor.start({ ssr: { disableDomPreservation: true } });
})();
