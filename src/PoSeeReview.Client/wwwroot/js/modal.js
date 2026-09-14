// Native <dialog> helper.
//
// Blazor has no ShowModal(), and showModal() is the only way to get the platform's own modal
// semantics — a focus trap, Escape handling, an inert background, and focus restored to whatever
// was focused before the dialog opened. A div-based modal has to reimplement all four badly, and
// the version this replaces had none of them: Escape did nothing, Tab walked out into the page
// behind the scrim, and closing dropped focus on <body>.
//
// The close event fires for every route out of the dialog — Escape, dialog.close(), form
// method=dialog — so it is the one place that needs to tell .NET the dialog is gone. Listening
// with `once: true` and clearing the ref keeps a reopen from double-invoking.

export function open(dialog, dotNetRef) {
    if (!dialog || typeof dialog.showModal !== 'function') {
        // No <dialog> support: the element renders as a plain block, so the panel is still
        // visible and usable. Degrading to "modal without a trap" beats not opening at all.
        return false;
    }

    if (dialog.open) {
        return true;
    }

    dialog.__poseeCloseRef = dotNetRef;
    dialog.addEventListener('close', () => {
        const ref = dialog.__poseeCloseRef;
        dialog.__poseeCloseRef = null;
        if (ref) {
            ref.invokeMethodAsync('OnNativeCloseAsync');
        }
    }, { once: true });

    dialog.showModal();
    return true;
}

export function close(dialog) {
    if (dialog && dialog.open) {
        dialog.close();
    }
}
