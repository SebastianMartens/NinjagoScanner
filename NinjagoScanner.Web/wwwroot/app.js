window.focusCardRow = (key) => {
    const row = document.querySelector(`tr[data-card-key="${CSS.escape(key)}"]`);
    if (row) {
        row.focus({ preventScroll: false });
        row.scrollIntoView({ block: 'nearest' });
    }
};

window.scrollCardRowIntoView = (key) => {
    const el = document.querySelector(`[data-card-key="${CSS.escape(key)}"]`);
    if (el) el.closest('tr')?.scrollIntoView({ block: 'nearest' });
};
