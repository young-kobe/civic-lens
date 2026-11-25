(() => {
    const siteMenu = document.querySelector(".public-menu");
    if (siteMenu) {
        const summary = siteMenu.querySelector("summary");
        document.addEventListener("keydown", event => {
            if (event.key !== "Escape" || !siteMenu.open) return;
            const restoreFocus = siteMenu.contains(document.activeElement);
            siteMenu.open = false;
            if (restoreFocus) summary.focus();
        });
        document.addEventListener("pointerdown", event => {
            if (siteMenu.open && !siteMenu.contains(event.target)) siteMenu.open = false;
        });
    }
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
        const categories = form.querySelector("#report-category [data-text-required]");
        const chosenCategory = () => categories.querySelector("input:checked")?.value ?? "";
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
        const title = query.get(form.dataset.reportTitleParameter) ?? "";
        const kind = query.get(form.dataset.reportKindParameter);
        const kindOptions = Array.from(document.querySelectorAll("[data-report-kind-option]"));
        const kindOption = kind === null ? null : kindOptions.find(candidate => candidate.dataset.reportKindOption === kind);
        if (kindOption) {
            const kindLabel = document.querySelector("[data-report-subject-kind]");
            kindLabel.textContent = kindOption.dataset.reportKindLabel;
            kindLabel.hidden = false;
        }
        const recordPage = form.dataset.recordPage.replace("{id}", recordId);
        if (title !== "" && title.length <= Number(form.dataset.reportTitleLimit)) {
            const link = document.querySelector("[data-report-subject-link]");
            link.href = recordPage;
            link.textContent = title;
            document.querySelector("[data-report-subject-title]").hidden = false;
        }
        document.querySelector("[data-report-subject-revision]").textContent = `Revision ${revision}.`;
        const back = document.querySelector("[data-report-record]");
        back.href = recordPage;
        back.textContent = "Back to the record";
        queryInputs.forEach(input => { input.value = query.get(input.name); });
        const checkText = () => {
            const required = chosenCategory() === categories.dataset.textRequired;
            text.required = required;
            text.setCustomValidity(required && text.value.trim() === "" ? "Add details to explain what is wrong." : "");
        };
        const widget = form.querySelector(".cf-turnstile");
        const widgetScript = document.querySelector("[data-public-external-script]");
        let widgetId = null;
        const renderWidget = () => {
            widgetId = window.turnstile.render(widget, { sitekey: widget.dataset.sitekey, action: widget.dataset.action });
        };
        categories.addEventListener("change", checkText);
        text.addEventListener("input", checkText);
        form.addEventListener("submit", async event => {
            event.preventDefault();
            const data = new URLSearchParams(new FormData(form));
            const tokens = data.getAll("cf-turnstile-response");
            if (tokens.length !== 1 || !tokens[0]) {
                show("bad", "Complete the check that you are not a robot, then send the report again.");
                return;
            }
            submit.disabled = true;
            let outcome = ["bad", "The report did not reach the service. Check your connection and send the report again."];
            try {
                const response = await fetch(form.dataset.reportApi, { method: "POST", body: data });
                outcome = reportMessages[response.status] ?? ["bad", `The service answered with the unexpected status ${response.status}. Try again later.`];
            } catch {}
            show(...outcome);
            if (outcome[0] === "ok") panel.hidden = true;
            else {
                if (widgetId !== null) window.turnstile.reset(widgetId);
                submit.disabled = false;
            }
        });
        panel.hidden = false;
        if (widgetScript && window.turnstile) renderWidget();
        else widgetScript?.addEventListener("load", renderWidget, { once: true });
    };

    const memberZipKey = "civic-lens-members-zip";
    const memberReleaseKey = "civic-lens-members-release";
    const validZip = value => typeof value === "string" && /^[0-9]{5}$/.test(value);
    const positiveRelease = value => /^[1-9][0-9]*$/.test(String(value ?? "")) && Number(value) <= 2147483647;
    const initRememberedMembers = () => {
        const chip = document.querySelector("[data-your-members]");
        if (!chip) return { zip: () => null, snapshot: () => ({ zip: null, release: null }), current: () => true, complete: () => true, refresh: () => {}, onForget: () => {}, onChange: () => {} };
        let zip = null;
        const forgetters = new Set();
        const changers = new Set();
        let marker = null;
        const notify = callbacks => callbacks.forEach(callback => callback(zip));
        const readMarker = forZip => {
            let stored;
            try { stored = localStorage.getItem(memberReleaseKey); } catch { return marker?.zip === forZip ? marker.release : null; }
            try {
                const parsed = JSON.parse(stored);
                if (validZip(parsed?.zip) && Number.isInteger(parsed.release) && positiveRelease(parsed.release)) {
                    marker = { zip: parsed.zip, release: Number(parsed.release) };
                    return marker.zip === forZip ? marker.release : null;
                }
            } catch {}
            marker = null;
            return null;
        };
        const clearMarker = () => {
            marker = null;
            try { localStorage.removeItem(memberReleaseKey); } catch {}
        };
        const render = value => { zip = validZip(value) ? value : null; chip.hidden = zip === null; };
        const readStoredZip = () => {
            let stored;
            try { stored = localStorage.getItem(memberZipKey); } catch { return zip; }
            if (validZip(stored)) { readMarker(stored); return stored; }
            marker = null;
            return null;
        };
        const snapshot = () => {
            const storedZip = readStoredZip();
            return { zip: storedZip, release: marker?.zip === storedZip ? marker.release : null };
        };
        const current = expected => {
            const latest = snapshot();
            return latest.zip === expected.zip && latest.release === expected.release;
        };
        const refresh = () => { render(readStoredZip()); notify(changers); };
        render(readStoredZip());
        document.addEventListener("click", event => {
            if (!(event.target instanceof Element) || !event.target.closest("button[data-forget-members]")) return;
            try { localStorage.removeItem(memberZipKey); } catch {}
            clearMarker();
            render(null);
            notify(forgetters);
            const input = document.querySelector("[data-zip-api] input[name=zip]");
            if (input) input.focus();
            else document.querySelector(".public-brand")?.focus();
        });
        window.addEventListener("storage", event => {
            if (event.key !== memberZipKey && event.key !== null) return;
            render(readStoredZip());
            notify(changers);
        });
        window.addEventListener("pageshow", event => {
            if (!event.persisted) return;
            render(readStoredZip());
            notify(changers);
        });
        return {
            zip: () => zip,
            snapshot,
            current,
            refresh,
            complete: (value, release, expected, persist) => {
                if (!current(expected) || !validZip(value) || !positiveRelease(release) || (!persist && value !== expected.zip)) return false;
                render(value);
                marker = { zip: value, release: Number(release) };
                try {
                    if (persist) localStorage.setItem(memberZipKey, value);
                    localStorage.setItem(memberReleaseKey, JSON.stringify(marker));
                } catch {}
                return true;
            },
            onForget: callback => { forgetters.add(callback); },
            onChange: callback => { changers.add(callback); }
        };
    };

    const memberFragment = (html, zip, issue, expectedView, sinceRelease = null, official = null) => {
        const template = document.createElement("template");
        template.innerHTML = html;
        const fragment = template.content.firstElementChild;
        const count = fragment?.dataset.newVotes;
        const hasSince = fragment?.dataset.sinceRelease !== "";
        const hasCount = count !== "";
        const stripTemplates = fragment?.querySelectorAll("template[data-member-strip-template]") ?? [];
        const stripTemplateContent = stripTemplates[0]?.content;
        if (template.content.childElementCount !== 1 || !fragment.matches("div[data-zip-member-fragment]")
            || fragment.dataset.zcta !== zip || fragment.dataset.issue !== (issue ?? "") || fragment.dataset.view !== expectedView
            || (fragment.dataset.official ?? "") !== (official ?? "")
            || !positiveRelease(fragment.dataset.release) || !positiveRelease(fragment.dataset.congress)
            || (fragment.dataset.sinceRelease !== "" && !positiveRelease(fragment.dataset.sinceRelease))
            || fragment.dataset.sinceRelease !== (sinceRelease === null ? "" : String(sinceRelease))
            || (hasSince !== hasCount)
            || (hasCount && (typeof count !== "string" || count.length > 19 || !/^(0|[1-9][0-9]*)$/.test(count) || BigInt(count) > 9223372036854775807n))
            || expectedView !== "strip" && expectedView !== "issue" && expectedView !== "official" && (stripTemplates.length !== 1 || stripTemplateContent.childElementCount !== 1
                || !stripTemplateContent.firstElementChild.matches("div[data-member-strip-content]"))
            || (expectedView === "strip"
                ? fragment.childElementCount !== 1 || !fragment.firstElementChild.matches("div[data-member-strip-content]")
                : expectedView === "issue"
                    ? !fragment.querySelector(".shared-vote-rows, [data-member-votes-empty]")
                    : expectedView === "official" ? false : !fragment.querySelector(".zip-member-cards > li")))
            throw new TypeError("Invalid ZIP lookup answer.");
        return fragment;
    };

    const readMemberFragment = async (api, zip, issue, view, sinceRelease, signal, official = null) => {
        const params = new URLSearchParams();
        if (view !== "") params.set("view", view);
        if (issue !== null) params.set("issue", issue);
        if (official !== null) params.set("official", official);
        if (sinceRelease !== null) params.set("sinceRelease", String(sinceRelease));
        const query = params.size === 0 ? "" : `?${params}`;
        const response = await fetch(`${api}/v1/zip-districts/${zip}/members${query}`, { headers: { Accept: "text/html" }, signal });
        if (!response.ok) return { status: response.status, fragment: null };
        if (!/^text\/html(?:;|$)/i.test(response.headers.get("Content-Type") ?? ""))
            throw new TypeError("Invalid ZIP lookup content type.");
        const html = await response.text();
        signal.throwIfAborted();
        return { status: response.status, fragment: memberFragment(html, zip, issue, view, sinceRelease, official) };
    };

    const memberFailure = (zip, status, officialsPage) => {
        const message = document.createElement("p");
        const texts = {
            404: `No match for ${zip}. Check the 5 digits. Some ZIP codes, such as a PO box, are not in the Census data.`,
            429: "Too many lookups. Wait one minute and try again."
        };
        if (status in texts) { message.textContent = texts[status]; return message; }
        message.textContent = "The lookup failed. Try again, or ";
        const link = document.createElement("a");
        link.textContent = "browse all officials";
        link.href = officialsPage;
        message.append(link, ".");
        return message;
    };

    const memberFailureWithRetry = (zip, status, officialsPage, retry) => {
        const again = document.createElement("button");
        again.type = "button";
        again.className = "btn";
        again.textContent = "Try again";
        again.addEventListener("click", retry);
        return [memberFailure(zip, status, officialsPage), again];
    };

    // The one owner of loading a member fragment. It keeps the stale-ZIP guard (snapshot, current, refresh) for every caller.
    // A caller supplies the render hooks: onFragment(fragment, call) returns false when the page could not keep the answer,
    // onFailure(call, status) shows the failure, onLoading(call) and onSettled(call) are optional.
    // run() resolves to true when the answer was shown, false when it was not, and null when a newer run replaced it.
    const memberLoader = (remembered, { api, view, requireSnapshot = false, onLoading, onFragment, onFailure, onSettled }) => {
        let request = 0;
        let active = null;
        const cancel = () => {
            request++;
            active?.abort();
            active = null;
        };
        const run = async (zip, options = {}) => {
            const mine = ++request;
            active?.abort();
            active = null;
            const startingState = remembered.snapshot();
            if (requireSnapshot && startingState.zip !== zip) { remembered.refresh(); return false; }
            const marker = "since" in options ? options.since : startingState.zip === zip ? startingState.release : null;
            const abort = new AbortController();
            active = abort;
            const call = { ...options, zip, startingState, marker };
            onLoading?.(call);
            try {
                const { status, fragment } = await readMemberFragment(api, zip, options.issue ?? null, view, marker, abort.signal, options.official ?? null);
                if (mine !== request) return null;
                if (fragment === null) { onFailure(call, status); return false; }
                if (!remembered.current(startingState) || onFragment(fragment, call) === false) { remembered.refresh(); return false; }
                return true;
            } catch {
                if (mine !== request) return null;
                onFailure(call, 0);
                return false;
            } finally {
                if (mine === request) { active = null; onSettled?.(call); }
            }
        };
        return { run, cancel };
    };

    // Loads one member fragment into a slot and follows the remembered ZIP. A view supplies frame(nodes), nodeOf(fragment), and optional onShow and onClear hooks.
    // hideOnFailure clears the slot on a failed lookup instead of showing the retry message. The members strip already reports the lookup state.
    const followMemberSlot = (remembered, slot, { api, view, issue = null, official = null, sinceMarker = true, hideOnFailure = false, frame, nodeOf, accepts, onShow, onClear }) => {
        const loader = memberLoader(remembered, {
            api,
            view,
            requireSnapshot: true,
            onLoading: () => {
                onShow?.();
                slot.setAttribute("aria-busy", "true");
                if (slot.childElementCount !== 0) return;
                const loading = document.createElement("p");
                loading.textContent = "Looking up your officials.";
                slot.replaceChildren(frame([loading]));
            },
            onFragment: fragment => {
                if (accepts && !accepts(fragment)) { slot.replaceChildren(); onClear?.(); return true; }
                slot.replaceChildren(nodeOf(fragment));
            },
            onFailure: (call, status) => {
                if (hideOnFailure) { slot.replaceChildren(); onClear?.(); return; }
                slot.replaceChildren(frame(memberFailureWithRetry(call.zip, status, slot.dataset.officialsPage, () => run(call.zip))));
            },
            onSettled: () => slot.removeAttribute("aria-busy")
        });
        const clear = () => {
            loader.cancel();
            slot.removeAttribute("aria-busy");
            slot.replaceChildren();
            onClear?.();
        };
        const run = zip => {
            if (zip !== remembered.zip()) return;
            if (!validZip(zip)) { clear(); return; }
            loader.run(zip, sinceMarker ? { issue, official } : { issue, official, since: null });
        };
        remembered.onForget(clear);
        remembered.onChange(zip => { if (zip === null) clear(); else { slot.replaceChildren(); run(zip); } });
        const initial = remembered.zip();
        if (initial !== null) run(initial);
    };

    const initZipLookup = (form, remembered) => {
        const api = form.dataset.zipApi;
        const panel = document.querySelector("[data-zip-results]");
        const body = panel.querySelector("[data-zip-body]");
        const notes = panel.querySelector("[data-zip-notes]");
        const input = form.elements.namedItem("zip");
        const submit = form.querySelector("[type=submit]");
        const problem = form.querySelector("[data-zip-error]");
        const heading = panel.querySelector("h2");
        // The featured vote asks for a ZIP code only until a lookup answers, because the answer then shows how the members voted.
        const featuredPrompt = document.querySelector("[data-featured-zip-prompt]");
        const answerFragment = "#" + heading.id;
        heading.tabIndex = -1;
        const element = (tag, text, className) => {
            const node = document.createElement(tag);
            if (text !== undefined) node.textContent = text;
            if (className) node.className = className;
            return node;
        };
        const show = (nodes, listed = false) => { panel.hidden = false; notes.hidden = !listed; body.replaceChildren(...nodes); };
        const showProblem = (...nodes) => { panel.hidden = true; problem.replaceChildren(...nodes); };
        const reveal = () => {
            panel.scrollIntoView({ block: "start", behavior: reducedMotion.matches ? "auto" : "smooth" });
            heading.focus({ preventScroll: true });
        };
        const showAnswer = fragment => {
            show([fragment], true);
            fragment.querySelectorAll("[data-member-issue-filter]").forEach(controls => { controls.hidden = false; });
        };
        let strip = document.querySelector("#members-strip");
        const stripTemplate = fragment => fragment.querySelector("template[data-member-strip-template]");
        const mountStrip = fragment => {
            const source = stripTemplate(fragment);
            if (!source || source.content.childElementCount !== 1 || !source.content.firstElementChild.matches("div[data-member-strip-content]")) return;
            if (!strip) {
                strip = document.createElement("div");
                strip.id = "members-strip";
                strip.className = "members-strip";
                strip.setAttribute("aria-live", "polite");
                document.querySelector(".public-header")?.after(strip);
            }
            strip.replaceChildren(source.content.firstElementChild.cloneNode(true));
        };
        const submitLabel = submit.textContent;
        const loadingText = "Looking up your officials.";
        const setBusy = busy => {
            submit.disabled = busy;
            submit.textContent = busy ? loadingText : submitLabel;
            if (busy) body.setAttribute("aria-busy", "true");
            else body.removeAttribute("aria-busy");
            body.querySelectorAll("[data-member-issue]").forEach(button => { button.disabled = busy; });
        };
        let renderedSinceRelease = null;
        const loader = memberLoader(remembered, {
            api,
            view: "",
            onFragment: (fragment, call) => {
                showAnswer(fragment);
                mountStrip(fragment);
                if (!call.filtered) {
                    renderedSinceRelease = call.marker;
                    if (!remembered.complete(call.zip, Number(fragment.dataset.release), call.startingState, call.persist)) return false;
                    if (featuredPrompt) featuredPrompt.hidden = true;
                }
                if (call.filtered) fragment.querySelector('[data-member-issue][aria-pressed="true"]')?.focus();
                if (call.revealAnswer) reveal();
            },
            onFailure: (call, status) => {
                const nodes = memberFailureWithRetry(call.zip, status, form.dataset.officialsPage,
                    () => runLookup(call.zip, call.revealAnswer, call.persist, call.issue, call.filtered));
                if (call.filtered) problem.replaceChildren(...nodes);
                else showProblem(...nodes);
            },
            onSettled: () => setBusy(false)
        });
        const runLookup = async (zip, revealAnswer, persist = true, issue = null, filtered = false) => {
            loader.cancel();
            problem.replaceChildren();
            if (!validZip(zip)) {
                showProblem(element("p", "Enter a ZIP code with 5 digits."));
                setBusy(false);
                input.focus();
                return;
            }
            setBusy(true);
            if (!filtered) show([element("p", loadingText)]);
            const options = { issue, filtered, persist, revealAnswer };
            if (filtered) options.since = renderedSinceRelease;
            const found = await loader.run(zip, options);
            if (found === false && revealAnswer) input.focus();
        };
        body.addEventListener("click", event => {
            const button = event.target.closest("button[data-member-issue]");
            if (!button || button.disabled) return;
            const fragment = button.closest("[data-zip-member-fragment]");
            if (fragment) runLookup(fragment.dataset.zcta, false, false, button.dataset.memberIssue || null, true);
        });
        form.addEventListener("submit", event => {
            event.preventDefault();
            runLookup(input.value.trim(), true);
        });
        document.querySelector("[data-your-members] a")?.addEventListener("click", event => {
            if (event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
            const saved = remembered.zip();
            if (saved === null) return;
            event.preventDefault();
            if (location.hash !== answerFragment) history.pushState(null, "", answerFragment);
            input.value = saved;
            runLookup(saved, true, false);
        });
        input.addEventListener("input", () => {
            const wasBusy = body.hasAttribute("aria-busy");
            loader.cancel();
            setBusy(false);
            if (wasBusy) {
                panel.hidden = true;
                notes.hidden = true;
                body.replaceChildren();
            }
        });
        const clearLookup = () => {
            loader.cancel();
            renderedSinceRelease = null;
            setBusy(false);
            input.value = "";
            problem.replaceChildren();
            panel.hidden = true;
            notes.hidden = true;
            body.replaceChildren();
            strip?.replaceChildren();
            if (featuredPrompt) featuredPrompt.hidden = false;
        };
        remembered.onForget(clearLookup);
        remembered.onChange(value => {
            renderedSinceRelease = null;
            if (value === null) { clearLookup(); strip?.replaceChildren(); return; }
            strip?.replaceChildren();
            input.value = value;
            runLookup(value, false, false);
        });
        form.hidden = false;
        const saved = remembered.zip();
        if (saved !== null) {
            input.value = saved;
            runLookup(saved, location.hash === answerFragment, false);
        }
    };

    // The members card of a bill page or a vote page holds no ZIP or member data in the cached page. A remembered ZIP fills it from the private fragment of the browser API.
    // The kind is "bill" or "vote". It names the card attributes, the fragment marker, and the route. A failed read shows the shared lookup failure text,
    // because the reader already gave a ZIP. Only a reader with no ZIP sees the prompt.
    const initRecordMembers = (remembered, card, strip, kind) => {
        const api = strip?.dataset.membersApi;
        const body = card.querySelector(`[data-${kind}-members-body]`);
        const prompt = card.querySelector(`[data-${kind}-members-prompt]`);
        let request = 0;
        const clear = () => { request++; body.replaceChildren(); prompt.hidden = false; };
        const fail = (zip, status) => { body.replaceChildren(memberFailure(zip, status, strip.dataset.officialsPage)); prompt.hidden = true; };
        const run = async () => {
            const zip = remembered.zip();
            if (!validZip(zip) || !api) { clear(); return; }
            const mine = ++request;
            try {
                const response = await fetch(`${api}/v1/zip-districts/${zip}/${kind}s/${encodeURIComponent(card.dataset.recordId)}`, { headers: { Accept: "text/html" } });
                if (!response.ok) { if (mine === request) fail(zip, response.status); return; }
                if (!/^text\/html(?:;|$)/i.test(response.headers.get("Content-Type") ?? "")) throw new TypeError("Invalid record members answer.");
                const template = document.createElement("template");
                template.innerHTML = await response.text();
                const fragment = template.content.firstElementChild;
                if (mine !== request) return;
                if (template.content.childElementCount !== 1 || !fragment.matches(`div[data-${kind}-members-fragment]`)
                    || fragment.dataset.zcta !== zip || fragment.dataset.recordId !== card.dataset.recordId || !positiveRelease(fragment.dataset.release))
                    throw new TypeError("Invalid record members answer.");
                // The page covers one release. A fragment of another release would disagree with it.
                if (fragment.dataset.release !== card.dataset.release) { clear(); return; }
                body.replaceChildren(fragment);
                prompt.hidden = true;
            } catch {
                if (mine === request) fail(zip, 0);
            }
        };
        remembered.onForget(clear);
        remembered.onChange(run);
        run();
    };

    const initMemberStrip = (remembered, slot) => {
        if (document.querySelector("[data-zip-api]")) return;
        const api = slot.dataset.membersApi;
        if (!api) return;
        followMemberSlot(remembered, slot, {
            api,
            view: "strip",
            frame: nodes => {
                const content = document.createElement("div");
                content.className = "members-strip-content";
                const controls = document.createElement("div");
                controls.className = "members-strip-nav";
                controls.append(...nodes);
                content.append(controls);
                return content;
            },
            nodeOf: fragment => fragment.firstElementChild.cloneNode(true)
        });
    };

    const initIssueMembers = (remembered, slot) => {
        const api = slot.dataset.membersApi;
        const issue = slot.dataset.issueMembers;
        const prompt = document.querySelector("[data-issue-members-prompt]");
        if (!api || !issue) return;
        followMemberSlot(remembered, slot, {
            api,
            view: "issue",
            issue,
            sinceMarker: false,
            frame: nodes => {
                const content = document.createElement("div");
                content.append(...nodes);
                return content;
            },
            nodeOf: fragment => fragment,
            // The vote list of the page covers one release and Congress. A fragment of another release or Congress would disagree with it.
            accepts: fragment => fragment.dataset.release === slot.dataset.release && (!slot.dataset.congress || fragment.dataset.congress === slot.dataset.congress),
            onShow: () => {
                if (prompt) prompt.hidden = true;
                slot.hidden = false;
            },
            onClear: () => {
                slot.hidden = true;
                if (prompt) prompt.hidden = false;
            }
        });
    };

    // The official page section holds no ZIP or member data in the cached page. A remembered ZIP fills it from the private fragment of the browser API.
    // The section stays hidden when the remembered ZIP has no seat of this official, because the fragment is then empty.
    const initOfficialMembers = (remembered, slot) => {
        const api = slot.dataset.membersApi;
        const official = slot.dataset.officialMembers;
        const section = slot.closest("[data-official-members-section]");
        if (!api || !official || !section) return;
        followMemberSlot(remembered, slot, {
            api,
            view: "official",
            official,
            sinceMarker: false,
            // A failed lookup says nothing about this official, so the section stays hidden.
            hideOnFailure: true,
            frame: nodes => {
                const content = document.createElement("div");
                content.append(...nodes);
                return content;
            },
            nodeOf: fragment => fragment,
            // An empty fragment means that the remembered ZIP has no seat of this official. A fragment of another release would disagree with the page.
            accepts: fragment => fragment.dataset.release === slot.dataset.release && fragment.childElementCount > 0,
            onShow: () => { section.hidden = false; },
            onClear: () => { section.hidden = true; }
        });
    };

    const pagers = new WeakMap();
    const pagedTables = [];
    let printing = false;
    const showRows = (table, toFirstPage) => {
        const rows = Array.from(table.tBodies[0].rows);
        const matching = rows.filter(row => !row.hasAttribute("data-filtered-out"));
        const pager = pagers.get(table);
        if (pager && toFirstPage) pager.page = 0;
        const size = pager && !printing ? pager.size : Infinity;
        const pageCount = Math.max(1, Math.ceil(matching.length / size));
        if (pager && !printing) pager.page = Math.min(pager.page, pageCount - 1);
        const first = pager && !printing ? pager.page * size : 0;
        const visible = new Set(matching.slice(first, first + size));
        for (const row of rows) row.hidden = !visible.has(row);
        pager?.update(matching.length, first, visible.size, pageCount);
    };

    const reducedMotion = matchMedia("(prefers-reduced-motion: reduce)");
    const showAllRowsWhilePrinting = isPrinting => {
        printing = isPrinting;
        for (const table of pagedTables) showRows(table, false);
    };
    window.addEventListener("beforeprint", () => showAllRowsWhilePrinting(true));
    window.addEventListener("afterprint", () => showAllRowsWhilePrinting(false));

    const initPaged = table => {
        const noun = table.dataset.rowsNoun;
        const nav = document.createElement("nav");
        nav.className = "pager";
        nav.setAttribute("aria-label", `Pages of ${table.caption.textContent}`);
        table.tabIndex = -1;
        const summary = document.createElement("p");
        summary.setAttribute("role", "status");
        const links = document.createElement("div");
        links.className = "pager-links";
        const [previous, next] = ["Previous", "Next"].map(text => {
            const button = document.createElement("button");
            button.type = "button";
            button.className = text === "Next" ? "btn btn-small btn-primary" : "btn btn-small";
            button.textContent = text;
            return button;
        });
        links.append(previous, next);
        nav.append(summary, links);
        const pager = {
            page: 0,
            size: Number(table.dataset.paged),
            update: (total, first, shown, pageCount) => {
                nav.hidden = pageCount === 1;
                summary.textContent = `Showing ${first + 1} to ${first + shown} of ${total.toLocaleString("en-US")} ${noun}`;
                previous.disabled = pager.page === 0;
                next.disabled = pager.page === pageCount - 1;
            }
        };
        const turn = (button, step) => button.addEventListener("click", () => {
            pager.page += step;
            showRows(table, false);
            table.scrollIntoView({ block: "start", behavior: reducedMotion.matches ? "auto" : "smooth" });
            table.focus({ preventScroll: true });
        });
        turn(previous, -1);
        turn(next, 1);
        pagers.set(table, pager);
        pagedTables.push(table);
        table.closest(".table-wrap").after(nav);
        showRows(table, true);
    };

    // A link to a line inside a closed fold, such as a member row Source link, does nothing without this. Without script the anchor still reaches the page.
    const hashTarget = () => {
        try { return document.getElementById(decodeURIComponent(location.hash.slice(1))); }
        catch { return null; }
    };
    const openFoldOfHash = () => {
        const closed = [];
        for (let fold = hashTarget()?.closest("details"); fold; fold = fold.parentElement?.closest("details")) if (!fold.open) closed.push(fold);
        if (closed.length === 0) return;
        closed.forEach(fold => { fold.open = true; });
        hashTarget().scrollIntoView();
    };
    window.addEventListener("hashchange", openFoldOfHash);
    openFoldOfHash();

    // The member finder of a vote page filters the folded member table while the reader types or picks a cast or a party. Without script the GET form reloads the page.
    // The server marks the form only when the table holds every member. A name matches any part of the member name, ignoring case, as VoteMemberFilter does.
    // The cast and party keys of each row come from VoteMemberFilter.Keys. The filtered rows go through the pager of the table.
    const initMemberFinder = form => {
        const section = form.closest(".vote-members");
        const table = section.querySelector("table[data-paged]");
        const status = section.querySelector("[data-member-finder-status]");
        if (!table || !status) return;
        const rows = Array.from(table.tBodies[0].rows);
        const name = form.elements.namedItem("q");
        const cast = form.elements.namedItem("cast");
        const party = form.elements.namedItem("party");
        const keysOf = row => new Map((row.dataset.filterKeys ?? "").split(" ").map(key => {
            const split = key.indexOf(":");
            return [key.slice(0, split), decodeURIComponent(key.slice(split + 1))];
        }));
        const keys = new Map(rows.map(row => [row, keysOf(row)]));
        const apply = () => {
            const wanted = name.value.trim().toLowerCase();
            let shown = 0;
            for (const row of rows) {
                const rowKeys = keys.get(row);
                const hides = (wanted !== "" && !row.cells[0].textContent.toLowerCase().includes(wanted))
                    || (cast.value !== "" && rowKeys.get("cast") !== cast.value)
                    || (party.value !== "" && rowKeys.get("party") !== party.value);
                row.toggleAttribute("data-filtered-out", hides);
                if (!hides) shown++;
            }
            showRows(table, true);
            table.closest(".table-wrap").hidden = shown === 0;
            status.textContent = `${shown} of ${rows.length} tracked members shown.` + (shown === 0 ? ` ${status.dataset.noMatch}` : "");
        };
        name.addEventListener("input", apply);
        cast.addEventListener("change", apply);
        party.addEventListener("change", apply);
        form.addEventListener("submit", event => { event.preventDefault(); apply(); });
        form.querySelector(".vote-filter-actions").hidden = true;
    };

    // The filter fold of a record list opens on a wide screen, as the approved mockup does. The server opens it when a filter is active.
    if (matchMedia("(min-width: 720px)").matches) document.querySelectorAll("details[data-wide-open]").forEach(fold => { fold.open = true; });

    // The vote map shows the shapes on a wide screen and the tiles on a narrow one. The view buttons override that choice. Without script the width chooses.
    const initVoteMapView = map => {
        const views = map.querySelector("[data-vote-map-views]");
        const buttons = Array.from(views.querySelectorAll("[data-view]"));
        const show = view => {
            map.dataset.view = view;
            buttons.forEach(button => button.setAttribute("aria-pressed", String(button.dataset.view === view)));
        };
        buttons.forEach(button => button.addEventListener("click", () => show(button.dataset.view)));
        const narrow = matchMedia("(max-width: 719px)");
        const showWidthView = () => show(narrow.matches ? "tile" : "shape");
        narrow.addEventListener("change", showWidthView);
        showWidthView();
        views.hidden = false;
        initVoteMapColors(map);
        initVoteMapPartyBreaks(map);
    };
    // The color buttons switch between coloring by vote and by party. Without script the map stays colored by vote.
    const initVoteMapColors = map => {
        const colors = map.querySelector("[data-vote-map-colors]");
        if (!colors) return;
        const buttons = Array.from(colors.querySelectorAll("[data-color]"));
        const show = color => {
            map.dataset.color = color;
            buttons.forEach(button => button.setAttribute("aria-pressed", String(button.dataset.color === color)));
        };
        buttons.forEach(button => button.addEventListener("click", () => show(button.dataset.color)));
        show("vote");
        colors.hidden = false;
    };
    // Party-break rings are an optional overlay. Without script, every map keeps its existing fills and no rings.
    const initVoteMapPartyBreaks = map => {
        const control = map.querySelector("[data-vote-map-breaks]");
        if (!control) return;
        const button = control.querySelector("[data-party-breaks]");
        const key = map.querySelector("[data-party-breaks-key]");
        button.addEventListener("click", () => {
            const enabled = map.dataset.partyBreaks !== "true";
            map.dataset.partyBreaks = String(enabled);
            button.setAttribute("aria-pressed", String(enabled));
            if (key) key.hidden = !enabled;
        });
        control.hidden = false;
    };

    const initMaps = map => {
        if (map.querySelector("[data-vote-map-views]")) initVoteMapView(map);
        const details = map.querySelector("[data-maps-details]");
        const templates = new Map(Array.from(map.querySelectorAll("template[data-map-detail]"), item => [item.dataset.memberHref, item]));
        const links = Array.from(map.querySelectorAll(".vote-map-frame a"));
        let dismissing = false;
        details.addEventListener("beforetoggle", event => {
            if (event.newState !== "closed") return;
            // Native dismissal restores seat focus before it finishes closing the popover.
            dismissing = true;
            setTimeout(() => { dismissing = false; }, 0);
        });
        const show = link => {
            const template = templates.get(link.getAttribute("href"));
            if (!template) return;
            const changed = details.dataset.memberHref !== template.dataset.memberHref;
            if (changed) {
                details.replaceChildren(template.content.cloneNode(true));
                details.dataset.memberHref = template.dataset.memberHref;
            }
            links.forEach(item => item.toggleAttribute("data-selected", item.getAttribute("href") === template.dataset.memberHref));
            if (!details.matches(":popover-open")) details.showPopover();
        };
        details.addEventListener("toggle", event => {
            if (event.newState === "closed") links.forEach(link => link.removeAttribute("data-selected"));
        });
        map.querySelectorAll(".vote-map-frame svg").forEach(svg => {
            const seats = Array.from(svg.querySelectorAll("a"));
            seats.forEach((link, index) => {
                link.tabIndex = index === 0 ? 0 : -1;
                link.addEventListener("pointerenter", event => { if (event.pointerType !== "touch") show(link); });
                link.addEventListener("focus", () => {
                    seats.forEach(item => { item.tabIndex = item === link ? 0 : -1; });
                    if (!dismissing) show(link);
                });
                link.addEventListener("click", event => {
                    if (event.ctrlKey || event.metaKey || event.shiftKey || event.altKey || event.button !== 0) return;
                    event.preventDefault();
                    show(link);
                    // Touch keeps the seat selected. The explicit member link remains a normal link.
                    if (event.detail === 0) details.querySelector("a")?.focus();
                });
                link.addEventListener("keydown", event => {
                    let next;
                    if (event.key === "ArrowRight" || event.key === "ArrowDown") next = (index + 1) % seats.length;
                    else if (event.key === "ArrowLeft" || event.key === "ArrowUp") next = (index + seats.length - 1) % seats.length;
                    else if (event.key === "Home") next = 0;
                    else if (event.key === "End") next = seats.length - 1;
                    else return;
                    event.preventDefault();
                    seats[next].focus();
                });
            });
        });
    };
    document.querySelectorAll("[data-maps]").forEach(initMaps);
    document.querySelectorAll("[data-maps-form]").forEach(form => {
        const layer = form.querySelector('[name="layer"]');
        const fields = { issue: "issueActivity", measure: "issueActivity", end: "termEnds" };
        const update = () => Object.entries(fields).forEach(([name, wanted]) => {
            const field = form.querySelector(`[data-maps-field="${name}"]`);
            field.hidden = layer.value !== wanted && !field.querySelector('[aria-invalid="true"]');
        });
        layer.addEventListener("change", update);
        update();
    });

    document.querySelectorAll("table[data-paged]").forEach(initPaged);
    document.querySelectorAll("form[data-member-finder]").forEach(initMemberFinder);
    document.querySelectorAll("[data-vote-map]:not([data-compact])").forEach(initVoteMapView);
    document.querySelectorAll("[data-report-api]").forEach(initReportForm);
    const rememberedMembers = initRememberedMembers();
    document.querySelectorAll("[data-zip-api]").forEach(form => initZipLookup(form, rememberedMembers));
    const membersStrip = document.querySelector("#members-strip");
    if (membersStrip) initMemberStrip(rememberedMembers, membersStrip);
    document.querySelectorAll("[data-bill-members]").forEach(card => initRecordMembers(rememberedMembers, card, membersStrip, "bill"));
    document.querySelectorAll("[data-vote-members]").forEach(card => initRecordMembers(rememberedMembers, card, membersStrip, "vote"));
    const issueMembers = document.querySelector("[data-issue-members]");
    if (issueMembers) initIssueMembers(rememberedMembers, issueMembers);
    document.querySelectorAll("[data-official-members]").forEach(slot => initOfficialMembers(rememberedMembers, slot));
})();
