window.focusCardRow = (key) => {
    const row = document.querySelector(`tr[data-card-key="${CSS.escape(key)}"]`);
    if (row) {
        row.focus({ preventScroll: false });
        row.scrollIntoView({ block: 'nearest' });
    }
};
