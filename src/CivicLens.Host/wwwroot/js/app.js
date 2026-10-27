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
    let measurePending = false;
    const measureAll = () => {
        if (measurePending) return;
        measurePending = true;
        requestAnimationFrame(() => {
            measurePending = false;
            all(".segmented").forEach(measureSegmented);
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

    const setLinked = (key, linked) => all(`[data-link="${CSS.escape(key)}"]`).forEach(element => {
        if (element.offsetParent !== null) element.classList.toggle("is-linked", linked);
    });

    const init = () => {
        all(".theme-toggle").forEach(toggle => { toggle.hidden = false; });
        all("[data-layout-switch]").forEach(control => { control.hidden = false; });
        showTheme(root.dataset.theme || "");
        measureAll();
    };

    document.addEventListener("click", event => {
        const target = event.target instanceof Element ? event.target : null;
        const theme = target?.closest("[data-theme-choice]");
        if (theme) { setTheme(theme.dataset.themeChoice); return; }
        const layout = target?.closest("[data-layout-choice]");
        if (layout) {
            const view = layout.closest(".comparison");
            view.dataset.layout = layout.dataset.layoutChoice;
            all("[data-layout-choice]", view).forEach(button =>
                button.setAttribute("aria-pressed", String(button === layout)));
            measureAll();
        }
    });
    for (const [type, linked] of [["mouseover", true], ["focusin", true], ["mouseout", false], ["focusout", false]]) {
        document.addEventListener(type, event => {
            const element = event.target instanceof Element ? event.target.closest("[data-link]") : null;
            if (element) setLinked(element.dataset.link, linked);
        });
    }
    window.addEventListener("resize", measureAll);
    document.fonts?.ready.then(measureAll);
    new MutationObserver(measureAll)
        .observe(document.body, { subtree: true, childList: true, attributes: true, attributeFilter: ["aria-pressed", "aria-current"] });

    if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", init);
    else init();
})();
