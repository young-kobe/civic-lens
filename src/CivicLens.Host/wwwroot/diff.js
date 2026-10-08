document.querySelectorAll("[data-diff-view]").forEach((view) => {
    const button = view.querySelector("[data-diff-toggle]");
    const label = view.querySelector("[data-diff-toggle-label]");
    if (!button || !label) return;

    button.addEventListener("click", () => {
        const showSideBySide = button.getAttribute("aria-pressed") !== "true";
        button.setAttribute("aria-pressed", String(showSideBySide));
        view.dataset.diffLayout = showSideBySide ? "side" : "stacked";
        label.textContent = showSideBySide ? "Show passages in reading order" : "Show passages side by side";
    });
});
