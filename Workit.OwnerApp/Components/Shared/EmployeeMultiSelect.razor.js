// <details> opens and closes itself; the browser gives no way to close it when
// the user clicks elsewhere, so listen for that while it is open.
const handlers = new WeakMap();

export function closeOnOutsideClick(details) {
    const onClick = (event) => {
        if (details.open && !details.contains(event.target)) {
            details.open = false;
        }
    };
    const onKey = (event) => {
        if (event.key === "Escape" && details.open) {
            details.open = false;
        }
    };
    document.addEventListener("click", onClick);
    document.addEventListener("keydown", onKey);
    handlers.set(details, { onClick, onKey });
}

export function dispose(details) {
    const h = handlers.get(details);
    if (!h) return;
    document.removeEventListener("click", h.onClick);
    document.removeEventListener("keydown", h.onKey);
    handlers.delete(details);
}
