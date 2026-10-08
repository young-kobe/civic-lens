(() => {
    const activity = document.getElementById("source-activity");
    if (!activity) return;
    const notice = document.getElementById("activity-refresh-status");
    const sources = Array.from(document.querySelectorAll("[data-source-id]"));
    const token = document.querySelector("input[name='__RequestVerificationToken']")?.value;
    let latestJobs = new Map();
    const element = (tag, className, text) => {
        const node = document.createElement(tag);
        if (className) node.className = className;
        if (text !== undefined) node.textContent = text;
        return node;
    };
    const setText = (node, value) => {
        if (node && node.textContent !== value) node.textContent = value;
    };
    const addField = (form, name, value) => {
        const field = element("input");
        field.type = "hidden";
        field.name = name;
        field.value = value;
        form.append(field);
    };
    const updateJob = job => {
        let row = Array.from(activity.children).find(node => node.dataset.jobId === job.jobId);
        if (!row) {
            row = element("article", "review-list-item sources-activity-item");
            row.dataset.jobId = job.jobId;
            const copy = element("div");
            const meta = element("div", "review-meta");
            meta.append(element("span", "review-state"), element("time"));
            copy.append(meta, element("h3"), element("p"));
            row.append(copy, element("div", "sources-activity-actions"));
            activity.append(row);
        }
        const progress = job.progress;
        const state = row.querySelector(".review-state");
        state.classList.toggle("concern", progress?.needsAttention === true);
        setText(state, progress?.label ?? job.label);
        setText(row.querySelector("time"), job.createdAt);
        setText(row.querySelector("h3"), job.title);
        setText(row.querySelector("p"), job.sourceName + (progress?.detail ? " · " + progress.detail : ""));
        const actions = row.querySelector(".sources-activity-actions");
        const action = progress?.comparisonId ? "review:" + progress.comparisonId : job.canStop ? "stop" :
            (progress?.isActive ?? job.isActive) ? "waiting" : "details";
        if (actions.dataset.action !== action && !actions.contains(document.activeElement)) {
            actions.replaceChildren();
            if (progress?.comparisonId && token) {
                const form = element("form");
                form.method = "post";
                form.action = "/Review?handler=Create";
                addField(form, "__RequestVerificationToken", token);
                addField(form, "comparisonId", progress.comparisonId);
                addField(form, "idempotencyKey", crypto.randomUUID().replaceAll("-", ""));
                const button = element("button", "review-button review-button-primary", "Review");
                button.type = "submit";
                form.append(button);
                actions.append(form);
            } else if (action === "stop" && token) {
                const form = element("form");
                form.method = "post";
                form.action = "/Review/Sources?handler=Cancel";
                addField(form, "__RequestVerificationToken", token);
                addField(form, "jobId", job.jobId);
                const button = element("button", "review-button", "Stop");
                button.type = "submit";
                form.append(button);
                actions.append(form);
            } else if (action === "waiting") {
                actions.append(element("span", "review-help", "In progress"));
            } else {
                const link = element("a", "review-button", "Details");
                link.href = "/Review/Sources?jobId=" + encodeURIComponent(job.jobId) + "#troubleshooting";
                actions.append(link);
            }
            actions.dataset.action = action;
        }
        return row;
    };
    let refreshing = false;
    const refresh = async () => {
        if (document.hidden || refreshing) return;
        refreshing = true;
        try {
            const response = await fetch(activity.dataset.activityUrl, {
                credentials: "same-origin", headers: { Accept: "application/json" },
                signal: AbortSignal.timeout(5000)
            });
            if (!response.ok) throw new Error("Activity unavailable");
            const data = await response.json();
            latestJobs = new Map(data.jobs.map(job => [job.jobId, job]));
            const currentIds = new Set(data.jobs.map(job => job.jobId));
            const focusedRow = document.activeElement?.closest("[data-job-id]");
            Array.from(activity.children).forEach(row => {
                if (!currentIds.has(row.dataset.jobId) && row !== focusedRow) row.remove();
            });
            // Reorder only when needed, preserving focused controls and their idempotency keys.
            data.jobs.forEach((job, index) => {
                const row = updateJob(job);
                if (activity.children[index] !== row && row !== focusedRow)
                    activity.insertBefore(row, activity.children[index] ?? null);
            });
            if (data.jobs.length) document.querySelector("[data-activity-empty]")?.remove();
            data.sources.forEach(source => {
                const card = sources.find(node => node.dataset.sourceId === source.sourceId);
                if (!card) return;
                card.querySelector("button[type='submit']").disabled = !source.enabled || source.busy;
                const result = card.querySelector(".sources-result");
                const text = (source.progress?.label ?? source.label) +
                    (source.progress?.detail ? " · " + source.progress.detail : "");
                setText(result, text);
            });
            setText(notice, "Activity updates automatically.");
        } catch {
            setText(notice, "Live updates paused. Refresh to reconnect.");
        } finally {
            refreshing = false;
        }
    };
    let timer;
    activity.addEventListener("focusout", event => {
        const row = event.target.closest("[data-job-id]");
        queueMicrotask(() => {
            const job = latestJobs.get(row?.dataset.jobId);
            if (job) updateJob(job);
        });
    });
    const poll = async () => {
        await refresh();
        window.clearTimeout(timer);
        if (!document.hidden) timer = window.setTimeout(poll, 3000);
    };
    document.addEventListener("visibilitychange", () => {
        window.clearTimeout(timer);
        if (!document.hidden) poll();
    });
    window.addEventListener("pagehide", () => window.clearTimeout(timer), { once: true });
    timer = window.setTimeout(poll, 3000);
})();
