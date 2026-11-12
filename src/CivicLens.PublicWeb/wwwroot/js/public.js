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
    const markStart = "\uE000", markEnd = "\uE001";
    const months = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
    const formatDay = isoDay => {
        const [year, month, day] = isoDay.slice(0, 10).split("-");
        return `${Number(day)} ${months[Number(month) - 1]} ${year}`;
    };
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
        const hint = form.querySelector("[data-search-hint]");
        const none = form.querySelector("[data-search-none]");
        const optionLabels = name => new Map(Array.from(form.elements.namedItem(name).options, option => [option.value, option.text]));
        const kindLabels = optionLabels("kind");
        const issueLabels = optionLabels("issue");
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
            facts.textContent = [kindLabels.get(hit.kind), ...hit.officials.map(official => official.name), ...hit.issueIds.map(id => issueLabels.get(id) ?? id), hit.eventDate && formatDay(hit.eventDate)]
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
        const resultCountText = (count, hasMore) => {
            if (count === 0) return "No records match.";
            return `${count} ${count === 1 ? "result" : "results"}${hasMore ? " so far" : ""}`;
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
            const mine = ++running;
            if (append) params.set("cursor", nextCursor); else reset();
            hint.hidden = params.toString() !== "";
            none.hidden = true;
            if (params.toString() === "") { status.textContent = ""; return; }
            status.textContent = "Searching.";
            let page;
            try {
                const response = await fetch(`${api}/v1/search?${params}`);
                if (mine !== running) return;
                if (!response.ok) { status.textContent = failure(response); return; }
                page = await response.json();
            }
            catch { if (mine === running) status.textContent = failure({ status: 0 }); return; }
            if (mine !== running) return;
            const items = page.hits.map(resultItem);
            if (append) results.append(...items); else results.replaceChildren(...items);
            nextCursor = page.nextCursor;
            more.hidden = nextCursor === null;
            none.hidden = results.children.length > 0;
            status.textContent = resultCountText(results.children.length, nextCursor !== null);
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
            run(false);
        };
        form.closest("section").hidden = false;
        restoreFromAddress();
        writeAddress(formParams(), true);
        window.addEventListener("popstate", () => { if (location.search !== address) restoreFromAddress(); });
        form.addEventListener("submit", event => { event.preventDefault(); writeAddress(formParams()); run(false); });
        form.addEventListener("input", () => {
            running++;
            dropCursor();
            status.textContent = "";
            hint.hidden = results.children.length > 0 || !none.hidden;
        });
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
        const title = query.get(form.dataset.reportTitleParameter) ?? "";
        const named = title !== "" && title.length <= Number(form.dataset.reportTitleLimit) ? `"${title}", ` : "";
        document.querySelector("[data-report-subject]").textContent = `You are reporting the record ${named}ID ${recordId}, version ${revision}.`;
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

    const memberFragment = (html, zip, issue, expectedView, sinceRelease = null) => {
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
            || !positiveRelease(fragment.dataset.release) || !positiveRelease(fragment.dataset.congress)
            || (fragment.dataset.sinceRelease !== "" && !positiveRelease(fragment.dataset.sinceRelease))
            || fragment.dataset.sinceRelease !== (sinceRelease === null ? "" : String(sinceRelease))
            || (hasSince !== hasCount)
            || (hasCount && (typeof count !== "string" || count.length > 19 || !/^(0|[1-9][0-9]*)$/.test(count) || BigInt(count) > 9223372036854775807n))
            || expectedView !== "strip" && expectedView !== "issue" && (stripTemplates.length !== 1 || stripTemplateContent.childElementCount !== 1
                || !stripTemplateContent.firstElementChild.matches("div[data-member-strip-content]"))
            || (expectedView === "strip"
                ? fragment.childElementCount !== 1 || !fragment.firstElementChild.matches("div[data-member-strip-content]")
                : expectedView === "issue"
                    ? !fragment.querySelector(".shared-vote-rows, [data-member-votes-empty]")
                    : !fragment.querySelector(".zip-member-cards > li")))
            throw new TypeError("Invalid ZIP lookup answer.");
        return fragment;
    };

    const readMemberFragment = async (api, zip, issue, view, sinceRelease, signal) => {
        const params = new URLSearchParams();
        if (view !== "") params.set("view", view);
        if (issue !== null) params.set("issue", issue);
        if (sinceRelease !== null) params.set("sinceRelease", String(sinceRelease));
        const query = params.size === 0 ? "" : `?${params}`;
        const response = await fetch(`${api}/v1/zip-districts/${zip}/members${query}`, { headers: { Accept: "text/html" }, signal });
        if (!response.ok) return { status: response.status, fragment: null };
        if (!/^text\/html(?:;|$)/i.test(response.headers.get("Content-Type") ?? ""))
            throw new TypeError("Invalid ZIP lookup content type.");
        const html = await response.text();
        signal.throwIfAborted();
        return { status: response.status, fragment: memberFragment(html, zip, issue, view, sinceRelease) };
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
                const { status, fragment } = await readMemberFragment(api, zip, options.issue ?? null, view, marker, abort.signal);
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
    const followMemberSlot = (remembered, slot, { api, view, issue = null, sinceMarker = true, frame, nodeOf, accepts, onShow, onClear }) => {
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
            onFailure: (call, status) => slot.replaceChildren(frame(memberFailureWithRetry(call.zip, status, slot.dataset.officialsPage, () => run(call.zip)))),
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
            loader.run(zip, sinceMarker ? { issue } : { issue, since: null });
        };
        remembered.onForget(clear);
        remembered.onChange(zip => { if (zip === null) clear(); else { slot.replaceChildren(); run(zip); } });
        const initial = remembered.zip();
        if (initial !== null) run(initial);
    };

    const initZipLookup = (form, remembered) => {
        const api = form.dataset.zipApi;
        const view = form.dataset.zipView === "compare" ? "compare" : "";
        const panel = document.querySelector("[data-zip-results]");
        const body = panel.querySelector("[data-zip-body]");
        const notes = panel.querySelector("[data-zip-notes]");
        const input = form.elements.namedItem("zip");
        const submit = form.querySelector("[type=submit]");
        const problem = form.querySelector("[data-zip-error]");
        const heading = panel.querySelector("h2");
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
            view,
            onFragment: (fragment, call) => {
                showAnswer(fragment);
                mountStrip(fragment);
                if (!call.filtered) {
                    renderedSinceRelease = call.marker;
                    if (!remembered.complete(call.zip, Number(fragment.dataset.release), call.startingState, call.persist)) return false;
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

    // The bill page card holds no ZIP or member data in the cached page. A remembered ZIP fills it from the private fragment of the browser API.
    // A failed read shows the shared lookup failure text, because the reader already gave a ZIP. Only a reader with no ZIP sees the prompt.
    const initBillMembers = (remembered, card, strip) => {
        const api = strip?.dataset.membersApi;
        const body = card.querySelector("[data-bill-members-body]");
        const prompt = card.querySelector("[data-bill-members-prompt]");
        let request = 0;
        const clear = () => { request++; body.replaceChildren(); prompt.hidden = false; };
        const fail = (zip, status) => { body.replaceChildren(memberFailure(zip, status, strip.dataset.officialsPage)); prompt.hidden = true; };
        const run = async () => {
            const zip = remembered.zip();
            if (!validZip(zip) || !api) { clear(); return; }
            const mine = ++request;
            try {
                const response = await fetch(`${api}/v1/zip-districts/${zip}/bills/${encodeURIComponent(card.dataset.recordId)}`, { headers: { Accept: "text/html" } });
                if (!response.ok) { if (mine === request) fail(zip, response.status); return; }
                if (!/^text\/html(?:;|$)/i.test(response.headers.get("Content-Type") ?? "")) throw new TypeError("Invalid bill members answer.");
                const template = document.createElement("template");
                template.innerHTML = await response.text();
                const fragment = template.content.firstElementChild;
                if (mine !== request) return;
                if (template.content.childElementCount !== 1 || !fragment.matches("div[data-bill-members-fragment]")
                    || fragment.dataset.zcta !== zip || fragment.dataset.recordId !== card.dataset.recordId || !positiveRelease(fragment.dataset.release))
                    throw new TypeError("Invalid bill members answer.");
                // The backers and votes of the page cover one release. A fragment of another release would disagree with them.
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
        nav.setAttribute("aria-label", `Pages of ${table.caption.textContent.replace(`. ${table.dataset.defaultOrder}.`, "")}`);
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

    const initRecordFilters = section => {
        const listPane = section.querySelector("[data-list-pane]");
        const issuesPane = section.querySelector("[data-issues-pane]");
        const table = listPane.querySelector("table");
        const rows = Array.from(listPane.querySelectorAll("tbody tr"));
        if (rows.length === 0) return;
        const tabs = section.querySelector("[data-record-filters]");
        const tabButtons = tabs ? Array.from(tabs.querySelectorAll("[data-filter-value]")) : [];
        const counts = Array.from(section.querySelectorAll("[data-count-filter]"));
        const status = listPane.querySelector("[data-filter-status]");
        const notice = listPane.querySelector("[data-filter-notice]");
        const clearButton = notice?.querySelector("[data-filter-clear]");
        let kind = "", issue = null, issuesOpen = false;
        const isIssuesTab = button => button.dataset.filterValue === issuesPane?.dataset.issuesPane;
        const matchesIssue = (row, key) => (row.dataset.filterKeys ?? "").split(" ").some(rowKey => rowKey === key || rowKey.endsWith(":" + key));
        const hides = row => (kind !== "" && !row.classList.contains(kind)) || (issue !== null && !matchesIssue(row, issue.key));
        const apply = () => {
            for (const row of rows) row.toggleAttribute("data-filtered-out", hides(row));
            const shown = rows.filter(row => !row.hasAttribute("data-filtered-out")).length;
            showRows(table, true);
            listPane.hidden = issuesOpen;
            if (issuesPane) issuesPane.hidden = !issuesOpen;
            for (const button of tabButtons) button.setAttribute("aria-pressed", String(isIssuesTab(button) ? issuesOpen : !issuesOpen && button.dataset.filterValue === kind));
            for (const count of counts) count.querySelector("button").setAttribute("aria-pressed", String(count === issue?.count));
            if (notice) {
                notice.querySelector("[data-filter-text]").textContent = issue?.shows ?? "";
                notice.hidden = issue === null;
            }
            const total = rows.length;
            const filtered = kind !== "" || issue !== null;
            status.textContent = !filtered ? "" : `${shown.toLocaleString("en-US")} of ${total.toLocaleString("en-US")} ${total === 1 ? "record matches" : "records match"}`
                + (shown === 0 ? `. No record matches both filters. Use ${clearButton.textContent} to see every record.` : "");
        };
        for (const count of counts) {
            const button = document.createElement("button");
            button.type = "button";
            button.className = "count-filter";
            button.textContent = count.textContent;
            button.setAttribute("aria-label", count.dataset.filterLabel);
            count.replaceChildren(button);
            button.addEventListener("click", () => {
                issue = issue?.count === count ? null : { key: count.dataset.countFilter, shows: count.dataset.filterShows, count };
                issuesOpen = false;
                apply();
            });
        }
        for (const button of tabButtons) button.addEventListener("click", () => {
            issuesOpen = isIssuesTab(button);
            if (!issuesOpen) kind = button.dataset.filterValue;
            apply();
        });
        clearButton?.addEventListener("click", () => {
            const pressed = issue?.count?.querySelector("button");
            kind = "";
            issue = null;
            apply();
            pressed?.focus();
        });
        const query = new URLSearchParams(location.search);
        const wanted = tabButtons.find(button => button.dataset.filterValue === query.get("tab"));
        if (wanted) {
            issuesOpen = isIssuesTab(wanted);
            if (!issuesOpen) kind = wanted.dataset.filterValue;
        }
        const wantedIssue = query.get("issue");
        const issueName = wantedIssue === null ? null : Array.from(section.querySelectorAll("[data-issue-name]")).find(name => name.dataset.issueName === wantedIssue);
        if (issueName) issue = { key: wantedIssue, shows: issueName.dataset.issueNotice, count: null };
        if (tabs) tabs.hidden = false;
        apply();
    };

    const codeUnitOrder = (a, b) => (a < b ? -1 : a > b ? 1 : 0);
    const sortKinds = {
        text: { first: "asc", words: { asc: "A to Z", desc: "Z to A" }, compare: (a, b) => codeUnitOrder(a.toLowerCase(), b.toLowerCase()) },
        number: { first: "desc", words: { desc: "most first", asc: "fewest first" }, ranked: { desc: "highest first", asc: "lowest first" }, compare: (a, b) => Number(a) - Number(b) },
        date: { first: "desc", words: { desc: "newest first", asc: "oldest first" }, compare: codeUnitOrder }
    };
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
            const kind = document.createElement("span");
            kind.className = "cell-note";
            kind.textContent = source.kinds[hit.kind];
            cell.append(link, kind);
            if (hit.billLine) {
                const line = document.createElement("span");
                line.className = "bill-line";
                line.textContent = hit.billLine;
                cell.append(line);
            }
            return cell;
        },
        officials: hit => hit.officials.map(official => official.name).join(", "),
        date: hit => formatDay(hit.eventDate ?? hit.firstPublishedAtUtc)
    };
    const hitRow = (hit, source, headers) => {
        const row = document.createElement("tr");
        row.setAttribute("role", "row");
        source.columns.forEach((column, index) => {
            const content = hitCells[column](hit, source);
            const cell = content instanceof HTMLTableCellElement ? content : document.createElement("td");
            if (cell !== content) {
                cell.className = headers[index].className;
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
        const isDefault = (header, direction) => header.dataset.sortDefault !== undefined && direction === sortKinds[header.dataset.sort].first;
        const label = (header, direction) => {
            if (isDefault(header, direction)) return table.dataset.defaultOrder;
            const kind = sortKinds[header.dataset.sort];
            return header.dataset.sortLabel === undefined ? `${names.get(header)}, ${kind.words[direction]}` : `${header.dataset.sortLabel}, ${kind.ranked[direction]}`;
        };
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
                if (!isDefault(header, direction)) select.add(new Option(label(header, direction), `${headers.indexOf(header)}:${direction}`));
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
            select.value = state && !isDefault(state.header, state.direction) ? `${headers.indexOf(state.header)}:${state.direction}` : "";
        };
        const lineNote = document.createElement("p");
        lineNote.className = "muted small";
        const restore = () => {
            running++;
            lineNote.remove();
            body.replaceChildren(...staticRows);
            showRows(table, true);
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
            showRows(table, true);
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
            if (append) body.append(...rows); else { body.replaceChildren(...rows); lineNote.remove(); }
            if (page.hits.some(hit => hit.billLine) && !table.closest(".record-list")?.parentElement.querySelector(".records-lead")?.textContent.includes(source.billLineNote)) {
                lineNote.textContent = source.billLineNote;
                bar.append(lineNote);
            }
            shown = append ? shown + rows.length : rows.length;
            nextCursor = page.nextCursor;
            more.hidden = nextCursor === null;
            if (pager) pager.hidden = true;
            commit(state);
            const total = source.total, count = Math.min(shown, total);
            status.textContent = `Showing ${count.toLocaleString("en-US")} of ${total.toLocaleString("en-US")} ${total === 1 ? "record" : "records"}, sorted by ${label(current.header, current.direction)}. Sorted rows leave out the kind icon, the party badge, the vote counts, and the bill line of a vote. An action that the search index stored before the bill line existed shows no line.`;
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
    };

    document.querySelectorAll("table[data-paged]").forEach(initPaged);
    document.querySelectorAll("[data-vote-map]").forEach(initVoteMapView);
    document.querySelectorAll("[data-records]").forEach(initRecordFilters);
    document.querySelectorAll("[data-officials-filters]").forEach(initOfficialsFilter);
    document.querySelectorAll("[data-search-api]").forEach(initSearchService);
    document.querySelectorAll("[data-report-api]").forEach(initReportForm);
    const rememberedMembers = initRememberedMembers();
    document.querySelectorAll("[data-zip-api]").forEach(form => initZipLookup(form, rememberedMembers));
    const membersStrip = document.querySelector("#members-strip");
    if (membersStrip) initMemberStrip(rememberedMembers, membersStrip);
    document.querySelectorAll("[data-bill-members]").forEach(card => initBillMembers(rememberedMembers, card, membersStrip));
    const issueMembers = document.querySelector("[data-issue-members]");
    if (issueMembers) initIssueMembers(rememberedMembers, issueMembers);
    new Set(Array.from(document.querySelectorAll("th[data-sort]"), header => header.closest("table"))).forEach(initSortable);
})();
