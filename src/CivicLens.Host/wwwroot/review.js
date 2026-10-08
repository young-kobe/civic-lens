(() => {
    const form = document.querySelector("[data-unsaved-form]");
    if (!form) return;

    let dirty = form.dataset.recoveredUnsaved === "true";
    let submitting = false;

    const decisionControls = document.querySelector("#decision-controls");
    const warning = document.querySelector("#decision-unsaved-warning");
    const markDirty = () => {
        dirty = true;
        if (decisionControls) decisionControls.disabled = true;
        if (warning) warning.hidden = false;
    };
    if (dirty) markDirty();
    form.addEventListener("input", markDirty);
    form.addEventListener("change", markDirty);
    document.querySelector("#decision-form")?.addEventListener("submit", event => {
        if (dirty || decisionControls?.disabled) event.preventDefault();
    });
    form.addEventListener("submit", () => { submitting = true; });
    window.addEventListener("beforeunload", event => {
        if (!dirty || submitting) return;
        event.preventDefault();
        event.returnValue = "";
    });
})();
