export function open(dialog, receiver) {
    if (!dialog.isConnected) return;

    const opener = document.activeElement;
    const previousOverflow = document.body.style.overflow;
    dialog.addEventListener("cancel", event => {
        event.preventDefault();
        if (dialog.dataset.busy !== "true") receiver.invokeMethodAsync("RequestClose");
    });

    dialog.showModal();
    document.body.style.overflow = "hidden";

    // Blazor removes the component after closing or navigating away.
    const observer = new MutationObserver(() => {
        if (dialog.isConnected) return;
        observer.disconnect();
        document.body.style.overflow = previousOverflow;
        if (opener?.isConnected) opener.focus({ preventScroll: true });
    });
    observer.observe(document.body, { childList: true, subtree: true });
}
