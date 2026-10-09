document.querySelectorAll("[data-list-search]").forEach(input => {
    const list = document.getElementById(input.dataset.listSearch);
    const status = document.querySelector(`[data-list-status="${input.dataset.listSearch}"]`);
    if (!list) return;
    const items = Array.from(list.querySelectorAll("[data-list-item]"));
    const filter = () => {
        const query = input.value.trim().toLocaleLowerCase();
        let visible = 0;
        items.forEach(item => {
            item.hidden = !item.textContent.toLocaleLowerCase().includes(query);
            if (!item.hidden) visible++;
        });
        if (status) status.textContent = query ? `${visible} of ${items.length} sources match your search.` : `${items.length} official sources.`;
    };
    input.addEventListener("input", filter);
    filter();
});
