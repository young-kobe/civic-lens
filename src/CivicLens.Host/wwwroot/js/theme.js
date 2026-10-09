try {
    const choice = localStorage.getItem("civic-lens-theme");
    if (choice === "light" || choice === "dark") document.documentElement.dataset.theme = choice;
} catch {}
