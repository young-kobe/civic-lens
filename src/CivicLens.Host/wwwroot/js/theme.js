// Applies a stored theme choice before first paint. No stored choice follows the system setting.
try {
    const choice = localStorage.getItem("civic-lens-theme");
    if (choice === "light" || choice === "dark") document.documentElement.dataset.theme = choice;
} catch { /* Storage can be unavailable; the system theme still applies. */ }
